using System.Text.Json;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Application.Features.SubmitQuery;

/// <summary>
/// FR-005: maps the raw Analytics JSON (already validated as the single source of truth, FR-003)
/// onto §13.2's <c>data.columns</c>/<c>data.rows</c> table shape. Implements the two worked shapes
/// the spec's own examples require byte-for-byte (product-performance ranking, revenue/time-series)
/// plus a generic flattening fallback for the other registered query types, per the same
/// "don't hand-build every one of the eleven shapes" scoping <see cref="Common.GroundingValidator"/>'s
/// template fallback already follows.
/// </summary>
public static class ResultTableAssembler
{
    public static AssistantResultTable Assemble(ResolvedIntent intent, string? rawResultJson)
    {
        if (string.IsNullOrWhiteSpace(rawResultJson))
        {
            return new AssistantResultTable([], []);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(rawResultJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new AssistantResultTable([], []);
        }

        if (TryGetArray(root, "products", out var products) || TryGetArray(root, "results", out products))
        {
            return AssembleRankingTable(products);
        }

        if (TryGetArray(root, "series", out var series))
        {
            return AssembleSeriesTable(series);
        }

        return AssembleGenericTable(root);
    }

    /// <summary>The product-performance ranking shape (§13.2's worked example):
    /// rank | sku | category | revenue | unitsSold | orderCount.</summary>
    private static AssistantResultTable AssembleRankingTable(JsonElement products)
    {
        string[] columns = ["rank", "product", "sku", "category", "revenue", "unitsSold", "orderCount"];
        var rows = new List<IReadOnlyList<object?>>();
        var rank = 1;

        foreach (var product in products.EnumerateArray())
        {
            rows.Add(new List<object?>
            {
                rank++,
                GetString(product, "name") ?? GetString(product, "sku"),
                GetString(product, "sku"),
                GetString(product, "category"),
                GetMoneyOrNumber(product, "revenue"),
                GetNumber(product, "unitsSold"),
                GetNumber(product, "orderCount"),
            });
        }

        return new AssistantResultTable(columns, rows);
    }

    /// <summary>The revenue/time-series shape: one row per bucket —
    /// bucketStart | revenue | orderCount (only the fields actually present are emitted).</summary>
    private static AssistantResultTable AssembleSeriesTable(JsonElement series)
    {
        var columnSet = new List<string>();
        var rows = new List<IReadOnlyList<object?>>();

        foreach (var bucket in series.EnumerateArray())
        {
            if (bucket.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (columnSet.Count == 0)
            {
                foreach (var property in bucket.EnumerateObject())
                {
                    columnSet.Add(property.Name);
                }
            }

            var row = new List<object?>(columnSet.Count);
            foreach (var column in columnSet)
            {
                row.Add(ToScalar(bucket, column));
            }

            rows.Add(row);
        }

        return new AssistantResultTable(columnSet, rows);
    }

    /// <summary>Generic fallback for the remaining registered query types (funnel, fulfillment,
    /// inventory, promotions, user-growth, reviews, notification-delivery, admin-audit): a
    /// single-row table of whatever scalar top-level fields the result carries, or a flattened
    /// array-of-objects table when the root itself is an array of log-style rows (admin-audit).</summary>
    private static AssistantResultTable AssembleGenericTable(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return AssembleArrayOfObjectsTable(root);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new AssistantResultTable([], []);
        }

        var columns = new List<string>();
        var values = new List<object?>();

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            columns.Add(property.Name);
            values.Add(ToScalar(root, property.Name));
        }

        return columns.Count == 0
            ? new AssistantResultTable([], [])
            : new AssistantResultTable(columns, [values]);
    }

    private static AssistantResultTable AssembleArrayOfObjectsTable(JsonElement array)
    {
        var columnSet = new List<string>();
        var rows = new List<IReadOnlyList<object?>>();

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (columnSet.Count == 0)
            {
                foreach (var property in item.EnumerateObject())
                {
                    columnSet.Add(property.Name);
                }
            }

            var row = new List<object?>(columnSet.Count);
            foreach (var column in columnSet)
            {
                row.Add(ToScalar(item, column));
            }

            rows.Add(row);
        }

        return new AssistantResultTable(columnSet, rows);
    }

    private static bool TryGetArray(JsonElement root, string propertyName, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var candidate) &&
            candidate.ValueKind == JsonValueKind.Array)
        {
            array = candidate;
            return true;
        }

        array = default;
        return false;
    }

    private static object? ToScalar(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out var d) ? d : null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object when value.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number =>
                amount.TryGetDecimal(out var amt) ? amt : null,
            _ => null,
        };
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetNumber(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d)
            ? d
            : null;

    private static decimal? GetMoneyOrNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var direct))
        {
            return direct;
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out var amt))
        {
            return amt;
        }

        return null;
    }
}
