using Revix.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Revix.Core.Interfaces;
using Revix.Infrastructure.Services;
using Polly;
using StackExchange.Redis;
using Revix.Core.Constants;
using Revix.Worker;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Authentication;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

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

var redisConnectionString = builder.Configuration["Redis:ConnectionString"]!
    .Trim('"').Trim();

Console.WriteLine($"🔌 Connecting to Redis: {redisConnectionString[..30]}...");

var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
redisOptions.Ssl = true;
redisOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
redisOptions.AbortOnConnectFail = false;
redisOptions.ConnectTimeout = 10000;
redisOptions.SyncTimeout = 10000;

var redisConnection = await ConnectionMultiplexer.ConnectAsync(redisOptions);
Console.WriteLine($"✅ Redis connected: {redisConnection.IsConnected}");

builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);


builder.Services.AddDataProtection()
    .SetApplicationName("Revix")
    .PersistKeysToStackExchangeRedis(redisConnection, "DataProtection-Keys")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

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

app.Use((context, next) =>
{
    context.Request.Scheme = "https";
    return next();
});

app.UseForwardedHeaders();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();


app.Use(async (context, next) =>
{
    var logger = context.RequestServices
        .GetRequiredService<ILogger<Program>>();

    if (context.Request.Path.StartsWithSegments("/auth"))
    {
        logger.LogInformation("=== AUTH REQUEST ===");
        logger.LogInformation("Path: {Path}", context.Request.Path);
        logger.LogInformation("Cookies: {Cookies}", string.Join(", ", context.Request.Cookies.Keys));
        logger.LogInformation("Has Revix.Auth: {HasCookie}", context.Request.Cookies.ContainsKey("Revix.Auth"));
        logger.LogInformation("Is Authenticated: {Auth}", context.User?.Identity?.IsAuthenticated);
        logger.LogInformation("User: {User}", context.User?.Identity?.Name);

        await next();

        logger.LogInformation("=== AUTH RESPONSE ===");
        logger.LogInformation("Status: {Status}", context.Response.StatusCode);
    }
    else
    {
        await next();
    }
});

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

if (!app.Environment.IsDevelopment())
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    app.Urls.Add($"http://0.0.0.0:{port}");
}

app.Run();