namespace Kart.AiAssistant.Infrastructure.ExternalClients;

/// <summary>
/// Binds the "AnalyticsService" configuration section — kart-analytics-service is this service's
/// exactly-one synchronous downstream dependency (ADR-0024). Default port 8097 is
/// kart-analytics-service's own real local dev port (its <c>docker-compose.yml</c>:
/// <c>${ANALYTICS_PORT:-8097}:8080</c>) — not listed in kart-devops/ports.env (that registry only
/// covers the Gateway-fronted services), confirmed instead from Analytics' own compose file.
/// </summary>
public sealed class AnalyticsServiceOptions
{
    public const string SectionName = "AnalyticsService";

    public string BaseUrl { get; set; } = "http://localhost:8097";

    /// <summary>Carved from the still-open end-to-end turn latency budget (Infrastructure-1,
    /// requirement-spec §8) — a reasonable placeholder, not a numeric decision this doc invents.</summary>
    public int TimeoutMilliseconds { get; set; } = 5000;
}
