using Kart.AiAssistant.Application.Common.Registry;
using Kart.AiAssistant.Domain.Enums;
using Xunit;

namespace Kart.AiAssistant.UnitTests.Common.Registry;

/// <summary>FR-002/ADR-0026/ADR-0027: every in-scope query type resolves; Geography/Seller-Vendor
/// have no representable enum value at all, so there is nothing to "reject" — confirming that
/// absence is itself the test.</summary>
public sealed class MetricEndpointRegistryTests
{
    [Theory]
    [InlineData(MetricType.Revenue, EntityType.Product, true, "/internal/v1/dashboards/product-performance")]
    [InlineData(MetricType.UnitsSold, EntityType.Product, true, "/internal/v1/dashboards/product-performance")]
    [InlineData(MetricType.OrderCount, EntityType.Product, true, "/internal/v1/dashboards/product-performance")]
    [InlineData(MetricType.Revenue, EntityType.Order, false, "/internal/v1/dashboards/revenue")]
    [InlineData(MetricType.ConversionRate, EntityType.Funnel, false, "/internal/v1/funnels/order-conversion")]
    [InlineData(MetricType.FulfillmentTime, EntityType.Fulfillment, false, "/internal/v1/dashboards/fulfillment-performance")]
    [InlineData(MetricType.InventoryMovement, EntityType.Inventory, false, "/internal/v1/dashboards/inventory-movement")]
    [InlineData(MetricType.PromotionRedemptionRate, EntityType.Promotion, false, "/internal/v1/dashboards/promotions-effectiveness")]
    [InlineData(MetricType.NotificationDelivery, EntityType.Notification, false, "/internal/v1/dashboards/notification-delivery")]
    [InlineData(MetricType.ReviewRating, EntityType.Review, false, "/internal/v1/dashboards/reviews-ratings")]
    [InlineData(MetricType.AdminActionCount, EntityType.AdminAction, false, "/internal/v1/dashboards/admin-audit")]
    public void TryResolve_InScopeQueryType_ResolvesExpectedEndpoint(MetricType metric, EntityType entity, bool hasRanking, string expectedPath)
    {
        var resolved = MetricEndpointRegistry.TryResolve(metric, entity, hasRanking, out var registration);

        Assert.True(resolved);
        Assert.Equal(expectedPath, registration.EndpointPath);
    }

    [Fact]
    public void TryResolve_UserGrowth_HasNoMetricMember_ButEntityAloneResolves()
    {
        // "user growth" has no MetricType member at all (§8's closed enum) — a null metric with
        // entity=User must still resolve, since the registry's own Accepts() rule treats an empty
        // AllowedMetrics list as "any/no metric is fine."
        var resolved = MetricEndpointRegistry.TryResolve(null, EntityType.User, false, out var registration);

        Assert.True(resolved);
        Assert.Equal("/internal/v1/dashboards/user-growth", registration.EndpointPath);
    }

    [Fact]
    public void TryResolve_NullEntity_DefaultsToRevenueDashboard()
    {
        // A null entity (no entity named at all) falls back to the revenue/order dashboard —
        // a deliberate default for a bare "how much revenue" question, not a failure case.
        var resolved = MetricEndpointRegistry.TryResolve(null, null, false, out var registration);

        Assert.True(resolved);
        Assert.Equal("/internal/v1/dashboards/revenue", registration.EndpointPath);
    }

    [Fact]
    public void TryResolve_MetricNotAcceptedByResolvedEndpoint_Fails()
    {
        // Geography/Seller-Vendor have no EntityType/MetricType member at all (ADR-0026/ADR-0027)
        // so they can never reach this switch — the real failure mode this registry can express is
        // a metric the resolved entity's endpoint doesn't accept, e.g. a funnel-only metric against
        // a non-funnel entity.
        var resolved = MetricEndpointRegistry.TryResolve(MetricType.ConversionRate, EntityType.Inventory, false, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_RevenueEntityProduct_RankingFalse_DoesNotResolveToProductPerformance()
    {
        // Same metric/entity pair as the ranking case, but with no RankingSpec present — this is
        // a plain (unsupported today, per §5) "revenue for a specific product" lookup, not a
        // ranking, and must not silently reuse the ranking endpoint's registration.
        var resolved = MetricEndpointRegistry.TryResolve(MetricType.Revenue, EntityType.Product, false, out var registration);

        if (resolved)
        {
            Assert.False(registration.IsNewProductPerformanceEndpoint);
        }
    }
}
