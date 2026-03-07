namespace Revix.core.Intefaces;

public interface ICommmentService
{
    Task PostInLineComment(String owner , String repo, int prNumber, string commitSha, string filename, int position, string comment , string token);

    Task PostSummeryCommentAsync(string owner, string repo, int prNumber, string summery, string token);
}