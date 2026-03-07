using Revix.Core.Entities;

namespace Revix.Infrastructure.Services;

public interface IGitHubAuthService
{
    Task<User> HandleGitHubLoginAsync(string githubId, string username, string accessToken);
}