using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Application.Common;

/// <summary>Result of one grounding check (FR-004, §16) — <see cref="Passed"/> is false iff at
/// least one numeric token in the generated explanation could not be traced to a value in the raw
/// Analytics result.</summary>
public sealed record GroundingCheckOutcome(bool Passed, IReadOnlyList<string> UngroundedTokens);

/// <summary>
/// FR-004/§16's post-generation numeric-grounding check: every numeric token in the LLM's
/// generated explanation must trace to a value actually present in the raw Analytics result JSON
/// fetched the same turn — the mechanism that keeps the model from ever "explaining" a number it
/// invented. Implements edge-cases.md's "Grounding-Check Failure, False Positive, and False
/// Negative" decision: both sides of the comparison are normalized (currency symbols, thousands
/// separators, percentage framing, rounding) before matching, specifically so a correct answer
/// formatted differently than the raw JSON ("$48,230" vs. `48230.00`) is not wrongly rejected.
/// </summary>
public static class GroundingValidator
{
    private const decimal AbsoluteTolerance = 0.01m;

    // Matches an optionally $-prefixed, optionally comma-grouped, optionally decimal, optionally
    // %-suffixed number, with an optional leading sign (e.g. "-3.5%", "$48,230", "210", "12.5").
    private static readonly Regex NumberTokenRegex = new(
        @"(?<sign>[-+])?(?<currency>\$)?(?<num>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?<percent>%)?",
        RegexOptions.Compiled);

    public static GroundingCheckOutcome Validate(string answerText, string? rawResultJson)
    {
        var jsonNumbers = ExtractJsonNumbers(rawResultJson);
        var ungrounded = new List<string>();

        foreach (Match match in NumberTokenRegex.Matches(answerText ?? string.Empty))
        {
            var raw = match.Value;
            var numGroup = match.Groups["num"].Value;
            var isCurrency = match.Groups["currency"].Success;
            var isPercent = match.Groups["percent"].Success;
            var isNegative = match.Groups["sign"].Value == "-";

            // FR-004 only cares about genuine business figures — bare 1-2 digit numbers with no
            // symbol (e.g. the "11"/"18" inside a rendered "Aug 11–18" date range, or "top 5") are
            // deliberately excluded: they are not numbers a grounding check against a data payload
            // is meaningful for, and treating them as ungrounded would make almost every answer
            // that mentions a formatted date range fail for a reason unrelated to data accuracy.
            var digitCount = numGroup.Replace(",", string.Empty).Replace(".", string.Empty).TrimStart('0').Length;
            var isQualifyingToken = isCurrency || isPercent || numGroup.Contains('.') || numGroup.Contains(',') || digitCount >= 3;
            if (!isQualifyingToken)
            {
                continue;
            }

            if (!decimal.TryParse(numGroup.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            if (isNegative)
            {
                value = -value;
            }

            if (!IsGrounded(value, isPercent, jsonNumbers))
            {
                ungrounded.Add(raw);
            }
        }

        return new GroundingCheckOutcome(ungrounded.Count == 0, ungrounded);
    }

    private static bool IsGrounded(decimal token, bool isPercent, IReadOnlyList<decimal> jsonNumbers)
    {
        foreach (var candidate in jsonNumbers)
        {
            if (Math.Abs(token - candidate) <= AbsoluteTolerance)
            {
                return true;
            }

            if (isPercent)
            {
                // Text says "12%"; JSON may store the same fact as a fraction (0.12) or already
                // as a percentage-scaled figure (12) — tolerate both representations.
                if (Math.Abs(token - (candidate * 100)) <= 0.5m || Math.Abs((token / 100) - candidate) <= 0.005m)
                {
                    return true;
                }
            }

            // Rounding tolerance: the text may state a JSON value rounded to fewer decimal
            // places ("$48,230" for a JSON value of 48230.00, or "4820.50" for 4820.4999).
            var decimals = CountDecimalDigits(token);
            var rounded = Math.Round(candidate, decimals, MidpointRounding.AwayFromZero);
            if (rounded == token)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountDecimalDigits(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        return dot < 0 ? 0 : text.Length - dot - 1;
    }

    private static List<decimal> ExtractJsonNumbers(string? rawResultJson)
    {
        var numbers = new List<decimal>();
        if (string.IsNullOrWhiteSpace(rawResultJson))
        {
            return numbers;
        }

        try
        {
            using var document = JsonDocument.Parse(rawResultJson);
            CollectNumbers(document.RootElement, numbers);
        }
        catch (JsonException)
        {
            // Malformed/non-JSON payload — nothing to ground against; every numeric token in the
            // explanation will correctly fail the check rather than this method throwing.
        }

        return numbers;
    }

    private static void CollectNumbers(JsonElement element, List<decimal> numbers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var value))
                {
                    numbers.Add(value);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectNumbers(property.Value, numbers);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectNumbers(item, numbers);
                }

                break;
        }
    }

    /// <summary>
    /// FR-004's fallback path: when the grounding check fails, the generated explanation is
    /// discarded and replaced with a plain sentence assembled directly from the fetched data —
    /// simple, but built from the same parsed JSON the check itself validated against, so it is
    /// definitionally grounded. Covers the two worked shapes this service must get right (revenue
    /// aggregate, product-performance ranking) plus a generic flattening fallback for every other
    /// registered query type, per the same "don't over-engineer every shape" scoping the handler
    /// itself follows for table assembly.
    /// </summary>
    public static string BuildTemplateFallback(ResolvedIntent intent, string? rawResultJson)
    {
        if (string.IsNullOrWhiteSpace(rawResultJson))
        {
            return "The requested data was retrieved, but no result was available to summarize.";
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(rawResultJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return "The requested data was retrieved, but it could not be summarized.";
        }

        if (TryGetArray(root, out var rankArray, "products", "results") && rankArray.GetArrayLength() > 0)
        {
            return BuildRankingFallback(intent, rankArray);
        }

        var revenue = GetNumber(root, "revenue") ?? SumSeriesField(root, "revenue");
        var orderCount = GetNumber(root, "orderCount") ?? SumSeriesField(root, "orderCount");
        if (revenue is not null || orderCount is not null)
        {
            var sentence = new StringBuilder("Revenue for the requested period was ");
            sentence.Append(revenue is not null ? FormatNumber(revenue.Value) : "not available");
            sentence.Append(orderCount is not null ? $" across {FormatNumber(orderCount.Value)} orders." : ".");
            return sentence.ToString();
        }

        var flattened = FlattenScalars(root);
        return flattened.Count > 0
            ? "Here is the data for your request: " + string.Join(", ", flattened) + "."
            : "The requested data was retrieved, but no summarizable figures were found in the result.";
    }

    private static string BuildRankingFallback(ResolvedIntent intent, JsonElement rankArray)
    {
        var count = rankArray.GetArrayLength();
        var top = rankArray[0];
        var sku = GetString(top, "sku") ?? GetString(top, "name") ?? "the top result";
        var metricField = intent.Metric switch
        {
            MetricType.UnitsSold => "unitsSold",
            MetricType.OrderCount => "orderCount",
            _ => "revenue",
        };
        var metricLabel = intent.Metric switch
        {
            MetricType.UnitsSold => "units sold",
            MetricType.OrderCount => "order count",
            _ => "revenue",
        };
        var metricValue = GetNumber(top, metricField);

        var sentence = new StringBuilder($"Showing {count} product{(count == 1 ? string.Empty : "s")} ranked by {metricLabel}.");
        if (metricValue is not null)
        {
            sentence.Append($" The top result was {sku} with {metricLabel} of {FormatNumber(metricValue.Value)}.");
        }

        return sentence.ToString();
    }

    private static bool TryGetArray(JsonElement root, out JsonElement array, params string[] propertyNames)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in propertyNames)
            {
                if (root.TryGetProperty(name, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
                {
                    array = candidate;
                    return true;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
            return true;
        }

        array = default;
        return false;
    }

    private static decimal? GetNumber(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var value))
            {
                return value;
            }

            // Money-shaped {amount, currency} sub-object.
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("amount", out var amount) &&
                amount.ValueKind == JsonValueKind.Number &&
                amount.TryGetDecimal(out var amountValue))
            {
                return amountValue;
            }
        }

        return null;
    }

    private static decimal? SumSeriesField(JsonElement root, string propertyName)
    {
        if (!TryGetArray(root, out var series, "series") || series.GetArrayLength() == 0)
        {
            return null;
        }

        decimal? sum = null;
        foreach (var bucket in series.EnumerateArray())
        {
            var value = GetNumber(bucket, propertyName);
            if (value is null)
            {
                continue;
            }

            sum = (sum ?? 0) + value.Value;
        }

        return sum;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static List<string> FlattenScalars(JsonElement root)
    {
        var parts = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return parts;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False)
            {
                parts.Add($"{property.Name}: {property.Value}");
            }
        }

        return parts;
    }

    private static string FormatNumber(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
