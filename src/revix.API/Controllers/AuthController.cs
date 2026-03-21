using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = "/auth/complete"
        };
        return Challenge(properties, "GitHub");
    }

    [HttpGet("complete")]
    [Authorize]
    public IActionResult Complete()
    {
        var frontendUrl = _config["App:FrontendUrl"]!;
        return Redirect($"{frontendUrl}/dashboard");
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        return Ok(new
        {
            GitHubId   = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            Username   = User.FindFirst(ClaimTypes.Name)?.Value,
            AvatarUrl  = User.FindFirst("avatar_url")?.Value,
            ProfileUrl = User.FindFirst("profile_url")?.Value,
        });
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok("Logged out");
    }
}