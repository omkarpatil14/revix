namespace Revix.Core.Entities;

public class Repository
{
    public Guid   Id               { get; set; }
    public Guid   UserId           { get; set; }
    public string GitHubRepoId     { get; set; } = string.Empty;
    public string RepoName         { get; set; } = string.Empty;
    public bool   IsEnabled        { get; set; }
    public long   GitHubWebhookId  { get; set; }  
    public DateTime CreatedAt      { get; set; }

    public User User                   { get; set; } = null!;
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}