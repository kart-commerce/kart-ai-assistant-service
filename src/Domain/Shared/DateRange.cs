namespace Kart.AiAssistant.Domain.Shared;

/// <summary>Half-open [From, To) window, always resolved to explicit UTC instants before it
/// reaches this type (genai-business-assistant-spec.md §8 — "rolling 7 days" etc. resolved at
/// request time by application code, never left as a relative phrase past the planning step).</summary>
public sealed record DateRange(DateTimeOffset From, DateTimeOffset To)
{
    public bool IsValid => To > From;
}

public sealed record Money(decimal Amount, string Currency);
