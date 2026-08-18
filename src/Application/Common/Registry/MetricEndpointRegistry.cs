using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Application.Common.Registry;

/// <summary>
/// One resolved registry entry (FR-002's "versioned application config") — everything
/// <see cref="Application.Common.QueryPlanBuilder"/> needs to know about a single
/// `kart-analytics-service` endpoint: its path, which of the §7 filters it accepts, whether it
/// supports the §10.3 ranking parameters, and (per edge-cases.md's "Schema-Valid Intent Requests a
/// Rank/Limit Analytics Cannot Fulfill") the documented maximum `limit` it will serve.
/// </summary>
public sealed record EndpointRegistration(
    string EndpointPath,
    bool IsNewProductPerformanceEndpoint,
    IReadOnlyList<MetricType> AllowedMetrics,
    bool SupportsCategory,
    bool SupportsSku,
    bool SupportsChannel,
    bool SupportsActionType,
    bool SupportsGranularity,
    bool SupportsRanking,
    int? MaxRankingLimit)
{
    /// <summary>Empty <see cref="AllowedMetrics"/> means "this endpoint has no dedicated metric
    /// field to validate against" (e.g. `user-growth` — §8's closed metric enum has no
    /// "user growth" member at all, so any/no metric is accepted for this entity).</summary>
    public bool Accepts(MetricType? metric) => AllowedMetrics.Count == 0 || metric is null || AllowedMetrics.Contains(metric.Value);
}

/// <summary>
/// The deterministic, versioned intent→endpoint mapping table (FR-002, source spec §10.3/§5) —
/// the *only* place that says which of the eleven registered `kart-analytics-service` endpoints
/// (ten existing + the new `product-performance` capability) a given metric/entity/ranking
/// combination resolves to. Never LLM-authored (§9): the LLM selects a metric/entity from the
/// closed §6/§8 enums, this static table decides whether that selection maps to a real capability.
///
/// <para>
/// Geography and Seller/Vendor deliberately have **no entry, and no <see cref="EntityType"/>/
/// <see cref="MetricType"/> value exists for either anywhere in the closed enums this table
/// switches over (ADR-0026, ADR-0027)** — there is structurally no code path that could ever
/// resolve them. A lookup for either is not a special case here; it is simply a miss like any
/// other unregistered combination, which <see cref="TryResolve"/> reports as "not found" for the
/// caller to turn into FR-009's "unsupported" response.
/// </para>
/// </summary>
public static class MetricEndpointRegistry
{
    // Query type #1 (§5) — Ranking (top/bottom-N by product). New capability (§10.3).
    private static readonly EndpointRegistration ProductPerformance = new(
        EndpointPath: "/internal/v1/dashboards/product-performance",
        IsNewProductPerformanceEndpoint: true,
        AllowedMetrics: [MetricType.Revenue, MetricType.UnitsSold, MetricType.OrderCount],
        SupportsCategory: true,
        SupportsSku: false,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: false,
        SupportsRanking: true,
        MaxRankingLimit: 100);

    // Query types #2/#3/#4/#5/#16 (§5) — revenue/sales aggregation, trend, period-over-period,
    // category analysis, peak-hour-of-day — all served by the existing revenue_dashboard.
    private static readonly EndpointRegistration Revenue = new(
        EndpointPath: "/internal/v1/dashboards/revenue",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.Revenue, MetricType.OrderCount, MetricType.Aov],
        SupportsCategory: true,
        SupportsSku: true,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #6 (§5) — Conversion funnel / drop-off analysis.
    private static readonly EndpointRegistration OrderConversionFunnel = new(
        EndpointPath: "/internal/v1/funnels/order-conversion",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.ConversionRate],
        SupportsCategory: false,
        SupportsSku: false,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #7 (§5) — Fulfillment performance (time-to-ship/deliver).
    private static readonly EndpointRegistration FulfillmentPerformance = new(
        EndpointPath: "/internal/v1/dashboards/fulfillment-performance",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.FulfillmentTime],
        SupportsCategory: false,
        SupportsSku: false,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #8 (§5) — Inventory movement (reserved/released/replenished/failed).
    private static readonly EndpointRegistration InventoryMovement = new(
        EndpointPath: "/internal/v1/dashboards/inventory-movement",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.InventoryMovement],
        SupportsCategory: false,
        SupportsSku: true,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #9 (§5) — Promotions/coupon effectiveness.
    private static readonly EndpointRegistration PromotionsEffectiveness = new(
        EndpointPath: "/internal/v1/dashboards/promotions-effectiveness",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.PromotionRedemptionRate],
        SupportsCategory: false,
        SupportsSku: false,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #10 (§5) — User growth & engagement. §8's closed metric enum has no dedicated
    // "user growth" member (a documented gap in the source schema, not one this table papers
    // over) — AllowedMetrics is empty, meaning "no metric field to validate," matched on Entity alone.
    private static readonly EndpointRegistration UserGrowth = new(
        EndpointPath: "/internal/v1/dashboards/user-growth",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [],
        SupportsCategory: false,
        SupportsSku: false,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #11 (§5) — Reviews & ratings distribution.
    private static readonly EndpointRegistration ReviewsRatings = new(
        EndpointPath: "/internal/v1/dashboards/reviews-ratings",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.ReviewRating],
        SupportsCategory: false,
        SupportsSku: false,
        SupportsChannel: false,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #12 (§5) — Notification delivery.
    private static readonly EndpointRegistration NotificationDelivery = new(
        EndpointPath: "/internal/v1/dashboards/notification-delivery",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.NotificationDelivery],
        SupportsCategory: false,
        SupportsSku: false,
        SupportsChannel: true,
        SupportsActionType: false,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    // Query type #13 (§5) — Admin audit trail (log rows — table_only, §13.1).
    private static readonly EndpointRegistration AdminAudit = new(
        EndpointPath: "/internal/v1/dashboards/admin-audit",
        IsNewProductPerformanceEndpoint: false,
        AllowedMetrics: [MetricType.AdminActionCount],
        SupportsCategory: false,
        SupportsSku: false,
        SupportsChannel: false,
        SupportsActionType: true,
        SupportsGranularity: true,
        SupportsRanking: false,
        MaxRankingLimit: null);

    /// <summary>
    /// Resolves a (metric, entity, ranking-requested) combination to exactly one registered
    /// endpoint, or reports a miss. <paramref name="hasRanking"/> is what disambiguates
    /// "top 5 products by revenue" (→ the new product-performance endpoint) from "revenue for SKU
    /// X" (→ the existing revenue_dashboard's own `sku` filter) — both are `metric=Revenue,
    /// entity=Product`, differing only in whether a <c>RankingSpec</c> is present on the intent.
    /// Deliberately takes <paramref name="metric"/> as nullable: several query types (§5 item 10,
    /// "user growth") have no corresponding <see cref="MetricType"/> member at all, so requiring a
    /// non-null metric here would make an entire in-scope, "Existing"-status query type
    /// unreachable — a stricter signature would misrepresent a schema gap as a design constraint.
    /// </summary>
    public static bool TryResolve(MetricType? metric, EntityType? entity, bool hasRanking, out EndpointRegistration registration)
    {
        registration = null!;

        if (hasRanking && entity == EntityType.Product)
        {
            if (!ProductPerformance.Accepts(metric))
            {
                return false;
            }

            registration = ProductPerformance;
            return true;
        }

        // Geography/Seller-Vendor have no EntityType member — they can never reach this switch,
        // so they fall through to the final "no match" return, exactly as ADR-0026/ADR-0027 require.
        var candidate = entity switch
        {
            EntityType.Product or EntityType.Category or EntityType.Order or null => Revenue,
            EntityType.Funnel => OrderConversionFunnel,
            EntityType.Fulfillment => FulfillmentPerformance,
            EntityType.Inventory => InventoryMovement,
            EntityType.Promotion => PromotionsEffectiveness,
            EntityType.User => UserGrowth,
            EntityType.Review => ReviewsRatings,
            EntityType.Notification => NotificationDelivery,
            EntityType.AdminAction => AdminAudit,
            _ => null,
        };

        if (candidate is null || !candidate.Accepts(metric))
        {
            return false;
        }

        registration = candidate;
        return true;
    }
}
