namespace Revix.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public string GitHubId { get; set; }
    public string GitHubUsername { get; set; }
    public string AccessToken { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Repository> Repositories { get; set; }
}