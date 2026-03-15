using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Revix.Core.Interfaces;
using Revix.Core.Models;
using Revix.Infrastructure;

namespace Revix.Worker;

public class ReviewOrchestrator
{
    private readonly IGitHubService  _github;
    private readonly IGroqService    _groq;
    private readonly ICommentService _comments;
    private readonly ITokenEncryptionService _encryption;
    private readonly RevixDbContext  _db;
    private readonly ILogger<ReviewOrchestrator> _logger;

    public ReviewOrchestrator(
        IGitHubService github,
        IGroqService groq,
        ICommentService comments,
        ITokenEncryptionService encryption,
        RevixDbContext db,
        ILogger<ReviewOrchestrator> logger)
    {
        _github     = github;
        _groq       = groq;
        _comments   = comments;
        _encryption = encryption;
        _db         = db;
        _logger     = logger;
    }

    public async Task ProcessReviewAsync(ReviewJob job)
    {
        // 1. Load user + decrypt token
        var repoGuid = Guid.Parse(job.RepoDbId);

        var repository = await _db.Repositories
            .AsNoTracking()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == repoGuid);

        if (repository == null)
        {
            _logger.LogWarning("Repo {RepoDbId} not found in DB. Skipping PR #{PrNumber}.", job.RepoDbId, job.PrNumber);
            return;
        }

        var accessToken = _encryption.Decrypt(repository.User.EncryptedAccessToken);
        _logger.LogInformation("Token starts with: {Token}", accessToken[..10]);

        // 2. Fetch changed files
        var files = await _github.GetPrFilesAsync(
            job.Owner, job.Repo, job.PrNumber, accessToken);

        if (files.Count == 0)
        {
            _logger.LogInformation("No files found for PR #{PrNumber}. Skipping.", job.PrNumber);
            return;
        }

        // 3. Create Review record
        var review = new Revix.Core.Entities.Review
        {
            Id           = Guid.NewGuid(),
            RepositoryId = repository.Id,
            PrNumber     = job.PrNumber,
            PrTitle      = $"PR #{job.PrNumber}",   
            FilesReviewed  = files.Count,
            CommentsPosted = 0,
            LlmTokensUsed  = 0,
            CreatedAt      = DateTime.UtcNow
        };
        await _db.Reviews.AddAsync(review);

        var allReviews    = new List<string>();
        int totalComments = 0;

        // 4. Review each file and post inline comment
        foreach (var file in files)
        {
            _logger.LogInformation("Reviewing {FileName}...", file.FileName);

            var reviewText = await _groq.ReviewCodeAsync(file.Language, file.FileName, file.Patch);
            allReviews.Add($"**{file.FileName}**\n{reviewText}");

            await _db.ReviewComments.AddAsync(new Revix.Core.Entities.ReviewComment
            {
                Id        = Guid.NewGuid(),
                ReviewId  = review.Id,
                FileName  = file.FileName,
                LineNumber = 0,
                Comment   = reviewText,
                Severity  = ExtractSeverity(reviewText),
                CreatedAt = DateTime.UtcNow
            });
            totalComments++;

            await _comments.PostInlineCommentAsync(
                job.Owner, job.Repo, job.PrNumber,
                job.CommitSha, file.FileName, 1,
                reviewText, accessToken);
        }

        // 5. Post summary comment
        var summary = string.Join("\n\n---\n\n", allReviews);
        await _comments.PostSummaryCommentAsync(
            job.Owner, job.Repo, job.PrNumber, summary, accessToken);

        // 6. Save everything
        review.CommentsPosted = totalComments;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Review complete for PR #{PrNumber}. {Count} comments posted.",
            job.PrNumber, totalComments);
    }

    private static string ExtractSeverity(string review)
    {
        if (review.Contains("[Bug]")     || review.Contains("Bug"))     return "Bug";
        if (review.Contains("[Warning]") || review.Contains("Warning")) return "Warning";
        return "Suggestion";
    }
}