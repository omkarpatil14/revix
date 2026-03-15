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
    private readonly RevixDbContext  _db;
    private readonly IGitHubService  _github;
    private readonly IConfiguration  _config;
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

    // ── GET /api/repos/github ─────────────────────────────────────────────────
    // Fetch all repos from GitHub for the logged-in user
    // Frontend uses this to show a picker so user can choose which repo to connect
    [HttpGet("github")]
    public async Task<IActionResult> GetGitHubRepos()
    {
        var (user, accessToken) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        var repos = await _github.GetUserReposAsync(accessToken);

        
        var connectedRepoNames = await _db.Repositories
            .Where(r => r.UserId == user.Id)
            .Select(r => r.RepoName)
            .ToListAsync();

        var result = repos.Select(r => new
        {
            r.Id,
            r.Name,
            r.FullName,
            r.Private,
            r.HtmlUrl,
            IsConnected = connectedRepoNames.Contains(r.Name)
        });

        return Ok(result);
    }

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


    [HttpPost]
    public async Task<IActionResult> ConnectRepo([FromBody] ConnectRepoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RepoName))
            return BadRequest("RepoName is required.");

        var (user, accessToken) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        // Check if already connected
        var existing = await _db.Repositories
            .FirstOrDefaultAsync(r => r.UserId == user.Id && r.RepoName == request.RepoName);

        if (existing != null)
            return Conflict(new { message = $"'{request.RepoName}' is already connected." });

        // Create webhook on GitHub
        var webhookUrl    = _config["App:WebhookUrl"]!;  // e.g. https://xxxx.ngrok.io/webhook
        var webhookSecret = _config["GitHub:WebhookSecret"]!;
        var owner         = user.GitHubUsername;

        long webhookId;
        try
        {
            webhookId = await _github.CreateWebhookAsync(
                owner, request.RepoName, webhookUrl, webhookSecret, accessToken);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to create GitHub webhook: {ex.Message}" });
        }

        // Save to DB
        var repo = new Revix.Core.Entities.Repository
        {
            Id             = Guid.NewGuid(),
            UserId         = user.Id,
            GitHubRepoId   = request.GitHubRepoId ?? "0",
            RepoName       = request.RepoName,
            IsEnabled      = true,
            GitHubWebhookId = webhookId,
            CreatedAt      = DateTime.UtcNow
        };

        await _db.Repositories.AddAsync(repo);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRepo), new { id = repo.Id }, new
        {
            repo.Id,
            repo.RepoName,
            repo.IsEnabled,
            repo.CreatedAt
        });
    }


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

 
    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> ToggleRepo(Guid id)
    {
        var (user, accessToken) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == user.Id);

        if (repo == null) return NotFound();

        var webhookUrl    = _config["App:WebhookUrl"]!;
        var webhookSecret = _config["GitHub:WebhookSecret"]!;

        if (repo.IsEnabled)
        {
            // Currently enabled → disable → delete webhook from GitHub
            try
            {
                if (repo.GitHubWebhookId != 0)
                    await _github.DeleteWebhookAsync(
                        user.GitHubUsername, repo.RepoName,
                        repo.GitHubWebhookId, accessToken);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Failed to delete GitHub webhook: {ex.Message}" });
            }

            repo.GitHubWebhookId = 0;
            repo.IsEnabled       = false;
        }
        else
        {
            // Currently disabled → enable → recreate webhook on GitHub
            try
            {
                var webhookId = await _github.CreateWebhookAsync(
                    user.GitHubUsername, repo.RepoName,
                    webhookUrl, webhookSecret, accessToken);

                repo.GitHubWebhookId = webhookId;
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Failed to create GitHub webhook: {ex.Message}" });
            }

            repo.IsEnabled = true;
        }

        await _db.SaveChangesAsync();

        return Ok(new { repo.Id, repo.RepoName, repo.IsEnabled });
    }

   
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRepo(Guid id)
    {
        var (user, accessToken) = await GetCurrentUserWithTokenAsync();
        if (user == null) return Unauthorized();

        var repo = await _db.Repositories
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == user.Id);

        if (repo == null) return NotFound();

        // Delete webhook from GitHub
        try
        {
            if (repo.GitHubWebhookId != 0)
                await _github.DeleteWebhookAsync(
                    user.GitHubUsername, repo.RepoName,
                    repo.GitHubWebhookId, accessToken);
        }
        catch (Exception ex)
        {
            // Log but don't block deletion — webhook may already be gone
            Console.WriteLine($"Warning: Could not delete GitHub webhook: {ex.Message}");
        }

        _db.Repositories.Remove(repo);
        await _db.SaveChangesAsync();

        return NoContent();
    }


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

public record ConnectRepoRequest(string RepoName, string? GitHubRepoId);