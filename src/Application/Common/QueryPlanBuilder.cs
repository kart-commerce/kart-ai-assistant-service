using System.Globalization;
using Kart.AiAssistant.Application.Common.Models;
using Kart.AiAssistant.Application.Common.Registry;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;
using Kart.Shared.Domain;

namespace Kart.AiAssistant.Application.Common;

/// <summary>
/// The outcome of a successful <see cref="QueryPlanBuilder.Build"/> call. Wraps the fixed
/// <see cref="QueryPlan"/> contract with the two extra facts the handler needs but that
/// <see cref="QueryPlan"/> itself (an already-published, unmodifiable shape) has no field for:
/// which registry entry was matched, and — per edge-cases.md's "Schema-Valid Intent Requests a
/// Rank/Limit Analytics Cannot Fulfill" — the originally-requested ranking limit when it had to be
/// clamped, so the caller can disclose the clamp in the answer text rather than silently serving a
/// smaller list than asked for.
/// </summary>
public sealed record QueryPlanBuildOutcome(
    QueryPlan Plan,
    EndpointRegistration Registration,
    int? OriginalRequestedLimit)
{
    public bool WasRankingLimitClamped => OriginalRequestedLimit is not null;
}

/// <summary>
/// FR-002's deterministic intent→endpoint translator. Takes a <see cref="ResolvedIntent"/> whose
/// date range(s) are already resolved to explicit UTC instants (never a relative phrase — that
/// resolution happens before this class ever sees the intent, per <c>DateRange</c>'s own
/// contract) and produces exactly one <see cref="QueryPlan"/> against
/// <see cref="MetricEndpointRegistry"/>'s versioned config — never an LLM-authored endpoint path
/// or query string (§9, §16). A registry miss is reported as <see cref="Result{T}"/> failure for
/// the caller to turn into FR-009's "unsupported" response; this class raises no exceptions for an
/// ordinary unsupported-capability outcome.
/// </summary>
public static class QueryPlanBuilder
{
    private const int HardRankingLimitCeiling = 100;

    public static Result<QueryPlanBuildOutcome> Build(ResolvedIntent intent)
    {
        var hasRanking = intent.Ranking is not null;

        if (!MetricEndpointRegistry.TryResolve(intent.Metric, intent.Entity, hasRanking, out var registration))
        {
            return Result.Failure<QueryPlanBuildOutcome>(Error.NotFound(
                $"No registered analytics capability for metric='{intent.Metric?.ToString() ?? "(none)"}', entity='{intent.Entity?.ToString() ?? "(none)"}'."));
        }

        if (intent.Filters.DateRange is not { IsValid: true } dateRange)
        {
            // Edge-cases.md "Intent Passes Validation but the Resolved Query Plan Is Itself
            // Malformed" — a schema-valid intent can still be missing the one field every
            // registered endpoint requires (§7: "Date range — All dashboards (required)").
            return Result.Failure<QueryPlanBuildOutcome>(
                Error.Validation("The resolved intent has no valid date range to query — from/to must both be present and to > from."));
        }

        int? originalRequestedLimit = null;
        var parameters = new Dictionary<string, string>
        {
            ["from"] = FormatIso8601(dateRange.From),
            ["to"] = FormatIso8601(dateRange.To),
        };

        if (registration.SupportsGranularity)
        {
            // kart-analytics-service's `Granularity` query parameter binds via .NET's default
            // enum model binder, which is case-sensitive to the C# enum member's own casing
            // ("Day"/"Hour") — every existing dashboard caller already sends PascalCase; lowercase
            // fails minimal-API parameter binding with a 400 (BadHttpRequestException), which this
            // client's own resilience layer otherwise reports as a generic "unavailable" error.
            parameters["granularity"] = intent.Dimensions.Contains(DimensionType.HourOfDay) ? "Hour" : "Day";
        }

        if (registration.SupportsCategory && !string.IsNullOrWhiteSpace(intent.Filters.Category))
        {
            parameters["category"] = intent.Filters.Category;
        }

        if (registration.SupportsSku && !string.IsNullOrWhiteSpace(intent.Filters.Sku))
        {
            parameters["sku"] = intent.Filters.Sku;
        }

        if (registration.SupportsChannel && !string.IsNullOrWhiteSpace(intent.Filters.Channel))
        {
            parameters["channel"] = intent.Filters.Channel;
        }

        if (registration.SupportsActionType && !string.IsNullOrWhiteSpace(intent.Filters.ActionType))
        {
            parameters["actionType"] = intent.Filters.ActionType;
        }

        if (registration.SupportsRanking && intent.Ranking is { } ranking)
        {
            var maxLimit = Math.Min(registration.MaxRankingLimit ?? HardRankingLimitCeiling, HardRankingLimitCeiling);
            var clampedLimit = Math.Clamp(ranking.Limit, 1, maxLimit);
            if (clampedLimit != ranking.Limit)
            {
                originalRequestedLimit = ranking.Limit;
            }

            parameters["metric"] = (intent.Metric ?? MetricType.Revenue).ToWire();
            parameters["limit"] = clampedLimit.ToString(CultureInfo.InvariantCulture);
            parameters["direction"] = ranking.Direction.ToWire();
        }

        var plan = new QueryPlan(registration.EndpointPath, parameters)
        {
            BaselineComparisonPlan = BuildBaselinePlan(intent, registration, parameters),
        };

        return Result.Success(new QueryPlanBuildOutcome(plan, registration, originalRequestedLimit));
    }

    /// <summary>
    /// FR-002's period-over-period rule (§5 item 4): "two calls to the same dashboard, different
    /// from/to, diffed by kart-ai-assistant-service." Reuses every non-window parameter from the
    /// primary plan unchanged — only <c>from</c>/<c>to</c> differ — and never applies to a ranking
    /// call (comparing two independently-ranked top-N lists is not a query type §5 defines).
    /// </summary>
    private static QueryPlan? BuildBaselinePlan(ResolvedIntent intent, EndpointRegistration registration, IReadOnlyDictionary<string, string> primaryParameters)
    {
        if (intent.Comparison is not { BaselineDateRange.IsValid: true } comparison || registration.SupportsRanking)
        {
            return null;
        }

        var baselineParameters = new Dictionary<string, string>(primaryParameters)
        {
            ["from"] = FormatIso8601(comparison.BaselineDateRange.From),
            ["to"] = FormatIso8601(comparison.BaselineDateRange.To),
        };

        return new QueryPlan(registration.EndpointPath, baselineParameters);
    }

    private static string FormatIso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
