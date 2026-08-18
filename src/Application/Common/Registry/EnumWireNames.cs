using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Application.Common.Registry;

/// <summary>
/// The single place that converts the closed §6/§8 enums to/from the exact snake_case wire
/// strings `kart-analytics-service`'s query parameters and `genai-business-assistant-spec.md`'s
/// intent JSON both use (e.g. `MetricType.UnitsSold` &lt;-&gt; `"units_sold"`). Kept out of
/// <see cref="MetricEndpointRegistry"/>/<see cref="Application.Common.QueryPlanBuilder"/> so both
/// (and any future caller, e.g. audit serialization) share exactly one mapping table rather than
/// each re-deriving its own snake_case rule and silently drifting apart.
/// </summary>
public static class EnumWireNames
{
    public static string ToWire(this MetricType metric) => metric switch
    {
        MetricType.Revenue => "revenue",
        MetricType.UnitsSold => "units_sold",
        MetricType.OrderCount => "order_count",
        MetricType.Aov => "aov",
        MetricType.ConversionRate => "conversion_rate",
        MetricType.FulfillmentTime => "fulfillment_time",
        MetricType.InventoryMovement => "inventory_movement",
        MetricType.PromotionRedemptionRate => "promotion_redemption_rate",
        MetricType.ReviewRating => "review_rating",
        MetricType.NotificationDelivery => "notification_delivery",
        MetricType.AdminActionCount => "admin_action_count",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unregistered MetricType — closed enum, this switch must stay exhaustive."),
    };

    public static string ToWire(this EntityType entity) => entity switch
    {
        EntityType.Product => "product",
        EntityType.Category => "category",
        EntityType.Order => "order",
        EntityType.Funnel => "funnel",
        EntityType.Fulfillment => "fulfillment",
        EntityType.Inventory => "inventory",
        EntityType.Promotion => "promotion",
        EntityType.User => "user",
        EntityType.Review => "review",
        EntityType.Notification => "notification",
        EntityType.AdminAction => "admin_action",
        _ => throw new ArgumentOutOfRangeException(nameof(entity), entity, "Unregistered EntityType — closed enum, this switch must stay exhaustive."),
    };

    public static string ToWire(this RankDirection direction) => direction switch
    {
        RankDirection.Desc => "desc",
        RankDirection.Asc => "asc",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    public static string ToWire(this VisualizationType type) => type switch
    {
        VisualizationType.BarChart => "bar_chart",
        VisualizationType.HorizontalBarChart => "horizontal_bar_chart",
        VisualizationType.LineChart => "line_chart",
        VisualizationType.DonutChart => "donut_chart",
        VisualizationType.FunnelChart => "funnel_chart",
        VisualizationType.SingleStat => "single_stat",
        VisualizationType.TableOnly => "table_only",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string ToWire(this AggregationType aggregation) => aggregation switch
    {
        AggregationType.Sum => "sum",
        AggregationType.Count => "count",
        AggregationType.Avg => "avg",
        AggregationType.P50 => "p50",
        AggregationType.P95 => "p95",
        AggregationType.P99 => "p99",
        AggregationType.Rate => "rate",
        _ => throw new ArgumentOutOfRangeException(nameof(aggregation), aggregation, null),
    };

    public static string ToWire(this DimensionType dimension) => dimension switch
    {
        DimensionType.Time => "time",
        DimensionType.Product => "product",
        DimensionType.Category => "category",
        DimensionType.Channel => "channel",
        DimensionType.HourOfDay => "hour_of_day",
        DimensionType.FunnelStage => "funnel_stage",
        DimensionType.StarRating => "star_rating",
        DimensionType.ActionType => "action_type",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, null),
    };

    /// <summary>Full wire-shaped serialization of a resolved intent (§8's schema) — every enum
    /// field goes through this file's own snake_case mapping rather than the generic
    /// System.Text.Json camelCase enum converter, so a multi-word value (e.g. `UnitsSold`,
    /// `HourOfDay`) round-trips as `units_sold`/`hour_of_day`, not `unitsSold`/`hourOfDay`.</summary>
    public static object ToWireObject(this ResolvedIntent intent) => new
    {
        metric = intent.Metric?.ToWire(),
        entity = intent.Entity?.ToWire(),
        aggregation = intent.Aggregation?.ToWire(),
        dimensions = intent.Dimensions.Select(d => d.ToWire()).ToArray(),
        filters = new
        {
            dateRange = intent.Filters.DateRange is { } range ? new { from = range.From, to = range.To } : null,
            category = intent.Filters.Category,
            sku = intent.Filters.Sku,
            channel = intent.Filters.Channel,
            actionType = intent.Filters.ActionType,
        },
        ranking = intent.Ranking is { } ranking
            ? new { sortBy = ranking.SortBy.ToWire(), direction = ranking.Direction.ToWire(), limit = ranking.Limit }
            : null,
        comparison = intent.Comparison is { } comparison
            ? new { baselineDateRange = new { from = comparison.BaselineDateRange.From, to = comparison.BaselineDateRange.To } }
            : null,
        visualizationHint = intent.VisualizationHint?.ToWire(),
        clarificationNeeded = intent.ClarificationNeeded,
    };
}
