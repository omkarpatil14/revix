using Microsoft.AspNetCore.Mvc;
using Revix.Core.Interfaces;

namespace Revix.API.Controllers;

[ApiController]
[Route("api/groq")]
public class GroqController : ControllerBase
{
    private readonly IGroqService _groq;

    public GroqController(IGroqService groq)
    {
        _groq = groq;
    }

    [HttpPost("review")]
    public async Task<IActionResult> Review([FromBody] ReviewRequest request)
    {
        var result = await _groq.ReviewCodeAsync(request.Language, request.Filename, request.Diff);
        return Ok(new { review = result });
    }
}

public record ReviewRequest(string Language, string Filename, string Diff);