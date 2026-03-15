using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Revix.Core.Constants;
using Revix.Core.Models;

namespace Revix.Worker;

public class ReviewWorkerService : BackgroundService
{
    private readonly IDatabase _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReviewWorkerService> _logger;

    public ReviewWorkerService(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        ILogger<ReviewWorkerService> logger)
    {
        _db           = redis.GetDatabase();
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Review worker started.");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var entries = await _db.StreamReadGroupAsync(
                    StreamNames.Reviews,
                    StreamNames.ConsumerGroup,
                    StreamNames.ConsumerName,
                    StreamPosition.NewMessages,
                    count: 1);

                if (entries.Length == 0)
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                foreach (var entry in entries)
                {
                    await ProcessEntry(entry, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker loop error.");
                await Task.Delay(2000, ct);
            }
        }
    }

    private async Task ProcessEntry(StreamEntry entry, CancellationToken ct)
    {
        var job = new ReviewJob
        {
            Owner     = entry["owner"]!,
            Repo      = entry["repo"]!,
            PrNumber  = int.Parse(entry["prNumber"]!),
            PrTitle   = entry["prTitle"].HasValue ? (string)entry["prTitle"]! : $"PR #{entry["prNumber"]}",
            CommitSha = entry["sha"]!,
            RepoDbId  = entry["repoDbId"]!
        };

        _logger.LogInformation(
            "Processing PR #{PrNumber} '{PrTitle}' in {Owner}/{Repo}",
            job.PrNumber, job.PrTitle, job.Owner, job.Repo);

        // Create a fresh DI scope per job — resolves DbContext, ReviewOrchestrator, etc.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ReviewOrchestrator>();

        try
        {
            await orchestrator.ProcessReviewAsync(job);

            await _db.StreamAcknowledgeAsync(
                StreamNames.Reviews,
                StreamNames.ConsumerGroup,
                entry.Id);

            _logger.LogInformation("✅ Review done for PR #{PrNumber}.", job.PrNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review failed for PR #{PrNumber}.", job.PrNumber);

            await _db.StreamAddAsync(StreamNames.ReviewsFailed, new NameValueEntry[]
            {
                new("owner",     job.Owner),
                new("repo",      job.Repo),
                new("prNumber",  job.PrNumber.ToString()),
                new("prTitle",   job.PrTitle),
                new("sha",       job.CommitSha),
                new("error",     ex.Message),
                new("failedAt",  DateTimeOffset.UtcNow.ToString("O"))
            });

            // ACK anyway — don't block the queue with poison messages
            await _db.StreamAcknowledgeAsync(
                StreamNames.Reviews,
                StreamNames.ConsumerGroup,
                entry.Id);
        }
    }
}