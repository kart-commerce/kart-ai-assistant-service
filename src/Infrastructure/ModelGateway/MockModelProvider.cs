using Kart.AiAssistant.Application.Common.Interfaces;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;
using Kart.AiAssistant.Domain.Shared;

namespace Kart.AiAssistant.Infrastructure.ModelGateway;

/// <summary>
/// design-decisions.md's "Model-Gateway / LLM-Provider Abstraction" decision names a real
/// provider/tier as this service's eventual model-gateway backend; until that's wired,
/// <see cref="IModelProvider"/> is implemented here by a deterministic, rule-based
/// natural-language-understanding engine — not a token stub. It is written to correctly answer
/// genai-business-assistant-spec.md's own worked examples (§3.3, §14.2, §15, §23) end-to-end,
/// including the ambiguity/clarification rules (§15.1), the default-metric-resolution table
/// (§15.2), and FR-004's grounding requirement on the explain call.
/// </summary>
public sealed class MockModelProvider : IModelProvider
{
    private static readonly TimeSpan MinSimulatedLatency = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan MaxSimulatedLatency = TimeSpan.FromMilliseconds(250);

    private readonly IClock _clock;

    public MockModelProvider(IClock clock)
    {
        _clock = clock;
    }

    public async Task<PlanCallResult> PlanAsync(PlanCallRequest request, CancellationToken cancellationToken)
    {
        var latencyMs = await SimulateLatencyAsync(cancellationToken);
        var message = request.Message ?? string.Empty;
        var outcome = ResolveIntent(message, request.PriorIntent, _clock.UtcNow);

        var tokenUsage = new TokenUsage(
            PromptTokens: EstimateTokens(message),
            CompletionTokens: EstimateTokens(outcome.Patch?.ToString() ?? outcome.Clarification?.Question ?? string.Empty));

        return new PlanCallResult(outcome.Patch, outcome.Clarification, latencyMs, tokenUsage);
    }

    public async Task<ExplainCallResult> ExplainAsync(ExplainCallRequest request, CancellationToken cancellationToken)
    {
        var latencyMs = await SimulateLatencyAsync(cancellationToken);
        var answerText = ExplainAnswerBuilder.Build(request.Intent, request.RawResultJson);

        var tokenUsage = new TokenUsage(
            PromptTokens: EstimateTokens(request.RawResultJson),
            CompletionTokens: EstimateTokens(answerText));

        return new ExplainCallResult(answerText, latencyMs, tokenUsage);
    }

    // ---- Plan-call resolution -------------------------------------------------------------

    private sealed record PlanOutcome(ResolvedIntentPatch? Patch, ClarificationRequest? Clarification);

    /// <summary>
    /// NOTE — coordination gap, flagged explicitly rather than silently worked around: §15.2's
    /// "state the assumption explicitly only the first time" behavior needs a signal threaded from
    /// this plan call out to whatever builds <see cref="TurnProvenance.UnqualifiedSuperlativeDefaultApplied"/>.
    /// Neither <see cref="ResolvedIntentPatch"/> nor <see cref="PlanCallResult"/> (both fixed
    /// Application-layer contracts) carry a slot for that signal — this method still resolves the
    /// *metric* correctly (defaults an unqualified superlative to Revenue per the table below), but
    /// has no wire to report "this specific resolution was a superlative default" back to the
    /// caller. The Application-layer handler that eventually owns <c>TurnProvenance</c> construction
    /// will need its own way to detect this (e.g. re-deriving it from the same message text), since
    /// this provider cannot carry it out-of-band through the fixed contracts above.
    /// </summary>
    private static PlanOutcome ResolveIntent(string message, ResolvedIntent? priorIntent, DateTimeOffset now)
    {
        if (priorIntent is not null)
        {
            if (IntentPatternMatcher.TryMatchCategoryFollowUp(message, out var category))
            {
                return Patch(new ResolvedIntentPatch(Filters: new IntentFiltersPatch(Category: category)));
            }

            if (IntentPatternMatcher.IsComparisonFollowUp(message))
            {
                var baseline = ComputeEqualLengthPrecedingWindow(priorIntent.Filters.DateRange, now);
                return Patch(new ResolvedIntentPatch(Comparison: new ComparisonSpec(baseline)));
            }

            if (IntentPatternMatcher.TryMatchVisualizationHint(message, out var visualizationType))
            {
                return Patch(new ResolvedIntentPatch(VisualizationHint: visualizationType));
            }
        }

        var isFollowUp = priorIntent is not null;

        // §7/§26-Data-1/§26-Data-2 — geography/seller are permanently out of scope; no Entity/
        // MetricType member exists for either, so degrading to an empty patch here lets the
        // Application layer's registry lookup fail naturally into FR-009's "unsupported" response.
        if (IntentPatternMatcher.IsOutOfScopeDimension(message))
        {
            return Patch(new ResolvedIntentPatch(
                Metric: null,
                Entity: null,
                Dimensions: Array.Empty<DimensionType>(),
                IsContextReset: isFollowUp));
        }

        if (IntentPatternMatcher.IsFunnelQuery(message))
        {
            if (!TryResolveDate(message, now, out var range, out var dateClarification))
            {
                return Clarify(dateClarification!);
            }

            return Patch(new ResolvedIntentPatch(
                Metric: MetricType.ConversionRate,
                Entity: EntityType.Funnel,
                Dimensions: [DimensionType.FunnelStage],
                Filters: new IntentFiltersPatch(DateRange: range),
                IsContextReset: isFollowUp));
        }

        if (IntentPatternMatcher.IsInventoryQuery(message))
        {
            if (!TryResolveDate(message, now, out var range, out var dateClarification))
            {
                return Clarify(dateClarification!);
            }

            return Patch(new ResolvedIntentPatch(
                Metric: MetricType.InventoryMovement,
                Entity: EntityType.Inventory,
                Aggregation: AggregationType.Count,
                Dimensions: [DimensionType.Time],
                Filters: new IntentFiltersPatch(DateRange: range),
                IsContextReset: isFollowUp));
        }

        if (IntentPatternMatcher.IsPeakHourQuery(message))
        {
            // §26-UX-2's own assumed default applies here even for an unstated date phrase; an
            // unresolvable-but-stated phrase ("recently") still clarifies like every other query.
            if (!TryResolveDate(message, now, out var range, out var dateClarification))
            {
                return Clarify(dateClarification!);
            }

            return Patch(new ResolvedIntentPatch(
                Metric: MetricType.Revenue,
                Entity: EntityType.Order,
                Aggregation: AggregationType.Avg,
                Dimensions: [DimensionType.HourOfDay],
                Filters: new IntentFiltersPatch(DateRange: range),
                IsContextReset: isFollowUp));
        }

        if (IntentPatternMatcher.TryMatchRankingWithLimit(message, out var limit, out var direction))
        {
            if (!TryResolveDate(message, now, out var range, out var dateClarification))
            {
                return Clarify(dateClarification!);
            }

            var metric = IntentPatternMatcher.ResolveRankingMetric(message);
            return Patch(BuildRankingPatch(metric, limit, direction, range, isFollowUp));
        }

        // §15.2's default-metric-resolution table: "top selling," "best-selling," "top products"
        // (no explicit metric) resolve silently to Revenue (assumed limit of 5, matching every
        // worked example in the spec that leaves the count unstated).
        if (IntentPatternMatcher.IsUnqualifiedSuperlativeWithContext(message))
        {
            if (!TryResolveDate(message, now, out var range, out var dateClarification))
            {
                return Clarify(dateClarification!);
            }

            var metric = IntentPatternMatcher.ResolveRankingMetric(message);
            return Patch(BuildRankingPatch(metric, 5, RankDirection.Desc, range, isFollowUp));
        }

        // §15.1's canonical hard-stop trigger: a bare superlative ("best products") with genuinely
        // no ranking context — clarify, unless a metric is already established this conversation
        // (§15.1's own parenthetical: "a later 'and the worst ones?' ... inherits the established
        // metric").
        if (IntentPatternMatcher.IsBareSuperlativeNoContext(message))
        {
            if (priorIntent?.Metric is { } inheritedMetric)
            {
                if (!TryResolveDate(message, now, out var range, out var dateClarification))
                {
                    return Clarify(dateClarification!);
                }

                var inheritedDirection = message.Contains("worst", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("declining", StringComparison.OrdinalIgnoreCase)
                    ? RankDirection.Asc
                    : RankDirection.Desc;

                return Patch(BuildRankingPatch(inheritedMetric, 5, inheritedDirection, range, isFollowUp));
            }

            return Clarify(new ClarificationRequest(
                "By 'best,' do you mean by revenue, units sold, or order count?",
                ["Revenue", "Units Sold", "Order Count"]));
        }

        if (IntentPatternMatcher.IsRevenueOrSalesMention(message) || IntentPatternMatcher.IsComparisonQuery(message))
        {
            if (!TryResolveDate(message, now, out var range, out var dateClarification))
            {
                return Clarify(dateClarification!);
            }

            ComparisonSpec? comparison = null;
            if (IntentPatternMatcher.IsComparisonQuery(message))
            {
                var length = range.To - range.From;
                comparison = new ComparisonSpec(new DateRange(range.From - length, range.From));
            }

            return Patch(new ResolvedIntentPatch(
                Metric: MetricType.Revenue,
                Entity: EntityType.Order,
                Aggregation: AggregationType.Sum,
                Dimensions: [DimensionType.Time],
                Filters: new IntentFiltersPatch(DateRange: range),
                Comparison: comparison,
                IsContextReset: isFollowUp));
        }

        // FR-002: an unmappable intent degrades to "unsupported," never a best-effort guess.
        return Patch(new ResolvedIntentPatch(
            Metric: null,
            Entity: null,
            Dimensions: Array.Empty<DimensionType>(),
            IsContextReset: isFollowUp));
    }

    private static ResolvedIntentPatch BuildRankingPatch(MetricType metric, int limit, RankDirection direction, DateRange range, bool isContextReset) =>
        new(
            Metric: metric,
            Entity: EntityType.Product,
            Aggregation: AggregationType.Sum,
            Dimensions: [DimensionType.Product],
            Filters: new IntentFiltersPatch(DateRange: range),
            Ranking: new RankingSpec(metric, direction, limit),
            IsContextReset: isContextReset);

    private static bool TryResolveDate(string message, DateTimeOffset now, out DateRange range, out ClarificationRequest? clarification)
    {
        var resolution = DatePhraseResolver.Resolve(message, now);
        if (resolution.Kind == DateResolutionKind.Unresolvable)
        {
            range = default!;
            clarification = new ClarificationRequest(
                "Could you give me a concrete date range — e.g. \"last 7 days\" or \"this month\"? \"Recently\"/\"lately\" isn't specific enough to query.",
                ["Last 7 days", "This week", "This month"]);
            return false;
        }

        range = resolution.Kind == DateResolutionKind.Resolved ? resolution.Range! : DatePhraseResolver.DefaultRollingWindow(now);
        clarification = null;
        return true;
    }

    private static DateRange ComputeEqualLengthPrecedingWindow(DateRange? priorRange, DateTimeOffset now)
    {
        var range = priorRange ?? DatePhraseResolver.DefaultRollingWindow(now);
        var length = range.To - range.From;
        return new DateRange(range.From - length, range.From);
    }

    private static PlanOutcome Patch(ResolvedIntentPatch patch) => new(patch, null);

    private static PlanOutcome Clarify(ClarificationRequest clarification) => new(null, clarification);

    // ---- Shared helpers ---------------------------------------------------------------------

    private static async Task<long> SimulateLatencyAsync(CancellationToken cancellationToken)
    {
        var delay = Random.Shared.Next((int)MinSimulatedLatency.TotalMilliseconds, (int)MaxSimulatedLatency.TotalMilliseconds);
        await Task.Delay(delay, cancellationToken);
        return delay;
    }

    /// <summary>Rough token estimate proportional to text length — good enough for latency/cost
    /// dashboards to have non-zero, plausible data (this is a mock provider, not a real tokenizer).</summary>
    private static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);
}
