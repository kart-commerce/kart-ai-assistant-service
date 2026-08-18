using Kart.AiAssistant.Domain.Enums;
using Kart.AiAssistant.Domain.Shared;

namespace Kart.AiAssistant.Domain.ConversationSessions;

/// <summary>
/// The canonical structured intent (genai-business-assistant-spec.md §8) — the ONLY thing the
/// LLM's plan call is allowed to produce, and the authoritative state a follow-up turn merges
/// against (§14.1). A pure value object: two instances with the same field values are the same
/// intent, and a follow-up produces a brand new instance (fields carried forward are copied, not
/// referenced) so an older AuditRecord's snapshot can never be mutated out from under it by a
/// later turn — see ddd-model.md's "Value Object, Not Entity" reasoning.
/// </summary>
public sealed record ResolvedIntent(
    MetricType? Metric,
    EntityType? Entity,
    AggregationType? Aggregation,
    IReadOnlyList<DimensionType> Dimensions,
    IntentFilters Filters,
    RankingSpec? Ranking,
    ComparisonSpec? Comparison,
    VisualizationType? VisualizationHint,
    bool ClarificationNeeded)
{
    public static ResolvedIntent Empty { get; } = new(
        Metric: null,
        Entity: null,
        Aggregation: null,
        Dimensions: Array.Empty<DimensionType>(),
        Filters: IntentFilters.Empty,
        Ranking: null,
        Comparison: null,
        VisualizationHint: null,
        ClarificationNeeded: false);

    /// <summary>
    /// FR-007's merge rule: every field the follow-up message doesn't address carries forward
    /// unchanged; only fields the caller explicitly supplies in <paramref name="overrides"/> are
    /// replaced. A `null` in `overrides.Filters.DateRange` etc. means "not mentioned," not
    /// "clear it" — clearing a filter is expressed by the planner emitting an explicit sentinel
    /// the application layer maps before calling this (never a bare null vs. unset ambiguity).
    /// </summary>
    public ResolvedIntent MergeFollowUp(ResolvedIntentPatch overrides) => this with
    {
        Metric = overrides.Metric ?? Metric,
        Entity = overrides.Entity ?? Entity,
        Aggregation = overrides.Aggregation ?? Aggregation,
        Dimensions = overrides.Dimensions ?? Dimensions,
        Filters = Filters.MergeFollowUp(overrides.Filters),
        Ranking = overrides.Ranking ?? Ranking,
        Comparison = overrides.Comparison ?? Comparison,
        VisualizationHint = overrides.VisualizationHint ?? VisualizationHint,
        ClarificationNeeded = overrides.ClarificationNeeded ?? false,
    };
}

/// <summary>A follow-up planning result before it's merged onto the prior turn's intent — every
/// field is optional because "not mentioned by this message" must be distinguishable from "the
/// user cleared this filter."</summary>
public sealed record ResolvedIntentPatch(
    MetricType? Metric = null,
    EntityType? Entity = null,
    AggregationType? Aggregation = null,
    IReadOnlyList<DimensionType>? Dimensions = null,
    IntentFiltersPatch? Filters = null,
    RankingSpec? Ranking = null,
    ComparisonSpec? Comparison = null,
    VisualizationType? VisualizationHint = null,
    bool? ClarificationNeeded = null,
    bool IsContextReset = false);

public sealed record IntentFilters(
    DateRange? DateRange,
    string? Category,
    string? Sku,
    string? Channel,
    string? ActionType)
{
    public static IntentFilters Empty { get; } = new(null, null, null, null, null);

    public IntentFilters MergeFollowUp(IntentFiltersPatch? patch)
    {
        if (patch is null)
        {
            return this;
        }

        return new IntentFilters(
            patch.DateRange ?? DateRange,
            patch.CategoryCleared ? null : patch.Category ?? Category,
            patch.SkuCleared ? null : patch.Sku ?? Sku,
            patch.ChannelCleared ? null : patch.Channel ?? Channel,
            patch.ActionTypeCleared ? null : patch.ActionType ?? ActionType);
    }
}

public sealed record IntentFiltersPatch(
    DateRange? DateRange = null,
    string? Category = null,
    bool CategoryCleared = false,
    string? Sku = null,
    bool SkuCleared = false,
    string? Channel = null,
    bool ChannelCleared = false,
    string? ActionType = null,
    bool ActionTypeCleared = false);

public sealed record RankingSpec(MetricType SortBy, RankDirection Direction, int Limit);

public enum RankDirection
{
    Desc,
    Asc,
}

public sealed record ComparisonSpec(DateRange BaselineDateRange);
