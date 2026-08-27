using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Application.Common;

/// <summary>Shape facts about a fetched result that §13.1's rule table switches on — deliberately
/// separate from the raw JSON itself so <see cref="VisualizationSelector"/> never has to parse a
/// payload shape it doesn't own (that parsing already happened once, for table assembly).</summary>
public sealed record ResultShape(
    int RowCount,
    bool IsRanking,
    bool IsTimeSeries,
    bool IsLogRows,
    bool IsDistribution,
    bool IsFunnel,
    bool IsComparison,
    bool IsShareOfTotal);

/// <summary>
/// FR-006/§13.1's deterministic visualization-selection rule table — never open-ended LLM
/// discretion. The LLM's advisory <see cref="ResolvedIntent.VisualizationHint"/> (§9) is honored
/// only when <paramref name="userExplicitlyRequestedChartType"/> is true, i.e. the *current* turn's
/// planner patch set a fresh hint (the user's own words this turn named a chart type) rather than
/// one merely carried forward from an earlier turn (edge-cases.md's "LLM visualizationHint
/// Conflicts With the Deterministic Rule Table" — confirmed non-event, resolved exactly as §13.1
/// already specifies).
/// </summary>
public static class VisualizationSelector
{
    public static VisualizationType? Select(ResolvedIntent intent, ResultShape shape, bool userExplicitlyRequestedChartType)
    {
        if (userExplicitlyRequestedChartType && intent.VisualizationHint is { } hint)
        {
            return hint;
        }

        return ApplyDeterministicRule(shape);
    }

    private static VisualizationType ApplyDeterministicRule(ResultShape shape)
    {
        if (shape.RowCount == 0)
        {
            // §19 "Visualization failure" posture, applied proactively: nothing to chart, the
            // table (always present, FR-005) already communicates the empty result.
            return VisualizationType.TableOnly;
        }

        if (shape.IsLogRows)
        {
            return VisualizationType.TableOnly;
        }

        if (shape.IsFunnel)
        {
            return VisualizationType.FunnelChart;
        }

        if (shape.IsRanking)
        {
            return VisualizationType.HorizontalBarChart;
        }

        if (shape.IsComparison)
        {
            // Closest enum value to "grouped bar chart or single stat with a delta badge" (§13.1)
            // — the response's `answer`/metadata still carries the delta explicitly.
            return VisualizationType.BarChart;
        }

        if (shape.IsShareOfTotal)
        {
            return VisualizationType.DonutChart;
        }

        if (shape.IsDistribution)
        {
            return VisualizationType.BarChart;
        }

        if (shape.IsTimeSeries && shape.RowCount >= 2)
        {
            return VisualizationType.LineChart;
        }

        if (shape.RowCount == 1)
        {
            return VisualizationType.SingleStat;
        }

        // Category/channel/dimension breakdown, non-ranked, non-time-series, multiple rows.
        return VisualizationType.BarChart;
    }
}
