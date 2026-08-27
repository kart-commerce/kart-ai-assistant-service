using Kart.AiAssistant.Application.Common.Models;

namespace Kart.AiAssistant.Application.Common.Interfaces;

/// <summary>
/// This service's exactly-one synchronous dependency (ADR-0024). Deliberately generic — takes an
/// already-resolved <see cref="QueryPlan"/> (endpoint path + query-string parameters, built by
/// deterministic application code, FR-002) and returns the raw JSON body unmodified (FR-003:
/// "the assistant never caches or locally recomputes what Analytics itself returns"). Per-metric
/// extraction of the raw body happens in the calling handler, not here — this client has no
/// knowledge of what a "revenue" or "product-performance" shape looks like.
/// </summary>
public interface IAnalyticsClient
{
    Task<AnalyticsQueryOutcome> ExecuteAsync(QueryPlan plan, CancellationToken cancellationToken);
}
