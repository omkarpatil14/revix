namespace Revix.Core.Interfaces;

public interface IGroqService
{
    Task<string> ReviewCodeAsync(string language, string filename, string diff);
}