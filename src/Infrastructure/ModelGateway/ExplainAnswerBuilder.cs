using System.Text.Json;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;
using Kart.AiAssistant.Domain.Shared;

namespace Kart.AiAssistant.Infrastructure.ModelGateway;

/// <summary>
/// Builds the "explain" call's answer text — genuinely grounded (FR-004): every number this
/// produces is read directly out of <c>RawResultJson</c>, never invented, so the Application
/// layer's own numeric-grounding check (§16) passes by construction. Kept separate from
/// <see cref="MockModelProvider"/> for the same file-size reason <see cref="IntentPatternMatcher"/>
/// is its own file.
/// </summary>
public static class ExplainAnswerBuilder
{
    private static readonly string[] EnvelopeOnlyKeys = ["isProvisional", "reconciledThrough", "generatedAt"];

    public static string Build(ResolvedIntent intent, string rawResultJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawResultJson) ? "{}" : rawResultJson);
        }
        catch (JsonException)
        {
            return "The result could not be summarized from the data returned this turn.";
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "The result could not be summarized from the data returned this turn.";
            }

            var isProvisional = TryGetBool(root, "isProvisional") ?? false;
            var reconciledThrough = TryGetString(root, "reconciledThrough");

            var body = intent switch
            {
                { Ranking: not null } => BuildRankingSentence(intent, root),
                { Comparison: not null } => BuildComparisonSentence(intent, root),
                _ when intent.Dimensions.Contains(DimensionType.HourOfDay) => BuildPeakHourSentence(intent, root),
                _ => BuildGenericSentence(intent, root),
            };

            if (isProvisional)
            {
                body += reconciledThrough is not null
                    ? $" Note: today's figures are still provisional and will finalize after reconciliation (last reconciled through {reconciledThrough})."
                    : " Note: today's figures are still provisional and will finalize after tonight's reconciliation.";
            }

            return body;
        }
    }

    private static string BuildRankingSentence(ResolvedIntent intent, JsonElement root)
    {
        var window = FormatWindow(intent.Filters.DateRange);
        var entity = Pluralize(DisplayName(intent.Entity));
        var metric = DisplayName(intent.Metric);

        if (!TryFindObjectArray(root, out var array))
        {
            return $"Here are the top results by {metric} over {window}.";
        }

        var count = array.GetArrayLength();
        if (count == 0)
        {
            return $"No {entity} had qualifying data for {metric} over {window}.";
        }

        var sentence = $"Here are the top {count} {entity} by {metric} over {window}.";

        var first = array[0];
        var topName = TryGetString(first, "sku") ?? TryGetString(first, "product") ?? TryGetString(first, "name") ?? TryGetString(first, "category");
        var topValue = TryGetMetricValue(first, intent.Metric);
        if (topName is not null && topValue is not null)
        {
            sentence += $" {topName} leads at {FormatMetricValue(intent.Metric, topValue.Value)}.";
        }

        if (intent.Ranking is { } ranking && count < ranking.Limit)
        {
            sentence += $" Fewer than the requested {ranking.Limit} were found for this window.";
        }

        return sentence;
    }

    private static string BuildComparisonSentence(ResolvedIntent intent, JsonElement root)
    {
        var window = FormatWindow(intent.Filters.DateRange);
        var metricName = DisplayName(intent.Metric);

        var currentValue = TryGetNestedMetricValue(root, "current", intent.Metric);
        var baselineValue = TryGetNestedMetricValue(root, "baseline", intent.Metric);

        if (currentValue is null || baselineValue is null)
        {
            return BuildGenericSentence(intent, root);
        }

        var pctChange = TryGetNumber(root, "pctChange");
        var currentText = FormatMetricValue(intent.Metric, currentValue.Value);
        var baselineText = FormatMetricValue(intent.Metric, baselineValue.Value);

        if (pctChange is null)
        {
            return $"{metricName} over {window} is {currentText}, compared with {baselineText} for the prior period.";
        }

        var direction = pctChange.Value >= 0 ? "up" : "down";
        return $"{metricName} over {window} is {currentText}, {direction} {Math.Abs(pctChange.Value):0.#}% from {baselineText} the prior period.";
    }

    private static string BuildPeakHourSentence(ResolvedIntent intent, JsonElement root)
    {
        var window = FormatWindow(intent.Filters.DateRange);

        if (!TryFindObjectArray(root, out var array) || array.GetArrayLength() == 0)
        {
            return $"Here is the hour-of-day breakdown for {DisplayName(intent.Metric)} over {window}.";
        }

        string? peakHour = null;
        decimal? peakValue = null;
        foreach (var element in array.EnumerateArray())
        {
            var value = TryGetMetricValue(element, intent.Metric) ?? TryGetNumber(element, "value");
            if (value is null)
            {
                continue;
            }

            if (peakValue is null || value.Value > peakValue.Value)
            {
                peakValue = value.Value;
                peakHour = TryGetString(element, "hour") ?? TryGetString(element, "hourOfDay") ?? TryGetString(element, "bucketStart");
            }
        }

        if (peakHour is null || peakValue is null)
        {
            return $"Here is the hour-of-day breakdown for {DisplayName(intent.Metric)} over {window}.";
        }

        return $"Sales peak around {peakHour}, averaging {FormatMetricValue(intent.Metric, peakValue.Value)} over {window}.";
    }

    private static string BuildGenericSentence(ResolvedIntent intent, JsonElement root)
    {
        var window = FormatWindow(intent.Filters.DateRange);
        var metricName = DisplayName(intent.Metric);

        var value = TryGetMetricValue(root, intent.Metric) ?? FindFirstNumber(root);
        if (value is not null)
        {
            return $"{metricName} over {window}: {FormatMetricValue(intent.Metric, value.Value)}.";
        }

        if (TryFindObjectArray(root, out var array))
        {
            return $"Here is the {metricName} result over {window} ({array.GetArrayLength()} data point(s)).";
        }

        return $"Here is the {metricName} result over {window}.";
    }

    private static decimal? TryGetNestedMetricValue(JsonElement root, string parentProperty, MetricType? metric)
    {
        if (!root.TryGetProperty(parentProperty, out var parent) || parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetMetricValue(parent, metric);
    }

    private static decimal? TryGetMetricValue(JsonElement element, MetricType? metric)
    {
        var candidateKeys = metric switch
        {
            MetricType.Revenue or MetricType.Aov => new[] { "revenue", "amount", "aov" },
            MetricType.UnitsSold => new[] { "unitsSold", "units" },
            MetricType.OrderCount => new[] { "orderCount", "orders" },
            MetricType.ConversionRate => new[] { "conversionRate", "rate" },
            MetricType.InventoryMovement => new[] { "count", "reserved", "released", "replenished" },
            MetricType.PromotionRedemptionRate => new[] { "redemptionRate", "rate" },
            MetricType.ReviewRating => new[] { "rating", "averageRating" },
            _ => new[] { "value", "amount", "count" },
        };

        foreach (var key in candidateKeys)
        {
            var number = TryGetNumber(element, key);
            if (number is not null)
            {
                return number;
            }
        }

        return null;
    }

    private static decimal? TryGetNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.Object when value.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number => amount.GetDecimal(),
            _ => null,
        };
    }

    private static decimal? FindFirstNumber(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (EnvelopeOnlyKeys.Contains(property.Name))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Number)
                {
                    return property.Value.GetDecimal();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (EnvelopeOnlyKeys.Contains(property.Name))
                {
                    continue;
                }

                var nested = FindFirstNumber(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindFirstNumber(item);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool TryFindObjectArray(JsonElement element, out JsonElement array)
    {
        if (element.ValueKind == JsonValueKind.Array && (element.GetArrayLength() == 0 || element[0].ValueKind == JsonValueKind.Object))
        {
            array = element;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (TryFindObjectArray(property.Value, out array))
                {
                    return true;
                }
            }
        }

        array = default;
        return false;
    }

    private static bool? TryGetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FormatWindow(DateRange? range) =>
        range is null ? "the requested period" : $"{range.From:MMM d}–{range.To:MMM d}";

    private static string Pluralize(string word) => word.EndsWith('s') ? word : word + "s";

    private static string DisplayName(MetricType? metric) => metric switch
    {
        MetricType.Revenue => "revenue",
        MetricType.UnitsSold => "units sold",
        MetricType.OrderCount => "order count",
        MetricType.Aov => "average order value",
        MetricType.ConversionRate => "conversion rate",
        MetricType.FulfillmentTime => "fulfillment time",
        MetricType.InventoryMovement => "inventory movement",
        MetricType.PromotionRedemptionRate => "promotion redemption rate",
        MetricType.ReviewRating => "review rating",
        MetricType.NotificationDelivery => "notification delivery",
        MetricType.AdminActionCount => "admin action count",
        _ => "the requested metric",
    };

    private static string DisplayName(EntityType? entity) => entity switch
    {
        EntityType.Product => "product",
        EntityType.Category => "category",
        EntityType.Order => "order",
        EntityType.Funnel => "funnel stage",
        EntityType.Fulfillment => "fulfillment",
        EntityType.Inventory => "inventory record",
        EntityType.Promotion => "promotion",
        EntityType.User => "user",
        EntityType.Review => "review",
        EntityType.Notification => "notification",
        EntityType.AdminAction => "admin action",
        _ => "result",
    };

    private static string FormatMetricValue(MetricType? metric, decimal value) => metric switch
    {
        MetricType.Revenue or MetricType.Aov => $"${value:N2}",
        MetricType.UnitsSold or MetricType.OrderCount or MetricType.AdminActionCount => $"{value:N0}",
        MetricType.ConversionRate or MetricType.PromotionRedemptionRate => $"{value:N1}%",
        _ => $"{value:N2}",
    };
}
