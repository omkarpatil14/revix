namespace Revix.Core.Interfaces;

public interface ICommentService
{
    Task PostInlineCommentAsync(string owner, string repo, int prNumber,
        string commitSha, string filename, int position, string comment, string token);
    
    Task PostSummaryCommentAsync(string owner, string repo, int prNumber,
        string summary, string token);
}