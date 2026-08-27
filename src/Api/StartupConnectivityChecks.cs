using Kart.AiAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Kart.AiAssistant.Api;

/// <summary>Fail-fast startup probes (mirrors kart-admin-service's/kart-analytics-service's own
/// StartupConnectivityChecks) — a misconfigured connection string surfaces immediately in the
/// logs at boot, not on the first request.</summary>
public static class StartupConnectivityChecks
{
    public static async Task RunAsync(WebApplication app, AiAssistantDbContext dbContext)
    {
        var logger = app.Logger;

        await CheckAsync(logger, "Postgres", async () =>
        {
            await dbContext.Database.CanConnectAsync();
        });

        await CheckAsync(logger, "Redis", async () =>
        {
            var multiplexer = app.Services.GetRequiredService<IConnectionMultiplexer>();
            await multiplexer.GetDatabase().PingAsync();
        });
    }

    private static async Task CheckAsync(ILogger logger, string dependency, Func<Task> connect)
    {
        logger.LogInformation("Connecting AiAssistant {Dependency} ...", dependency);
        await connect();
        logger.LogInformation("{Dependency} connected", dependency);
    }
}
