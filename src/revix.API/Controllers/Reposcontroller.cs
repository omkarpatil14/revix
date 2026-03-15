using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Revix.Core.Interfaces;
using Revix.Infrastructure;
using System.Security.Claims;

namespace Revix.API.Controllers;

[ApiController]
[Route("api/repos")]
[Authorize]
public class ReposController : ControllerBase
{
    private readonly RevixDbContext _db;
    private readonly IGitHubService _github;
    private readonly IConfiguration _config;
    private readonly ITokenEncryptionService _encryption;

    public ReposController(
        RevixDbContext db,
        IGitHubService github,
        IConfiguration config,
        ITokenEncryptionService encryption)
    {
        _db         = db;
        _github     = github;
        _config     = config;
        _encryption = encryption;
    }

    // ── POST /api/repos/sync ──────────────────────────────────────────────────
    // Auto-syncs ALL GitHub repos for the logged-in user
    // Called once after login — connects every repo automatically with a webhook
    [HttpPost("sync")]
    public async Task<IActionResult> SyncRepos()
    {
        var (user, accessToken) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        List<Revix.Core.Models.GitHubRepo> githubRepos;
        try
        {
            githubRepos = await _github.GetUserReposAsync(accessToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to fetch GitHub repos: {ex.Message}");
            return StatusCode(500, new { message = "Failed to fetch repositories from GitHub." });
        }

        // Get all already-connected repo names for this user
        var existingRepoNames = await _db.Repositories
            .Where(r => r.UserId == user.Id)
            .Select(r => r.RepoName)
            .ToHashSetAsync();

        var webhookUrl    = _config["App:WebhookUrl"]!;
        var webhookSecret = _config["GitHub:WebhookSecret"]!;

        var newRepos      = new List<Revix.Core.Entities.Repository>();
        var skippedCount  = 0;
        var failedRepos   = new List<string>();

        foreach (var ghRepo in githubRepos)
        {
            // Already connected — skip
            if (existingRepoNames.Contains(ghRepo.Name))
            {
                skippedCount++;
                continue;
            }

            long webhookId = 0;
            try
            {
                webhookId = await _github.CreateWebhookAsync(
                    user.GitHubUsername,
                    ghRepo.Name,
                    webhookUrl,
                    webhookSecret,
                    accessToken);

                Console.WriteLine($"✅ Webhook created for {ghRepo.Name} (id: {webhookId})");
            }
            catch (Exception ex)
            {
                // Don't block the whole sync — save repo without webhook
                // Webhook can be retried later
                Console.WriteLine($"⚠️ Could not create webhook for {ghRepo.Name}: {ex.Message}");
                failedRepos.Add(ghRepo.Name);
            }

            newRepos.Add(new Revix.Core.Entities.Repository
            {
                Id              = Guid.NewGuid(),
                UserId          = user.Id,
                GitHubRepoId    = ghRepo.Id.ToString(),
                RepoName        = ghRepo.Name,
                IsEnabled       = true,
                GitHubWebhookId = webhookId,
                CreatedAt       = DateTime.UtcNow
            });
        }

        if (newRepos.Any())
        {
            await _db.Repositories.AddRangeAsync(newRepos);
            await _db.SaveChangesAsync();
        }

        return Ok(new
        {
            Total        = githubRepos.Count,
            Added        = newRepos.Count,
            Skipped      = skippedCount,
            WebhookFailed = failedRepos,
            Message      = newRepos.Count == 0
                ? "All repositories are already connected."
                : $"Successfully connected {newRepos.Count} new repository/repositories."
        });
    }

    // ── GET /api/repos ────────────────────────────────────────────────────────
    // Returns all connected repos for the logged-in user (from our DB)
    [HttpGet]
    public async Task<IActionResult> GetRepos()
    {
        var (user, _) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        var repos = await _db.Repositories
            .Where(r => r.UserId == user.Id)
            .Select(r => new
            {
                r.Id,
                r.RepoName,
                r.IsEnabled,
                r.CreatedAt,
                TotalReviews   = r.Reviews.Count,
                LastReviewedAt = r.Reviews
                    .OrderByDescending(rv => rv.CreatedAt)
                    .Select(rv => (DateTime?)rv.CreatedAt)
                    .FirstOrDefault()
            })
            .OrderByDescending(r => r.LastReviewedAt)
            .ToListAsync();

        return Ok(repos);
    }

    // ── GET /api/repos/{id} ───────────────────────────────────────────────────
    // Returns a single repo with full review history and stats
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetRepo(Guid id)
    {
        var (user, _) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        var repo = await _db.Repositories
            .Where(r => r.Id == id && r.UserId == user.Id)
            .Select(r => new
            {
                r.Id,
                r.RepoName,
                r.IsEnabled,
                r.CreatedAt,
                TotalReviews  = r.Reviews.Count,
                TotalComments = r.Reviews.Sum(rv => rv.CommentsPosted),
                TotalBugs     = r.Reviews
                    .SelectMany(rv => rv.ReviewComments)
                    .Count(rc => rc.Severity == "Bug"),
                TotalWarnings = r.Reviews
                    .SelectMany(rv => rv.ReviewComments)
                    .Count(rc => rc.Severity == "Warning"),
                Reviews = r.Reviews
                    .OrderByDescending(rv => rv.CreatedAt)
                    .Take(20)
                    .Select(rv => new
                    {
                        rv.Id,
                        rv.PrNumber,
                        rv.PrTitle,
                        rv.FilesReviewed,
                        rv.CommentsPosted,
                        rv.CreatedAt
                    })
            })
            .FirstOrDefaultAsync();

        if (repo == null) return NotFound();

        return Ok(repo);
    }

    // ── DELETE /api/repos/{id} ────────────────────────────────────────────────
    // Removes a repo from Revix and deletes the webhook from GitHub
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRepo(Guid id)
    {
        var (user, accessToken) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == user.Id);

        if (repo == null) return NotFound();

        // Delete webhook from GitHub — don't block DB deletion if this fails
        try
        {
            if (repo.GitHubWebhookId != 0)
            {
                await _github.DeleteWebhookAsync(
                    user.GitHubUsername,
                    repo.RepoName,
                    repo.GitHubWebhookId,
                    accessToken);

                Console.WriteLine($"✅ Webhook deleted for {repo.RepoName}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Could not delete GitHub webhook for {repo.RepoName}: {ex.Message}");
        }

        _db.Repositories.Remove(repo);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    // ── POST /api/repos/{id}/retry-webhook ───────────────────────────────────
    // Retries webhook creation for repos where it failed during sync
    [HttpPost("{id:guid}/retry-webhook")]
    public async Task<IActionResult> RetryWebhook(Guid id)
    {
        var (user, accessToken) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == user.Id);

        if (repo == null) return NotFound();

        if (repo.GitHubWebhookId != 0)
            return Ok(new { message = "Webhook already exists for this repository." });

        var webhookUrl    = _config["App:WebhookUrl"]!;
        var webhookSecret = _config["GitHub:WebhookSecret"]!;

        try
        {
            var webhookId = await _github.CreateWebhookAsync(
                user.GitHubUsername,
                repo.RepoName,
                webhookUrl,
                webhookSecret,
                accessToken);

            repo.GitHubWebhookId = webhookId;
            repo.IsEnabled       = true;
            await _db.SaveChangesAsync();

            Console.WriteLine($"✅ Webhook retried successfully for {repo.RepoName} (id: {webhookId})");

            return Ok(new { repo.Id, repo.RepoName, repo.IsEnabled, WebhookId = webhookId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Retry webhook failed for {repo.RepoName}: {ex.Message}");
            return StatusCode(500, new { message = $"Failed to create webhook: {ex.Message}" });
        }
    }

    // ── Private helper ────────────────────────────────────────────────────────
    private async Task<(Revix.Core.Entities.User? user, string accessToken)> GetCurrentUserWithTokenAsync()
    {
        var githubId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (githubId == null) return (null, string.Empty);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubId == githubId);
        if (user == null) return (null, string.Empty);

        var accessToken = _encryption.Decrypt(user.EncryptedAccessToken);
        return (user, accessToken);
    }
}