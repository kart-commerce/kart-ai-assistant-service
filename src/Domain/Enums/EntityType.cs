namespace Kart.AiAssistant.Domain.Enums;

/// <summary>Closed entity registry (genai-business-assistant-spec.md §8's intent schema).</summary>
public enum EntityType
{
    Product,
    Category,
    Order,
    Funnel,
    Fulfillment,
    Inventory,
    Promotion,
    User,
    Review,
    Notification,
    AdminAction,
}
