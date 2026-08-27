namespace Kart.AiAssistant.Domain.Enums;

/// <summary>
/// Closed metric registry (genai-business-assistant-spec.md §6). The LLM selects among these —
/// it never invents its own metric name (structured-output constraint enforced at the
/// Infrastructure model-gateway boundary, not here; this enum is what makes an unregistered
/// value structurally unrepresentable once parsed).
/// </summary>
public enum MetricType
{
    Revenue,
    UnitsSold,
    OrderCount,
    Aov,
    ConversionRate,
    FulfillmentTime,
    InventoryMovement,
    PromotionRedemptionRate,
    ReviewRating,
    NotificationDelivery,
    AdminActionCount,
}
