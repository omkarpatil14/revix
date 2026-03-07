using Octokit;
using Revix.core.Interfaces;

namespace Revix.infrastructure.Services;

public class CommentService : ICommentService
{
    public async Task PostInLineComment(string owner, string repo, int prNumber, string commitSha, string filename, int position, string comment, string token)
    {
        var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
        client.Credentials = new Credentials(token);

        await client.PullRequest.ReviewComment.Create(owner, repo, prNumber,
            new PullRequestReviewCommentCreate(
                body: FormatComment(comment),
                commitId: commitSha,
                path: filename,
                position: position
            ));
    }

    public async Task PostSummeryCommentAsync(string owner , string repo, int prNumber , string summery, string token){
        var client = new GitHubClient(new ProductHeaderValue("RevixBot"));
        client.Credentials = new Credentials(token);

         var client = new GitHubClient(new ProductHeaderValue("Revix"));
        client.Credentials = new Credentials(token);

        var body = $"""
        ## 🤖 Revix AI Code Review Summary

        {summary}

        ---
        🔴 **Bug** · 🟡 **Warning** · 🟢 **Suggestion**
        *Reviewed by Revix*
        """;

        await client.Issue.Comment.Create(owner, repo, prNumber, body);
    }

    private string FormatComment(string review)
    {
        var formatted = review
            .Replace("[Bug]", "🔴 **Bug**")
            .Replace("[Warning]", "🟡 **Warning**")
            .Replace("[Suggestion]", "🟢 **Suggestion**");

        return $"### 🤖 Revix Review\n\n{formatted}\n\n---\n*Reviewed by Revix*";
    }
}