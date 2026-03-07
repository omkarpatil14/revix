using Microsoft.AspNetCore.DataProtection;
using Revix.Core.Interfaces;

namespace Revix.Infrastructure.Services;

public class TokenEncryptionService : ITokenEncryptionService
{
    private readonly IDataProtector _protector;

    public TokenEncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("GitHubAccessToken");
    }

    public string Encrypt(string token) => _protector.Protect(token);
    public string Decrypt(string encryptedToken) => _protector.Unprotect(encryptedToken);
}