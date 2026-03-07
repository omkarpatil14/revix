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
    [HttpGet("login")]
    public IActionResult Login()
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = "/auth/me"
        };

        return Challenge(properties, "GitHub");
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var username = User.FindFirst(ClaimTypes.Name)?.Value;

        return Ok(new
        {
            Message = "Login Successful",
            GitHubId = userId,
            Username = username
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