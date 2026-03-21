using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using Revix.Core.Interfaces;
using Revix.Infrastructure.Services; 

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;
    private readonly IGitHubAuthService _authService;

    public AuthController(
        IConfiguration config,
        IHttpClientFactory http,
        IGitHubAuthService authService)
    {
        _config = config;
        _http = http;
        _authService = authService;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var clientId   = _config["GitHub:ClientId"]!;
        var backendUrl = _config["App:BackendUrl"]!;
        var callback   = Uri.EscapeDataString($"{backendUrl}/auth/callback");
        var scope      = Uri.EscapeDataString("read:user repo admin:repo_hook");

        return Redirect(
            $"https://github.com/login/oauth/authorize" +
            $"?client_id={clientId}" +
            $"&redirect_uri={callback}" +
            $"&scope={scope}"
        );
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code)
    {
        var frontendUrl  = _config["App:FrontendUrl"]!;
        var clientId     = _config["GitHub:ClientId"]!;
        var clientSecret = _config["GitHub:ClientSecret"]!;

        if (string.IsNullOrEmpty(code))
            return Redirect($"{frontendUrl}/login?error=no_code");

        var client = _http.CreateClient();
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("User-Agent", "Revix");

        var tokenRes = await client.PostAsJsonAsync(
            "https://github.com/login/oauth/access_token",
            new { client_id = clientId, client_secret = clientSecret, code });

        var tokenDoc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync());

        if (!tokenDoc.RootElement.TryGetProperty("access_token", out var tokenEl))
            return Redirect($"{frontendUrl}/login?error=token_failed");

        var accessToken = tokenEl.GetString()!;

        var userClient = _http.CreateClient();
        userClient.DefaultRequestHeaders.Add("Accept", "application/json");
        userClient.DefaultRequestHeaders.Add("User-Agent", "Revix");
        userClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var userDoc = JsonDocument.Parse(
            await userClient.GetStringAsync("https://api.github.com/user"));

        var githubId   = userDoc.RootElement.GetProperty("id").GetInt64().ToString();
        var username   = userDoc.RootElement.GetProperty("login").GetString()!;
        var avatarUrl  = userDoc.RootElement.GetProperty("avatar_url").GetString()!;
        var profileUrl = userDoc.RootElement.GetProperty("html_url").GetString()!;

        await _authService.HandleGitHubLoginAsync(githubId, username, accessToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, githubId),
            new(ClaimTypes.Name, username),
            new("avatar_url", avatarUrl),
            new("profile_url", profileUrl),
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true }
        );

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