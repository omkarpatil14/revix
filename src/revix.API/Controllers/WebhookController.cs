using Microsoft.AspNetCore.Mvc;
using Revix.Core.Interfaces;

[ApiController]
[Route("webhook")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookService _webhookService;

    public WebhookController(IWebhookService webhookService)  
    {
        _webhookService = webhookService;
    }

    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        var signature = Request.Headers["X-Hub-Signature-256"].ToString();  

        if (string.IsNullOrEmpty(signature) || !_webhookService.ValidateSignature(payload, signature))
        {
            Console.WriteLine("❌ Invalid webhook signature!");  
            return Unauthorized();
        }

        var eventType = Request.Headers["X-GitHub-Event"].ToString(); 
        Console.WriteLine($"📩 Received GitHub event: {eventType}");

        if (eventType != "pull_request")
            return Ok("Event ignored");

        await _webhookService.QueueReviewAsync(payload);

        return Ok("Webhook received");
    }
}