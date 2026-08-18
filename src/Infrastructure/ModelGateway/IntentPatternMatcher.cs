using System.Text.RegularExpressions;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;

namespace Kart.AiAssistant.Infrastructure.ModelGateway;

/// <summary>
/// Deterministic, keyword/regex-based natural-language understanding rules for
/// <see cref="MockModelProvider"/> — kept in its own file since <c>MockModelProvider.cs</c>'s own
/// job (orchestrating these matches into a <c>PlanCallResult</c>) is large enough on its own.
/// Every pattern here is traceable to a specific bullet in genai-business-assistant-spec.md §15/§5/§6.
/// </summary>
public static class IntentPatternMatcher
{
    // §7/§26-Data-1/§26-Data-2 — geography and seller/vendor are permanently out of scope
    // (ADR-0026/0027): no Entity/MetricType member exists for either, so detecting the phrase here
    // and returning an intent with no metric/entity lets the Application layer's registry lookup
    // fail naturally into the "unsupported" response (FR-009), rather than mis-matching some other
    // pattern (e.g. a bare ranking regex) and answering with a category-mislabeled figure.
    private static readonly Regex GeographyOrSellerPhrase = new(
        @"\b(area|city|cities|region|regional|geograph\w*|location|seller|vendor|dhaka|chittagong|chattogram|sylhet|khulna|rajshahi|barisal|rangpur|mymensingh)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FunnelPhrase = new(
        @"\b(conversion\s+funnel|funnel|drop[-\s]?off)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InventoryPhrase = new(
        @"\b(inventory\s+movement|inventory|reserved|released|replenished)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PeakHourPhrase = new(
        @"\b(peak\s+(sales\s+)?hour|hour[-\s]of[-\s]day|peak\s+time)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "top 5", "bottom 3", "worst 10", "best 5" — the explicit ranking-with-limit shape.
    private static readonly Regex RankingWithLimit = new(
        @"\b(top|bottom|worst|best)\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // §15.2's default-metric-resolution table entries — a superlative phrase that already narrows
    // the field enough that a clarification would be pedantic, even with no explicit number.
    private static readonly Regex UnqualifiedSuperlativeWithContext = new(
        @"\b(top\s+selling|best[-\s]selling|top\s+products?|most\s+units(\s+sold)?|highest\s+revenue|most\s+orders?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // §15.1's canonical hard-stop trigger — a superlative with genuinely no ranking context
    // ("best products," not "best selling" or "top products").
    private static readonly Regex BareSuperlative = new(
        @"\b(best|worst|declining)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnitsKeyword = new(@"\bunits?(\s+sold)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OrderCountKeyword = new(@"\border(s)?(\s+count)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OnlyOrJustFollowUp = new(
        @"\b(?:only|just)\s+([A-Za-z][A-Za-z&'\-]*(?:\s+[A-Za-z][A-Za-z&'\-]*){0,2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ComparisonFollowUpPhrase = new(
        @"\bcompar\w*\b.*\b(to|with|against)\b.*\b(last\s+week|previous\s+week|last\s+month|previous\s+month|previous\s+period|prior\s+period)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ComparisonQuery = new(@"\bcompar\w*\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChartTypeHint = new(
        @"\b(line|bar|donut|pie)\s*chart\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RevenueOrSalesKeyword = new(
        @"\b(revenue|sales)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> GenericTrailingCategoryWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "products", "product", "category", "categories", "items", "item",
    };

    public static bool IsOutOfScopeDimension(string message) => GeographyOrSellerPhrase.IsMatch(message);

    public static bool IsFunnelQuery(string message) => FunnelPhrase.IsMatch(message);

    public static bool IsInventoryQuery(string message) => InventoryPhrase.IsMatch(message);

    public static bool IsPeakHourQuery(string message) => PeakHourPhrase.IsMatch(message);

    public static bool IsRevenueOrSalesMention(string message) => RevenueOrSalesKeyword.IsMatch(message);

    public static bool IsComparisonQuery(string message) => ComparisonQuery.IsMatch(message);

    public static bool IsComparisonFollowUp(string message) => ComparisonFollowUpPhrase.IsMatch(message);

    public static bool IsUnqualifiedSuperlativeWithContext(string message) => UnqualifiedSuperlativeWithContext.IsMatch(message);

    public static bool IsBareSuperlativeNoContext(string message) => BareSuperlative.IsMatch(message);

    public static bool TryMatchRankingWithLimit(string message, out int limit, out RankDirection direction)
    {
        var match = RankingWithLimit.Match(message);
        if (!match.Success)
        {
            limit = 0;
            direction = RankDirection.Desc;
            return false;
        }

        limit = int.Parse(match.Groups[2].Value);
        var word = match.Groups[1].Value;
        direction = word.Equals("bottom", StringComparison.OrdinalIgnoreCase) || word.Equals("worst", StringComparison.OrdinalIgnoreCase)
            ? RankDirection.Asc
            : RankDirection.Desc;
        return true;
    }

    /// <summary>§15.2's default-metric-resolution table: units/orders keywords narrow the field
    /// explicitly; anything else (including a truly unqualified "top selling") falls back to
    /// Revenue, the spec's own stated default.</summary>
    public static MetricType ResolveRankingMetric(string message)
    {
        if (UnitsKeyword.IsMatch(message))
        {
            return MetricType.UnitsSold;
        }

        if (OrderCountKeyword.IsMatch(message))
        {
            return MetricType.OrderCount;
        }

        return MetricType.Revenue;
    }

    public static bool TryMatchCategoryFollowUp(string message, out string category)
    {
        var match = OnlyOrJustFollowUp.Match(message);
        if (!match.Success)
        {
            category = string.Empty;
            return false;
        }

        var words = match.Groups[1].Value.Trim().TrimEnd('.', '!', '?')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        while (words.Count > 1 && GenericTrailingCategoryWords.Contains(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }

        category = string.Join(' ', words);
        return category.Length > 0;
    }

    public static bool TryMatchVisualizationHint(string message, out VisualizationType visualizationType)
    {
        var match = ChartTypeHint.Match(message);
        if (!match.Success)
        {
            visualizationType = VisualizationType.TableOnly;
            return false;
        }

        visualizationType = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "line" => VisualizationType.LineChart,
            "donut" or "pie" => VisualizationType.DonutChart,
            _ => VisualizationType.BarChart,
        };
        return true;
    }
}
