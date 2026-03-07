using Revix.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;
using System.Text.Json;
using Revix.Core.Interfaces;
using Revix.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// =======================
// SERVICES
// =======================

builder.Services.AddControllers();

builder.Services.AddDbContext<RevixDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));

// =======================
// AUTHENTICATION
// =======================

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "Revix.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
})
.AddOAuth("GitHub", options =>
{
    options.ClientId = builder.Configuration["GitHub:ClientId"]!;
    options.ClientSecret = builder.Configuration["GitHub:ClientSecret"]!;
    options.CallbackPath = "/auth/callback";

    options.AuthorizationEndpoint   = "https://github.com/login/oauth/authorize";
    options.TokenEndpoint           = "https://github.com/login/oauth/access_token";
    options.UserInformationEndpoint = "https://api.github.com/user";

    options.Scope.Add("read:user");
    options.Scope.Add("repo");

    options.SaveTokens = true;

    options.CorrelationCookie.Name         = ".Revix.OAuth.Correlation";
    options.CorrelationCookie.HttpOnly     = true;
    options.CorrelationCookie.IsEssential  = true;
    options.CorrelationCookie.SameSite     = SameSiteMode.Lax;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;

    options.Events = new OAuthEvents
    {
        OnCreatingTicket = async context =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
            request.Headers.Add("User-Agent", "Revix");

            var response = await context.Backchannel.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var userJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            var githubId = userJson.RootElement.GetProperty("id").GetInt64().ToString();
            var username = userJson.RootElement.GetProperty("login").GetString()!;

            context.Identity!.AddClaim(new Claim(ClaimTypes.NameIdentifier, githubId));
            context.Identity.AddClaim(new Claim(ClaimTypes.Name, username));

            
            var authService = context.HttpContext.RequestServices
                                    .GetRequiredService<IGitHubAuthService>();

            await authService.HandleGitHubLoginAsync(githubId, username, context.AccessToken!);
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
});

// =======================
// COOKIE POLICY
// =======================

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure               = CookieSecurePolicy.Always;
    options.CheckConsentNeeded   = _ => false;
});

builder.Services.AddDataProtection();
builder.Services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
builder.Services.AddScoped<IGitHubAuthService, GitHubAuthService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();

// =======================
// PIPELINE
// =======================

var app = builder.Build();
app.UseForwardedHeaders();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();