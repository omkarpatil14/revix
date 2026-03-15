using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Revix.Core.Interfaces;
using Revix.Core.Models;
using Revix.Infrastructure.Services;

namespace Revix.Infrastructure.Services;

public class WebhookService : IWebhookService
{
    private readonly string _secret;
    private readonly ITokenEncryptionService _encryption;
    private readonly RevixDbContext _db;
    private readonly ReviewQueue _reviewQueue;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        IConfiguration config,
        ITokenEncryptionService encryption,
        RevixDbContext db,
        ReviewQueue reviewQueue,
        ILogger<WebhookService> logger)
    {
        _secret = config["GitHub:WebhookSecret"]
            ?? throw new InvalidOperationException("GitHub:WebhookSecret is not configured.");
        _encryption = encryption;
        _db = db;
        _reviewQueue = reviewQueue;
        _logger = logger;
    }

    public bool ValidateSignature(string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload cannot be empty.", nameof(payload));

        if (string.IsNullOrWhiteSpace(signature))
            throw new ArgumentException("Signature cannot be empty.", nameof(signature));

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
      
        GitHubWebhookPayload? webhookPayload;
        try
        {
            webhookPayload = JsonSerializer.Deserialize<GitHubWebhookPayload>(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize webhook payload.");
            return; 
        }

        
        var action    = webhookPayload?.Action;
        var owner     = webhookPayload?.Repository?.Owner?.Login;
        var repo      = webhookPayload?.Repository?.Name;
        var prNumber  = webhookPayload?.PrNumber;
        var prTitle   = webhookPayload?.PullRequest?.Title ?? $"PR #{webhookPayload?.PrNumber}";
        var commitSha = webhookPayload?.PullRequest?.Head?.Sha;

        if (action != "opened" && action != "synchronize")
        {
            _logger.LogInformation("Skipping action '{Action}' — not a review trigger.", action);
            return;
        }

        if (string.IsNullOrWhiteSpace(owner)  ||
            string.IsNullOrWhiteSpace(repo)   ||
            prNumber is null                  ||
            string.IsNullOrWhiteSpace(commitSha))
        {
            _logger.LogWarning(
                "Webhook payload is missing required fields. " +
                "Owner={Owner}, Repo={Repo}, PR={PrNumber}, SHA={Sha}",
                owner, repo, prNumber, commitSha);
            return;
        }

        
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.GitHubUsername == owner);

        if (user == null)
        {
            _logger.LogWarning(
                "User '{Owner}' not found in database. Skipping PR #{PrNumber}.",
                owner, prNumber);
            return;
        }

        
        var repository = await _db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == user.Id && r.RepoName == repo);

        if (repository == null)
        {
            _logger.LogWarning(
                "Repo '{Repo}' not found for user '{Owner}'. Skipping PR #{PrNumber}.",
                repo, owner, prNumber);
            return;
        }

        
        var job = new ReviewJob
        {
            Owner     = owner,
            Repo      = repo,
            PrNumber  = prNumber.Value,
            PrTitle   = prTitle,
            CommitSha = commitSha,
            RepoDbId  = repository.Id.ToString()
        };

        try
        {
            await _reviewQueue.EnqueueAsync(job);
            _logger.LogInformation(
                "PR #{PrNumber} ({Owner}/{Repo}) enqueued successfully. SHA={Sha}",
                job.PrNumber, job.Owner, job.Repo, job.CommitSha);
        }
        catch (Exception ex)
        {
           
            _logger.LogError(ex,
                "Failed to enqueue PR #{PrNumber} ({Owner}/{Repo}). " +
                "GitHub will retry the webhook automatically.",
                job.PrNumber, job.Owner, job.Repo);

            throw;
        }
    }
}