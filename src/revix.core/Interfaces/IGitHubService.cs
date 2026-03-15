namespace Revix.Core.Interfaces;

public interface IGitHubService
{
    // Existing
    Task<List<Revix.Core.Models.PullRequestFile>> GetPrFilesAsync(
        string owner,
        string repo,
        int prNumber,
        string accessToken);

    Task PostReviewCommentAsync(
        string owner,
        string repo,
        int prNumber,
        string body,
        string accessToken);

    // New
    Task<List<Revix.Core.Models.GitHubRepo>> GetUserReposAsync(string accessToken);

    Task<long> CreateWebhookAsync(
        string owner,
        string repo,
        string webhookUrl,
        string webhookSecret,
        string accessToken);

    Task DeleteWebhookAsync(
        string owner,
        string repo,
        long webhookId,
        string accessToken);
}