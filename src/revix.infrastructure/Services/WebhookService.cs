using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Revix.Core.Interfaces;
using Revix.Core.Models;

namespace Revix.Infrastructure.Services;

public class WebhookService : IWebhookService
{
    private readonly string _secret;
    private readonly IGitHubService _gitHubService;
    private readonly ITokenEncryptionService _encryption;
    private readonly RevixDbContext _db;
    private readonly IGroqService _groq;
    private readonly ICommentService _commentService;

    public WebhookService(
        IConfiguration config,
        IGitHubService gitHubService,
        ITokenEncryptionService encryption,
        RevixDbContext db,
        IGroqService groq,
        ICommentService commentService)
    {
        _secret = config["GitHub:WebhookSecret"]!;
        _gitHubService = gitHubService;
        _encryption = encryption;
        _db = db;
        _groq = groq;
        _commentService = commentService;
    }

    public bool ValidateSignature(string payload, string signature)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var expectedSignature = "sha256=" + Convert.ToHexString(hash).ToLower();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signature)
        );
    }

    public async Task QueueReviewAsync(string payload)
    {
        var webhookPayload = JsonSerializer.Deserialize<GitHubWebhookPayload>(payload);
        var owner = webhookPayload?.Repository?.Owner?.Login ;
        var repo = webhookPayload?.Repository?.Name;
        var prNumber = webhookPayload?.PrNumber;
        var prTitle = webhookPayload?.PullRequest?.Title;

        Console.WriteLine($"🔍 Fetching files for PR #{prNumber} - '{prTitle}'");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubUsername == owner);
        if (user == null)
        {
            Console.WriteLine($"❌ User '{owner}' not found. Skipping PR {prNumber}.");
            return;
        }

        var repository = await _db.Repositories
            .FirstOrDefaultAsync(r => r.UserId == user.Id && r.RepoName == repo);
        if (repository == null)
        {
            Console.WriteLine($"❌ Repo '{repo}' not found. Skipping PR #{prNumber}.");
            return;
        }

        var accessToken = _encryption.Decrypt(user.EncryptedAccessToken);
        var files = await _gitHubService.GetPrFilesAsync(owner!, repo!, prNumber!.Value, accessToken);

        var review = new Revix.Core.Entities.Review
        {
            Id = Guid.NewGuid(),
            RepositoryId = repository.Id,
            PrNumber = prNumber!.Value,
            PrTitle = prTitle!,
            FilesReviewed = files.Count,
            CommentsPosted = 0,
            LlmTokensUsed = 0,
            CreatedAt = DateTime.UtcNow
        };
        await _db.Reviews.AddAsync(review);

        var allReviews = new List<string>();
        int totalComments = 0;

        foreach (var file in files)
        {
            Console.WriteLine($"🤖 Reviewing {file.FileName}...");
            var reviewText = await _groq.ReviewCodeAsync(file.Language, file.FileName, file.Patch);
            allReviews.Add($"**{file.FileName}**\n{reviewText}");

            var reviewComment = new Revix.Core.Entities.ReviewComment
            {
                Id = Guid.NewGuid(),
                ReviewId = review.Id,
                FileName = file.FileName,
                LineNumber = 0,
                Comment = reviewText,
                Severity = ExtractSeverity(reviewText),
                CreatedAt = DateTime.UtcNow
            };
            await _db.ReviewComments.AddAsync(reviewComment);
            totalComments++;

            var comment = $"## 🤖 Revix Review: `{file.FileName}`\n\n{reviewText}";
            await _gitHubService.PostReviewCommentAsync(owner!, repo!, prNumber!.Value, comment, accessToken);
        }

        var summary = string.Join("\n\n---\n\n", allReviews);
        await _commentService.PostSummaryCommentAsync(owner!, repo!, prNumber!.Value, summary, accessToken);

        review.CommentsPosted = totalComments;
        await _db.SaveChangesAsync();

        Console.WriteLine($"✅ Review complete. {totalComments} comments posted.");
    }

    private string ExtractSeverity(string review)
    {
        if (review.Contains("[Bug]") || review.Contains("Bug")) return "Bug";
        if (review.Contains("[Warning]") || review.Contains("Warning")) return "Warning";
        return "Suggestion";
    }
}