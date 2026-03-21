using Revix.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;
using System.Text.Json;
using Revix.Core.Interfaces;
using Revix.Infrastructure.Services;
using Polly;
using Polly.Extensions.Http;
using StackExchange.Redis;
using Revix.Core.Constants;
using Revix.Worker;
using Microsoft.AspNetCore.DataProtection;

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
// CORS
// =======================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                builder.Configuration["App:FrontendUrl"] ?? "http://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// =======================
// FORWARDED HEADERS (trust ngrok proxy)
// =======================

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                             | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// =======================
// AUTHENTICATION
// =======================

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name         = "Revix.Auth";
    options.Cookie.HttpOnly     = true;
    options.Cookie.IsEssential  = true;
    options.Cookie.SameSite     = SameSiteMode.None;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = 401;
        return Task.CompletedTask;
    };
})
.AddOAuth("GitHub", options =>
{
    options.ClientId     = builder.Configuration["GitHub:ClientId"]!;
    options.ClientSecret = builder.Configuration["GitHub:ClientSecret"]!;
    options.CallbackPath = "/auth/callback";

    options.AuthorizationEndpoint   = "https://github.com/login/oauth/authorize";
    options.TokenEndpoint           = "https://github.com/login/oauth/access_token";
    options.UserInformationEndpoint = "https://api.github.com/user";

    options.Scope.Add("read:user");
    options.Scope.Add("repo");
    options.Scope.Add("admin:repo_hook");

    options.SaveTokens = true;

    options.CorrelationCookie.Name         = ".Revix.OAuth.Correlation";
    options.CorrelationCookie.HttpOnly     = true;
    options.CorrelationCookie.IsEssential  = true;
    options.CorrelationCookie.SameSite     = SameSiteMode.None;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

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

            var githubId   = userJson.RootElement.GetProperty("id").GetInt64().ToString();
            var username   = userJson.RootElement.GetProperty("login").GetString()!;
            var avatarUrl  = userJson.RootElement.GetProperty("avatar_url").GetString()!;
            var profileUrl = userJson.RootElement.GetProperty("html_url").GetString()!;

            context.Identity!.AddClaim(new Claim("avatar_url",  avatarUrl));
            context.Identity!.AddClaim(new Claim("profile_url", profileUrl));
            context.Identity!.AddClaim(new Claim(ClaimTypes.NameIdentifier, githubId));
            context.Identity.AddClaim(new Claim(ClaimTypes.Name, username));

            var authService = context.HttpContext.RequestServices
                                    .GetRequiredService<IGitHubAuthService>();

            await authService.HandleGitHubLoginAsync(githubId, username, context.AccessToken!);
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddHttpClient<IGroqService, GroqService>()
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

// =======================
// COOKIE POLICY
// =======================

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.None;
    options.Secure               = CookieSecurePolicy.Always;
    options.CheckConsentNeeded   = _ => false;
});

// =======================
// OTHER SERVICES
// =======================

builder.Services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
builder.Services.AddScoped<IGitHubAuthService, GitHubAuthService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(
        builder.Configuration["Redis:ConnectionString"]!));
builder.Services.AddSingleton<ReviewQueue>();
builder.Services.AddScoped<ReviewOrchestrator>();
builder.Services.AddHostedService<ReviewWorkerService>();
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(
        ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!),
        "DataProtection-Keys");

// =======================
// BUILD
// =======================

var app = builder.Build();

app.UseCors("Frontend");
app.UseForwardedHeaders();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// =======================
// REDIS CONSUMER GROUP
// =======================

var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
var redisDb = redis.GetDatabase();
try
{
    await redisDb.StreamCreateConsumerGroupAsync(
        StreamNames.Reviews,
        StreamNames.ConsumerGroup,
        StreamPosition.NewMessages,
        createStream: true);

    Console.WriteLine("✅ Redis consumer group created.");
}
catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
{
    Console.WriteLine("ℹ️ Consumer group already exists, skipping creation.");
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();