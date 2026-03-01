namespace Revix.Core.Entities;

public class Repository
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string GitHubRepoId { get; set; }
    public string RepoName { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; }
    public ICollection<Review> Reviews { get; set; }
}