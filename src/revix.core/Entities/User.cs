namespace Revix.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public string GitHubId { get; set; } = null!;
    public string GitHubUsername { get; set; } = null!;
     public string EncryptedAccessToken { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public ICollection<Repository> Repositories { get; set; } = new List<Repository>();
}