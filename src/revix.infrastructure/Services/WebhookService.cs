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

    public WebhookService(
        IConfiguration config,
        IGitHubService gitHubService,
        ITokenEncryptionService encryption,
        RevixDbContext db)
    {
        _secret = config["GitHub:WebhookSecret"]!;
        _gitHubService = gitHubService;
        _encryption = encryption;
        _db = db;
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
        var owner = webhookPayload?.Repository?.Owner?.Login;
        var repo = webhookPayload?.Repository?.Name;
        var prNumber = webhookPayload?.PrNumber;
        var prTitle = webhookPayload?.PullRequest?.Title;

        Console.WriteLine($"🔍 Fetching files for PR #{prNumber} - '{prTitle}'");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubUsername == owner);
        if (user == null)
        {
            Console.WriteLine($"❌ User '{owner}' not found in database. Skipping PR #{prNumber}.");
            return;
        }

        var accessToken = _encryption.Decrypt(user.EncryptedAccessToken);
        var files = await _gitHubService.GetPrFilesAsync(owner!, repo!, prNumber!.Value, accessToken);

        Console.WriteLine($"📄 PR #{prNumber} has {files.Count} changed files.");
        foreach (var file in files)
        {
            Console.WriteLine($"   - {file.FileName} ({file.Language})");
        }
    }
}