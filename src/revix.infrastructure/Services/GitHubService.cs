using Octokit;
using Revix.Core.Interfaces;

namespace Revix.Infrastructure.Services;

public class GitHubService : IGitHubService
{
    // ── Existing ──────────────────────────────────────────────────────────────

    public async Task<List<Revix.Core.Models.PullRequestFile>> GetPrFilesAsync(
        string owner,
        string repo,
        int prNumber,
        string accessToken)
    {
        var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
        client.Credentials = new Credentials(accessToken);

        var files = await client.PullRequest.Files(owner, repo, prNumber);

        return files
            .Where(f => f.Patch != null)
            .Where(f => !IsIgnored(f.FileName))
            .Select(f => new Revix.Core.Models.PullRequestFile
            {
                FileName = f.FileName,
                Patch    = f.Patch,
                Status   = f.Status,
                Language = DetectLanguage(f.FileName)
            })
            .ToList();
    }

    public async Task PostReviewCommentAsync(
        string owner,
        string repo,
        int prNumber,
        string body,
        string accessToken)
    {
        var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
        client.Credentials = new Credentials(accessToken);

        await client.PullRequest.Review.Create(owner, repo, prNumber,
            new PullRequestReviewCreate
            {
                Body  = body,
                Event = PullRequestReviewEvent.Comment
            });
    }

    // ── New ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all repos the authenticated user has access to (own + collaborator).
    /// </summary>
    public async Task<List<Revix.Core.Models.GitHubRepo>> GetUserReposAsync(string accessToken)
    {
        var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
        client.Credentials = new Credentials(accessToken);

        var options = new ApiOptions { PageSize = 100 };

        var repos = await client.Repository.GetAllForCurrent(
            new RepositoryRequest
            {
                Sort      = RepositorySort.Updated,
                Direction = SortDirection.Descending,
                Affiliation = RepositoryAffiliation.Owner  // only repos they own
            },
            options);

        return repos.Select(r => new Revix.Core.Models.GitHubRepo
        {
            Id       = r.Id,
            Name     = r.Name,
            FullName = r.FullName,
            Private  = r.Private,
            HtmlUrl  = r.HtmlUrl
        }).ToList();
    }

    /// <summary>
    /// Creates a webhook on the given repo that sends pull_request events to webhookUrl.
    /// Returns the webhook ID (needed to delete it later).
    /// </summary>
    public async Task<long> CreateWebhookAsync(
        string owner,
        string repo,
        string webhookUrl,
        string webhookSecret,
        string accessToken)
    {
        var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
        client.Credentials = new Credentials(accessToken);

        var config = new Dictionary<string, string>
        {
            { "url",          webhookUrl    },
            { "content_type", "json"        },
            { "secret",       webhookSecret },
            { "insecure_ssl", "0"           }
        };

        var hook = await client.Repository.Hooks.Create(owner, repo,
            new NewRepositoryHook("web", config)
            {
                Events = new[] { "pull_request" },
                Active = true
            });

        return hook.Id;
    }

    /// <summary>
    /// Deletes a webhook from GitHub by its ID.
    /// </summary>
    public async Task DeleteWebhookAsync(
        string owner,
        string repo,
        long webhookId,
        string accessToken)
    {
        var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
        client.Credentials = new Credentials(accessToken);

        await client.Repository.Hooks.Delete(owner, repo, (int)webhookId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public string DetectLanguage(string filename) =>
        Path.GetExtension(filename).ToLower() switch
        {
            ".cs"   => "C#",
            ".js"   => "JavaScript",
            ".ts"   => "TypeScript",
            ".py"   => "Python",
            ".java" => "Java",
            ".go"   => "Go",
            ".rb"   => "Ruby",
            ".php"  => "PHP",
            ".cpp"  => "C++",
            ".c"    => "C",
            _       => "Unknown"
        };

    private bool IsIgnored(string filename) =>
        filename.EndsWith(".json") ||
        filename.EndsWith(".md")   ||
        filename.EndsWith(".yml")  ||
        filename.EndsWith(".yaml") ||
        filename.EndsWith(".xml")  ||
        filename.Contains("migrations/")  ||
        filename.Contains("Migrations/")  ||
        filename.Contains("DataProtection");
}