namespace Kart.AiAssistant.Application.Common.Models;

/// <summary>
/// One resolved query plan (FR-002): exactly one registered endpoint + the parameters that
/// satisfy its documented contract. Never LLM-authored — built by
/// <c>QueryPlanBuilder</c> from a validated <see cref="Domain.ConversationSessions.ResolvedIntent"/>
/// against <c>MetricEndpointRegistry</c>'s versioned config.
/// </summary>
public sealed record QueryPlan(string EndpointPath, IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>Period-over-period comparison (FR-002 note: "Two calls to the same dashboard,
    /// different from/to, diffed by kart-ai-assistant-service") needs a second call to the same
    /// endpoint with the baseline window — represented as an optional second plan rather than
    /// modeling a "batch of plans" abstraction nothing else needs.</summary>
    public QueryPlan? BaselineComparisonPlan { get; init; }
}
