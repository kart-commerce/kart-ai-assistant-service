using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Kart.AiAssistant.Api.HealthChecks;

/// <summary>Conversation-session storage (design-decisions.md) — unreachable Redis degrades this
/// service to "no follow-up context available," so it's a readiness signal, not fatal-fast.</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer connectionMultiplexer) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await connectionMultiplexer.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"Redis reachable, latency {latency.TotalMilliseconds:F1}ms");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable.", ex);
        }
    }
}
