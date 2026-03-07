namespace Revix.Core.Interfaces;

public interface IWebhookService
{
    bool ValidateSignature(string payload, string signature);
    Task QueueReviewAsync(string payload);
}