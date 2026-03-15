using StackExchange.Redis;
using Revix.Core.Models;
using Revix.Core.Constants;
using System.Text.Json;

namespace Revix.Infrastructure.Services;

public class ReviewQueue
{
    private readonly IDatabase _db;

    public ReviewQueue(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    public async Task EnqueueAsync(ReviewJob job)
    {
         var entries = new NameValueEntry[]
        {
            new("owner",    job.Owner),
            new("repo",     job.Repo),
            new("prNumber", job.PrNumber.ToString()),
            new("prTitle",  job.PrTitle), 
            new("sha",      job.CommitSha),
            new("repoDbId", job.RepoDbId)
        };
        
        await  _db.StreamAddAsync(StreamNames.Reviews,  entries);
    }
 
}