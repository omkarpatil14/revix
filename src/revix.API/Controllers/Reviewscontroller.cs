using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Revix.Infrastructure;
using System.Security.Claims;

namespace Revix.API.Controllers;

[ApiController]
[Route("api/reviews")]
[Authorize]
public class ReviewsController : ControllerBase
{
    private readonly RevixDbContext _db;

    public ReviewsController(RevixDbContext db)
    {
        _db = db;
    }

    // ── GET /api/reviews ──────────────────────────────────────────────────────
    // All reviews across all repos for the logged-in user
    [HttpGet]
    public async Task<IActionResult> GetAllReviews([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var query = _db.Reviews
            .Where(rv => rv.Repository.UserId == user.Id)
            .OrderByDescending(rv => rv.CreatedAt);

        var total = await query.CountAsync();

        var reviews = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(rv => new
            {
                rv.Id,
                rv.PrNumber,
                rv.PrTitle,
                rv.FilesReviewed,
                rv.CommentsPosted,
                rv.CreatedAt,
                RepoName = rv.Repository.RepoName,
                RepoId   = rv.Repository.Id,
                BugCount  = rv.ReviewComments.Count(rc => rc.Severity == "Bug"),
                WarnCount = rv.ReviewComments.Count(rc => rc.Severity == "Warning"),
            })
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            data = reviews
        });
    }

    // ── GET /api/reviews/{id} ─────────────────────────────────────────────────
    // Full review detail with all file comments
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetReview(Guid id)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var review = await _db.Reviews
            .Where(rv => rv.Id == id && rv.Repository.UserId == user.Id)
            .Select(rv => new
            {
                rv.Id,
                rv.PrNumber,
                rv.PrTitle,
                rv.FilesReviewed,
                rv.CommentsPosted,
                rv.LlmTokensUsed,
                rv.CreatedAt,
                RepoName = rv.Repository.RepoName,
                RepoId   = rv.Repository.Id,
                Comments = rv.ReviewComments
                    .OrderBy(rc => rc.FileName)
                    .Select(rc => new
                    {
                        rc.Id,
                        rc.FileName,
                        rc.LineNumber,
                        rc.Comment,
                        rc.Severity,
                        rc.CreatedAt
                    })
            })
            .FirstOrDefaultAsync();

        if (review == null) return NotFound();

        return Ok(review);
    }

    // ── GET /api/reviews/repo/{repoId} ────────────────────────────────────────
    // All reviews for a specific repo
    [HttpGet("repo/{repoId:guid}")]
    public async Task<IActionResult> GetReviewsByRepo(
        Guid repoId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        // Make sure repo belongs to user
        var repoExists = await _db.Repositories
            .AnyAsync(r => r.Id == repoId && r.UserId == user.Id);

        if (!repoExists) return NotFound();

        var query = _db.Reviews
            .Where(rv => rv.RepositoryId == repoId)
            .OrderByDescending(rv => rv.CreatedAt);

        var total = await query.CountAsync();

        var reviews = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(rv => new
            {
                rv.Id,
                rv.PrNumber,
                rv.PrTitle,
                rv.FilesReviewed,
                rv.CommentsPosted,
                rv.CreatedAt,
                BugCount  = rv.ReviewComments.Count(rc => rc.Severity == "Bug"),
                WarnCount = rv.ReviewComments.Count(rc => rc.Severity == "Warning"),
            })
            .ToListAsync();

        return Ok(new
        {
            total,
            page,
            pageSize,
            data = reviews
        });
    }

    // ── GET /api/dashboard ────────────────────────────────────────────────────
    // Stats for the dashboard
    [HttpGet("/api/dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var user = await GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var repos = await _db.Repositories
            .Where(r => r.UserId == user.Id)
            .CountAsync();

        var reviews = await _db.Reviews
            .Where(rv => rv.Repository.UserId == user.Id)
            .CountAsync();

        var totalComments = await _db.Reviews
            .Where(rv => rv.Repository.UserId == user.Id)
            .SumAsync(rv => rv.CommentsPosted);

        var bugs = await _db.ReviewComments
            .Where(rc => rc.Review.Repository.UserId == user.Id && rc.Severity == "Bug")
            .CountAsync();

        var warnings = await _db.ReviewComments
            .Where(rc => rc.Review.Repository.UserId == user.Id && rc.Severity == "Warning")
            .CountAsync();

        var recentReviews = await _db.Reviews
            .Where(rv => rv.Repository.UserId == user.Id)
            .OrderByDescending(rv => rv.CreatedAt)
            .Take(5)
            .Select(rv => new
            {
                rv.Id,
                rv.PrNumber,
                rv.PrTitle,
                rv.CreatedAt,
                RepoName = rv.Repository.RepoName
            })
            .ToListAsync();

        return Ok(new
        {
            TotalRepos       = repos,
            TotalReviews     = reviews,
            TotalComments    = totalComments,
            TotalBugs        = bugs,
            TotalWarnings    = warnings,
            RecentReviews    = recentReviews
        });
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    private async Task<Revix.Core.Entities.User?> GetCurrentUserAsync()
    {
        var githubId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (githubId == null) return null;

        return await _db.Users
            .FirstOrDefaultAsync(u => u.GitHubId == githubId);
    }
}