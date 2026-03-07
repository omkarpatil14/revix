using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Revix.Core.Interfaces;
using Revix.Core.Models;

namespace Revix.Infrastructure.Services;

public class WebhookService : IWebhookService
{
    private readonly string _secret;

    public WebhookService(IConfiguration config)
    {
        _secret = config["GitHub:WebhookSecret"]!;
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

    public Task QueueReviewAsync(string payload)
    {
        var webhookPayload = JsonSerializer.Deserialize<GitHubWebhookPayload>(payload);
        Console.WriteLine($"✅ PR #{webhookPayload?.PrNumber} - '{webhookPayload?.PullRequest?.Title}' queued for review.");
        Console.WriteLine($"   Repo: {webhookPayload?.Repository?.Owner?.Login}/{webhookPayload?.Repository?.Name}");
        Console.WriteLine($"   SHA: {webhookPayload?.PullRequest?.Head?.Sha}");
        return Task.CompletedTask;
    }
}