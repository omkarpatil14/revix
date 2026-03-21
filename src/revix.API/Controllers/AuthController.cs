using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Revix.Core.Interfaces;

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

    [HttpGet("github/callback")]
    public IActionResult GitHubAppCallback([FromQuery] string installation_id, [FromQuery] string setup_action)
    {
        var frontendUrl = _config["App:FrontendUrl"]!;
        return Redirect($"{frontendUrl}/repos?installed=true");
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst(ClaimTypes.Name)?.Value;
        var  avatarUrl = User.FindFirst("avatar_url")?.Value;
        var profileUrl = User.FindFirst("profile_url")?.Value;

        return Ok(new
        {
            Message = "Login Successful",
            GitHubId = userId,
            Username = username,
            AvatarUrl  = avatarUrl,
            ProfileUrl = profileUrl
        });
    }

    [HttpGet("error")]
    public IActionResult Error()
    {
        return StatusCode(500, "OAuth login failed. Check server logs.");
    }

    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok("Logged out");
    }


}