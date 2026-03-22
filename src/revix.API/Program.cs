using Revix.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
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

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

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

// ✅ Data Protection keys persisted to Redis
builder.Services.AddDataProtection()
    .SetApplicationName("Revix")
    .PersistKeysToStackExchangeRedis(redisConnection, "DataProtection-Keys");

// ✅ Only cookie auth — OAuth handled manually in controller
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

app.Use(async (context, next) =>
{
    var logger = context.RequestServices
        .GetRequiredService<ILogger<Program>>();

    if (context.Request.Path.StartsWithSegments("/auth"))
    {
        logger.LogInformation("=== AUTH REQUEST ===");
        logger.LogInformation("Path: {Path}", context.Request.Path);
        logger.LogInformation("Method: {Method}", context.Request.Method);
        logger.LogInformation("Origin: {Origin}", context.Request.Headers["Origin"]);
        logger.LogInformation("Cookies: {Cookies}", string.Join(", ", context.Request.Cookies.Keys));
        logger.LogInformation("Has Revix.Auth: {HasCookie}", context.Request.Cookies.ContainsKey("Revix.Auth"));
        
        await next();
        
        logger.LogInformation("=== AUTH RESPONSE ===");
        logger.LogInformation("Status: {Status}", context.Response.StatusCode);
        logger.LogInformation("Is Authenticated: {Auth}", context.User?.Identity?.IsAuthenticated);
        logger.LogInformation("User: {User}", context.User?.Identity?.Name);
    }
    else
    {
        await next();
    }
});


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