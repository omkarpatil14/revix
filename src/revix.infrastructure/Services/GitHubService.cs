using Octokit;
using Revix.Core.Interfaces;

namespace Revix.Infrastructure.Services;

public class GitHubService : IGitHubService
{
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
                Patch = f.Patch,
                Status = f.Status,
                Language = DetectLanguage(f.FileName)
            })
            .ToList();
    }

    private bool IsIgnored(string filename) =>
        filename.EndsWith(".json") ||
        filename.EndsWith(".md") ||
        filename.EndsWith(".yml") ||
        filename.EndsWith(".yaml") ||
        filename.Contains("migrations/") ||
        filename.Contains("Migrations/");

    private string DetectLanguage(string filename) =>
        Path.GetExtension(filename).ToLower() switch
        {
            ".cs"   => "C#",
            ".js"   => "JavaScript",
            ".ts"   => "TypeScript",
            ".py"   => "Python",
            ".java" => "Java",
            ".go"   => "G",
            ".rb"   => "Ruby",
            ".php"  => "PHP",
            ".cpp"  => "C++",
            ".c"    => "C",
            _       => "Unknown"
        };

        public async Task PostReviewCommentAsync(string owner, string repo, int prNumber, string body, string accessToken)
        {
            var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
            client.Credentials = new Credentials(accessToken);

            await client.PullRequest.Review.Create(owner, repo, prNumber,
                new PullRequestReviewCreate
                {
                    Body = body,
                    Event = PullRequestReviewEvent.Comment
                });
        }

        private int GetLastPosition(string patch)
        {
            if (string.IsNullOrEmpty(patch)) return 1;
            var lines = patch.Split('\n');
            return lines.Length;
        }
}