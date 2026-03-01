namespace Revix.Core.Entities;

public class Review
{
    public Guid Id { get; set; }
    public Guid RepositoryId { get; set; }
    public int PrNumber { get; set; }
    public string PrTitle { get; set; }
    public int FilesReviewed { get; set; }
    public int CommentsPosted { get; set; }
    public int LlmTokensUsed { get; set; }
    public DateTime CreatedAt { get; set; }

    public Repository Repository { get; set; }
    public ICollection<ReviewComment> ReviewComments { get; set; }
}