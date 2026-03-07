namespace Revix.Core.Interfaces;

public interface IGitHubService
{
    Task<List<Revix.Core.Models.PullRequestFile>> GetPrFilesAsync(
        string owner,
        string repo,
        int prNumber,
        string accessToken);

    Task PostReviewCommentAsync(string owner, string repo, int prNumber, string body, string accessToken);
}