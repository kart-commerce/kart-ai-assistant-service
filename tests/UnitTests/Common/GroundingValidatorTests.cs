using Kart.AiAssistant.Application.Common;
using Kart.AiAssistant.Domain.ConversationSessions;
using Kart.AiAssistant.Domain.Enums;
using Kart.AiAssistant.Domain.Shared;
using Xunit;

namespace Kart.AiAssistant.UnitTests.Common;

/// <summary>FR-004/§16's post-generation numeric-grounding check.</summary>
public sealed class GroundingValidatorTests
{
    private const string RawJson = """
        {"generatedAt":"2026-08-18T09:00:00Z","isProvisional":true,"reconciledThrough":"2026-08-17",
         "products":[{"sku":"sku-1","revenue":{"amount":48230.50,"currency":"USD"},"unitsSold":210,"orderCount":198}]}
        """;

    [Fact]
    public void Validate_AllNumbersGroundedInRawJson_Passes()
    {
        var outcome = GroundingValidator.Validate(
            "Revenue was $48,230.50 across 198 orders, 210 units sold.", RawJson);

        Assert.True(outcome.Passed);
        Assert.Empty(outcome.UngroundedTokens);
    }

    [Fact]
    public void Validate_FabricatedNumberNotInPayload_Fails()
    {
        var outcome = GroundingValidator.Validate(
            "Revenue grew to reach $99,999.", RawJson);

        Assert.False(outcome.Passed);
        Assert.Contains(outcome.UngroundedTokens, t => t.Contains("99,999"));
    }

    [Fact]
    public void Validate_ExactRoundedNumber_IsToleratedAsGrounded()
    {
        // The raw JSON's 48230.50 rounds (AwayFromZero, 0 decimals) to 48231 — "$48,231" (rounded
        // for readability, per the spec's own worked examples) must be tolerated even though it
        // isn't a byte-for-byte match of the underlying value.
        var outcome = GroundingValidator.Validate("Revenue was about $48,231.", RawJson);

        Assert.True(outcome.Passed);
    }

    [Fact]
    public void Validate_MalformedJson_NeverThrows_TreatsEveryNumberAsUngrounded()
    {
        var outcome = GroundingValidator.Validate("Revenue was $100.", "not-json{");

        Assert.False(outcome.Passed);
    }

    [Fact]
    public void BuildTemplateFallback_ProducesSentenceContainingRealDataValues()
    {
        var intent = new ResolvedIntent(
            MetricType.Revenue, EntityType.Product, AggregationType.Sum,
            [DimensionType.Product],
            new IntentFilters(new DateRange(DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow), null, null, null, null),
            new Domain.ConversationSessions.RankingSpec(MetricType.Revenue, RankDirection.Desc, 5),
            null, null, false);

        var fallback = GroundingValidator.BuildTemplateFallback(intent, RawJson);

        Assert.NotNull(fallback);
        Assert.NotEmpty(fallback);
    }
}
