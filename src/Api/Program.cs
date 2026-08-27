using System.Text.Json;
using System.Text.Json.Serialization;
using Kart.AiAssistant.Api;
using Kart.AiAssistant.Api.Endpoints;
using Kart.AiAssistant.Api.HealthChecks;
using Kart.AiAssistant.Application;
using Kart.AiAssistant.Infrastructure;
using Kart.AiAssistant.Infrastructure.Persistence;
using Kart.AiAssistant.Infrastructure.Security;
using Kart.Shared.Configuration;
using Kart.Shared.ErrorHandling;
using Kart.Shared.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// kart-conventions.md Configuration Management: GlobalConfig external-secrets-file bootstrap,
// same pattern every other Kart service uses (kart-admin-service/kart-analytics-service Program.cs).
builder.AddKartGlobalConfig("kart-ai-assistant-service");
builder.AddKartObservability("kart-ai-assistant-service");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddKartErrorHandling();

// AIA-20 (Phase 6): rate limiting on POST /v1/ai-assistant/query, mirroring the Gateway's own
// tiered token-bucket approach (source spec §24 Phase 6) — keyed per-authenticated-subject so one
// noisy user can't starve another, generous enough that no legitimate chat session ever sees a 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("ai-assistant-query", httpContext =>
        RateLimitPartition.GetTokenBucketLimiter(
            httpContext.User.FindFirst("sub")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 20,
                TokensPerPeriod = 20,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

builder.Services.AddHealthChecks()
    .AddCheck<AiAssistantDbHealthCheck>("ai-assistant-db", tags: ["ready"])
    .AddCheck<RedisHealthCheck>("ai-assistant-redis", tags: ["ready"]);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseKartErrorHandling();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapAiAssistantEndpoints();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AiAssistantDbContext>();
    await StartupConnectivityChecks.RunAsync(app, dbContext);
}

app.Run();

public partial class Program
{
}
