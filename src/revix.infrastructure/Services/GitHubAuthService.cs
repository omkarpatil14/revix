using Microsoft.EntityFrameworkCore;
using Revix.Core.Entities;
using Revix.Core.Interfaces;


namespace Revix.Infrastructure.Services;

public class GitHubAuthService : IGitHubAuthService
{
    private readonly RevixDbContext _db;
    private readonly ITokenEncryptionService _encryption;
    public GitHubAuthService(RevixDbContext db, ITokenEncryptionService encryption)
    {
        _db = db;
        _encryption = encryption;
    }

    public async Task<User> HandleGitHubLoginAsync(string githubId,string username, string accessToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubId == githubId);
        if (user != null)
        {
            user.GitHubUsername = username;
            user.EncryptedAccessToken = _encryption.Encrypt(accessToken);
            _db.Users.Update(user);
        }
        else
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                GitHubId = githubId,
                GitHubUsername = username,
                EncryptedAccessToken = _encryption.Encrypt(accessToken),
                CreatedAt = DateTime.UtcNow
            };
            await _db.Users.AddAsync(user);
        }

        await _db.SaveChangesAsync();
        return user;
    }
}
