using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Revix.Core.Interfaces;
using Revix.Core.Models;
using Revix.Infrastructure;

namespace Revix.Worker;

public class ReviewOrchestrator
{
    private readonly IGitHubService          _github;
    private readonly IGroqService            _groq;
    private readonly ICommentService         _comments;
    private readonly ITokenEncryptionService _encryption;
    private readonly RevixDbContext          _db;
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
        
        var repoGuid = Guid.Parse(job.RepoDbId);

        var repository = await _db.Repositories
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == repoGuid);

        if (repository == null)
        {
            _logger.LogWarning("Repo {RepoDbId} not found. Skipping PR #{PrNumber}.", job.RepoDbId, job.PrNumber);
            return;
        }

        var accessToken = _encryption.Decrypt(repository.User.EncryptedAccessToken);

    
        var files = await _github.GetPrFilesAsync(
            job.Owner, job.Repo, job.PrNumber, accessToken);

        if (files.Count == 0)
        {
            _logger.LogInformation("No reviewable files found for PR #{PrNumber}. Skipping.", job.PrNumber);
            return;
        }

        
        var review = new Revix.Core.Entities.Review
        {
            Id             = Guid.NewGuid(),
            RepositoryId   = repository.Id,
            PrNumber       = job.PrNumber,
            PrTitle        = job.PrTitle,
            FilesReviewed  = files.Count,
            CommentsPosted = 0,
            LlmTokensUsed  = 0,
            CreatedAt      = DateTime.UtcNow
        };
        await _db.Reviews.AddAsync(review);

        var allReviews    = new List<string>();
        int totalComments = 0;

       
        foreach (var file in files)
        {
            _logger.LogInformation("Reviewing {FileName}...", file.FileName);

            var reviewText   = await _groq.ReviewCodeAsync(file.Language, file.FileName, file.Patch);
            var diffPosition = GetLastDiffPosition(file.Patch);

            allReviews.Add($"### 📄 `{file.FileName}`\n\n{reviewText}");

            await _db.ReviewComments.AddAsync(new Revix.Core.Entities.ReviewComment
            {
                Id         = Guid.NewGuid(),
                ReviewId   = review.Id,
                FileName   = file.FileName,
                LineNumber = diffPosition,
                Comment    = reviewText,
                Severity   = ExtractSeverity(reviewText),
                CreatedAt  = DateTime.UtcNow
            });
            totalComments++;

           
            try
            {
                await _comments.PostInlineCommentAsync(
                    job.Owner, job.Repo, job.PrNumber,
                    job.CommitSha, file.FileName, diffPosition,
                    reviewText, accessToken);

                _logger.LogInformation(
                    "Inline comment posted for {FileName} at position {Position}.",
                    file.FileName, diffPosition);
            }
            catch (Exception ex)
            {
               
                _logger.LogWarning(ex,
                    "Inline comment failed for {FileName}. Will appear in summary only.",
                    file.FileName);
            }
        }

       
        var summary = string.Join("\n\n---\n\n", allReviews);
        await _comments.PostSummaryCommentAsync(
            job.Owner, job.Repo, job.PrNumber, summary, accessToken);


        review.CommentsPosted = totalComments;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "✅ Review complete for PR #{PrNumber} '{PrTitle}'. {Count} files reviewed.",
            job.PrNumber, job.PrTitle, totalComments);
    }

    private static int GetLastDiffPosition(string patch)
    {
        if (string.IsNullOrWhiteSpace(patch)) return 1;

        var lines           = patch.Split('\n');
        int position        = 0;
        int lastAddedLine   = 1;

        foreach (var line in lines)
        {
            position++;
            if (line.StartsWith('+') && !line.StartsWith("+++"))
                lastAddedLine = position;
        }

        return lastAddedLine;
    }

    private static string ExtractSeverity(string review)
    {
        if (review.Contains("[Bug]")     || review.Contains("Bug"))     return "Bug";
        if (review.Contains("[Warning]") || review.Contains("Warning")) return "Warning";
        return "Suggestion";
    }
}