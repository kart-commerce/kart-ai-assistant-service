using Kart.AiAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kart.AiAssistant.Api.HealthChecks;

/// <summary>Fails readiness (not liveness) if Postgres is unreachable or a migration is pending
/// — mirrors kart-admin-service's own AdminDbHealthCheck precedent so a pod never takes traffic
/// against an un-migrated audit-log schema.</summary>
public sealed class AiAssistantDbHealthCheck(AiAssistantDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("Cannot connect to the ai-assistant audit database.");
            }

            var pending = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
            return pending.Any()
                ? HealthCheckResult.Unhealthy($"Pending migrations: {string.Join(", ", pending)}")
                : HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database health check failed.", ex);
        }
    }
}
