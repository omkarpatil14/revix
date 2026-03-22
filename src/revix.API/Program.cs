using Revix.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using System.Security.Claims;
using System.Text.Json;
using Revix.Core.Interfaces;
using Revix.Infrastructure.Services;
using Polly;
using StackExchange.Redis;
using Revix.Core.Constants;
using Revix.Worker;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<RevixDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    ));

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(builder.Configuration["App:FrontendUrl"]!)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ✅ Build Redis connection once, reuse everywhere
var redisConnection = ConnectionMultiplexer.Connect(
    builder.Configuration["Redis:ConnectionString"]!);

builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

// ✅ Fixed Data Protection
builder.Services.AddDataProtection()
    .SetApplicationName("Revix")
    .PersistKeysToStackExchangeRedis(redisConnection, "DataProtection-Keys");

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

    // ✅ KEY FIX: SameSite=None so cookie survives the GitHub redirect
    options.CorrelationCookie.Name         = ".Revix.OAuth.Correlation";
    options.CorrelationCookie.HttpOnly     = true;
    options.CorrelationCookie.IsEssential  = true;
    options.CorrelationCookie.SameSite     = SameSiteMode.None;
    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    // ✅ This prevents multiple stale correlation cookies building up
    options.CorrelationCookie.Path        = "/auth/callback";

    options.Events = new OAuthEvents
    {
        OnRedirectToAuthorizationEndpoint = context =>
        {
            var uri = context.RedirectUri.Replace("http://", "https://");
            context.Response.Redirect(uri);
            return Task.CompletedTask;
        },

        OnCreatingTicket = async context =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
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
        },

        OnRemoteFailure = context =>
        {
            var error = context.Failure?.Message ?? "Unknown OAuth error";
            context.Response.Redirect($"/auth/error?message={Uri.EscapeDataString(error)}");
            context.HandleResponse();
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddHttpClient<IGroqService, GroqService>()
    .AddTransientHttpErrorPolicy(p =>
        p.WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddScoped<ITokenEncryptionService, TokenEncryptionService>();
builder.Services.AddScoped<IGitHubAuthService, GitHubAuthService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddScoped<IGitHubService, GitHubService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddSingleton<ReviewQueue>();
builder.Services.AddScoped<ReviewOrchestrator>();
builder.Services.AddHostedService<ReviewWorkerService>();

var app = builder.Build();

// ✅ Force HTTPS scheme — Render terminates SSL at proxy level
app.Use((context, next) =>
{
    context.Request.Scheme = "https";
    return next();
});

app.UseForwardedHeaders();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
    Console.WriteLine("ℹ️ Consumer group already exists.");
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();
