using Kart.AiAssistant.Application.Common;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;
using Xunit;

namespace Kart.AiAssistant.UnitTests.Common;

/// <summary>FR-006/§13.1's deterministic visualization rule table.</summary>
public sealed class VisualizationSelectorTests
{
    private static readonly ResolvedIntent PlainIntent = ResolvedIntent.Empty;

    [Fact]
    public void Select_Ranking_ReturnsHorizontalBarChart()
    {
        var shape = new ResultShape(RowCount: 5, IsRanking: true, IsTimeSeries: false, IsLogRows: false, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(PlainIntent, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.HorizontalBarChart, result);
    }

    [Fact]
    public void Select_TimeSeriesTwoOrMoreBuckets_ReturnsLineChart()
    {
        var shape = new ResultShape(RowCount: 7, IsRanking: false, IsTimeSeries: true, IsLogRows: false, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(PlainIntent, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.LineChart, result);
    }

    [Fact]
    public void Select_SingleRow_ReturnsSingleStat()
    {
        var shape = new ResultShape(RowCount: 1, IsRanking: false, IsTimeSeries: false, IsLogRows: false, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(PlainIntent, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.SingleStat, result);
    }

    [Fact]
    public void Select_Funnel_ReturnsFunnelChart()
    {
        var shape = new ResultShape(RowCount: 5, IsRanking: false, IsTimeSeries: false, IsLogRows: false, IsDistribution: false, IsFunnel: true, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(PlainIntent, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.FunnelChart, result);
    }

    [Fact]
    public void Select_LogRows_ReturnsTableOnly()
    {
        var shape = new ResultShape(RowCount: 20, IsRanking: false, IsTimeSeries: false, IsLogRows: true, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(PlainIntent, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.TableOnly, result);
    }

    [Fact]
    public void Select_ZeroRows_ReturnsTableOnly()
    {
        var shape = new ResultShape(RowCount: 0, IsRanking: true, IsTimeSeries: false, IsLogRows: false, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(PlainIntent, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.TableOnly, result);
    }

    [Fact]
    public void Select_ShareOfTotal_ReturnsDonutChart()
    {
        var shape = new ResultShape(RowCount: 4, IsRanking: false, IsTimeSeries: false, IsLogRows: false, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: true);

        var result = VisualizationSelector.Select(PlainIntent, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.DonutChart, result);
    }

    [Fact]
    public void Select_HintPresentButNotExplicitlyRequestedThisTurn_IgnoresHint()
    {
        // FR-006: a carried-forward hint from an earlier turn must never override the
        // deterministic rule — only a *fresh*, this-turn explicit chart-type request may.
        var intentWithStaleHint = PlainIntent with { VisualizationHint = VisualizationType.DonutChart };
        var shape = new ResultShape(RowCount: 5, IsRanking: true, IsTimeSeries: false, IsLogRows: false, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(intentWithStaleHint, shape, userExplicitlyRequestedChartType: false);

        Assert.Equal(VisualizationType.HorizontalBarChart, result);
    }

    [Fact]
    public void Select_HintExplicitlyRequestedThisTurn_HonorsHintOverRule()
    {
        var intentWithFreshHint = PlainIntent with { VisualizationHint = VisualizationType.LineChart };
        var shape = new ResultShape(RowCount: 5, IsRanking: true, IsTimeSeries: false, IsLogRows: false, IsDistribution: false, IsFunnel: false, IsComparison: false, IsShareOfTotal: false);

        var result = VisualizationSelector.Select(intentWithFreshHint, shape, userExplicitlyRequestedChartType: true);

        Assert.Equal(VisualizationType.LineChart, result);
    }
}
