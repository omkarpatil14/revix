namespace Revix.Core.Entities;

public class ReviewComment
{
    public Guid Id { get; set; }
    public Guid ReviewId { get; set; }
    public string FileName { get; set; }
    public int LineNumber { get; set; }
    public string Comment { get; set; }
    public string Severity { get; set; }
    public DateTime CreatedAt { get; set; }

    public Review Review { get; set; }
}