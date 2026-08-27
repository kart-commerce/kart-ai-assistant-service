using Kart.AiAssistant.Application.Common;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;
using Kart.AiAssistant.Domain.Shared;
using Xunit;

namespace Kart.AiAssistant.UnitTests.Common;

/// <summary>FR-002's deterministic intent→endpoint translator.</summary>
public sealed class QueryPlanBuilderTests
{
    private static readonly DateRange SampleRange = new(
        DateTimeOffset.Parse("2026-08-11T00:00:00Z"),
        DateTimeOffset.Parse("2026-08-18T00:00:00Z"));

    [Fact]
    public void Build_RankingIntent_ProducesProductPerformancePlanWithGranularityOmitted()
    {
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Product, AggregationType.Sum,
            [DimensionType.Product],
            new IntentFilters(SampleRange, null, null, null, null),
            new RankingSpec(MetricType.Revenue, RankDirection.Desc, 5),
            null, null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.True(result.IsSuccess);
        Assert.Equal("/internal/v1/dashboards/product-performance", result.Value.Plan.EndpointPath);
        Assert.Equal("5", result.Value.Plan.Parameters["limit"]);
        Assert.Equal("desc", result.Value.Plan.Parameters["direction"]);
        Assert.False(result.Value.Plan.Parameters.ContainsKey("granularity"));
        Assert.False(result.Value.WasRankingLimitClamped);
    }

    [Fact]
    public void Build_RankingLimitAbove100_ClampsAndReportsOriginalLimit()
    {
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Product, AggregationType.Sum,
            [DimensionType.Product],
            new IntentFilters(SampleRange, null, null, null, null),
            new RankingSpec(MetricType.Revenue, RankDirection.Desc, 5000),
            null, null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.True(result.IsSuccess);
        Assert.Equal("100", result.Value.Plan.Parameters["limit"]);
        Assert.True(result.Value.WasRankingLimitClamped);
        Assert.Equal(5000, result.Value.OriginalRequestedLimit);
    }

    [Fact]
    public void Build_RankingLimitBelow1_ClampsToOne()
    {
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Product, AggregationType.Sum,
            [DimensionType.Product],
            new IntentFilters(SampleRange, null, null, null, null),
            new RankingSpec(MetricType.Revenue, RankDirection.Desc, 0),
            null, null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.True(result.IsSuccess);
        Assert.Equal("1", result.Value.Plan.Parameters["limit"]);
    }

    [Fact]
    public void Build_RevenueTimeSeriesIntent_SendsPascalCaseGranularity()
    {
        // Regression test for a real integration bug found during live E2E verification:
        // kart-analytics-service's Granularity query parameter binds via .NET's default
        // (case-sensitive) enum model binder and rejects lowercase "day"/"hour" with a 400 —
        // every existing dashboard caller already sends PascalCase, so this client must too.
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Order, AggregationType.Sum,
            [DimensionType.Time],
            new IntentFilters(SampleRange, null, null, null, null),
            null, null, null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.True(result.IsSuccess);
        Assert.Equal("Day", result.Value.Plan.Parameters["granularity"]);
    }

    [Fact]
    public void Build_HourOfDayDimension_SendsPascalCaseHourGranularity()
    {
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Order, AggregationType.Sum,
            [DimensionType.HourOfDay],
            new IntentFilters(SampleRange, null, null, null, null),
            null, null, null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hour", result.Value.Plan.Parameters["granularity"]);
    }

    [Fact]
    public void Build_ComparisonIntent_PopulatesBaselineComparisonPlan()
    {
        var baseline = new DateRange(
            DateTimeOffset.Parse("2026-08-04T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-11T00:00:00Z"));
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Order, AggregationType.Sum,
            [DimensionType.Time],
            new IntentFilters(SampleRange, null, null, null, null),
            null,
            new ComparisonSpec(baseline),
            null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Plan.BaselineComparisonPlan);
        Assert.Equal(baseline.From.ToString("O"), DateTimeOffset.Parse(result.Value.Plan.BaselineComparisonPlan!.Parameters["from"]).ToString("O"));
    }

    [Fact]
    public void Build_UnregisteredCombination_ReturnsFailure()
    {
        // The shape a geography/seller-shaped question degrades to.
        var intent = new ResolvedIntent(
            null, null, null, [],
            IntentFilters.Empty, null, null, null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void Build_CategoryFilter_IsPassedThroughWhenEndpointSupportsIt()
    {
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Product, AggregationType.Sum,
            [DimensionType.Product],
            new IntentFilters(SampleRange, "Electronics", null, null, null),
            new RankingSpec(MetricType.Revenue, RankDirection.Desc, 5),
            null, null, false);

        var result = QueryPlanBuilder.Build(intent);

        Assert.True(result.IsSuccess);
        Assert.Equal("Electronics", result.Value.Plan.Parameters["category"]);
    }
}
