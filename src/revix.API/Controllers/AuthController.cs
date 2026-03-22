using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using StackExchange.Redis;
using Revix.Infrastructure.Services; 

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IGitHubAuthService _authService;

    public AuthController(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        IConnectionMultiplexer redis,
        IGitHubAuthService authService)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _redis = redis;
        _authService = authService;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        // Generate random state and store in Redis
        var state = Guid.NewGuid().ToString("N");
        var db    = _redis.GetDatabase();
        db.StringSet($"oauth:state:{state}", "valid", TimeSpan.FromMinutes(10));

        var clientId    = _config["GitHub:ClientId"];
        var backendUrl  = _config["App:BackendUrl"];
        var callbackUrl = Uri.EscapeDataString($"{backendUrl}/auth/callback");

        var githubUrl = "https://github.com/login/oauth/authorize" +
                        $"?client_id={clientId}" +
                        $"&redirect_uri={callbackUrl}" +
                        $"&scope=read:user%20repo%20admin:repo_hook" +
                        $"&state={state}";

        return Redirect(githubUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string code,
        [FromQuery] string state)
    {
        var frontendUrl = _config["App:FrontendUrl"]!;

        // Validate state from Redis
        var db           = _redis.GetDatabase();
        var storedState  = await db.StringGetAsync($"oauth:state:{state}");
        if (storedState.IsNullOrEmpty)
            return Redirect($"{frontendUrl}?error=invalid_state");

        // Delete state so it cannot be reused
        await db.KeyDeleteAsync($"oauth:state:{state}");

        // Exchange code for access token
        var client       = _httpClientFactory.CreateClient();
        var tokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "https://github.com/login/oauth/access_token");

        tokenRequest.Headers.Add("Accept", "application/json");
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"]     = _config["GitHub:ClientId"]!,
            ["client_secret"] = _config["GitHub:ClientSecret"]!,
            ["code"]          = code,
            ["redirect_uri"]  = $"{_config["App:BackendUrl"]}/auth/callback"
        });

        var tokenResponse = await client.SendAsync(tokenRequest);
        var tokenJson     = await tokenResponse.Content.ReadAsStringAsync();
        var tokenDoc      = JsonDocument.Parse(tokenJson);

        if (!tokenDoc.RootElement.TryGetProperty("access_token", out var tokenElement))
            return Redirect($"{frontendUrl}?error=token_exchange_failed");

        var accessToken = tokenElement.GetString()!;

        // Get user info from GitHub
        var userRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/user");

        userRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        userRequest.Headers.Add("User-Agent", "Revix");

        var userResponse = await client.SendAsync(userRequest);
        var userJson     = JsonDocument.Parse(
            await userResponse.Content.ReadAsStringAsync());

        var githubId   = userJson.RootElement.GetProperty("id").GetInt64().ToString();
        var username   = userJson.RootElement.GetProperty("login").GetString()!;
        var avatarUrl  = userJson.RootElement.GetProperty("avatar_url").GetString()!;
        var profileUrl = userJson.RootElement.GetProperty("html_url").GetString()!;

        // Save or update user in database
        await _authService.HandleGitHubLoginAsync(githubId, username, accessToken);

        // Create cookie session
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, githubId),
            new Claim(ClaimTypes.Name,           username),
            new Claim("avatar_url",              avatarUrl),
            new Claim("profile_url",             profileUrl),
        };

        var identity  = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { 
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

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

    [HttpGet("error")]
    public IActionResult Error([FromQuery] string? message)
    {
        return Problem(
            detail: message ?? "An error occurred during authentication.",
            title: "Authentication Error",
            statusCode: 400);
    }
}