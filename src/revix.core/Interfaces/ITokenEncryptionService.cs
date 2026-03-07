namespace Revix.Core.Interfaces;

public interface ITokenEncryptionService
{
    string Encrypt(string token);
    string Decrypt(string encryptedToken);
}