using Microsoft.AspNetCore.Mvc;

namespace revix.API.Controllers;

[ApiController]
[Route("/")]
public class HomeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            application = "Revix API",
            version = "1.0.0",
            status = "Running",
            timestamp = DateTime.UtcNow
        });
    }
}