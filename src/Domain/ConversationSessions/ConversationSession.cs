namespace Kart.AiAssistant.Domain.ConversationSessions;

/// <summary>
/// The conversation-session aggregate (ddd-model.md) — Redis-backed (design-decisions.md), one
/// per chat panel instance. Holds only the LAST resolved structured intent, never the raw chat
/// transcript (§14.1: "the authoritative state driving the next query is the structured intent
/// object"). TTL/expiry is enforced by the Redis key's own expiry (UX-1 is still an open
/// question on the concrete duration — this aggregate is deliberately TTL-agnostic of that
/// number; expiry is infrastructure's job, not a field on this type).
/// </summary>
public sealed class ConversationSession
{
    public Guid ConversationId { get; private set; }
    public string UserId { get; private set; }
    public ResolvedIntent? LastResolvedIntent { get; private set; }
    public TurnProvenance? LastTurnProvenance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>Tracks the first occurrence, within this session, of an unqualified superlative
    /// ("top selling") resolving to the Revenue default (§15.2) — the assistant states the
    /// assumption explicitly only the first time, then remembers the choice for the rest of the
    /// session.</summary>
    public bool UnqualifiedSuperlativeDefaultAlreadyDisclosed { get; private set; }

    private ConversationSession(Guid conversationId, string userId, DateTimeOffset now)
    {
        ConversationId = conversationId;
        UserId = userId;
        CreatedAt = now;
        LastActivityAt = now;
    }

    public static ConversationSession Start(Guid conversationId, string userId, DateTimeOffset now) =>
        new(conversationId, userId, now);

    public void RecordTurn(ResolvedIntent intent, TurnProvenance provenance, DateTimeOffset now)
    {
        LastResolvedIntent = intent;
        LastTurnProvenance = provenance;
        LastActivityAt = now;
        if (provenance.UnqualifiedSuperlativeDefaultApplied)
        {
            UnqualifiedSuperlativeDefaultAlreadyDisclosed = true;
        }
    }

    public bool IsExpired(TimeSpan idleTimeout, DateTimeOffset now) => now - LastActivityAt > idleTimeout;
}
