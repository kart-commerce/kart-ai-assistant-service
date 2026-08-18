using Kart.AiAssistant.Domain.ConversationSessions;

namespace Kart.AiAssistant.Application.Common.Interfaces;

/// <summary>Redis-backed (design-decisions.md's closed storage decision) — this interface is
/// technology-agnostic on purpose so a UnitTest can fake it with an in-memory dictionary.</summary>
public interface IConversationSessionRepository
{
    Task<ConversationSession?> GetAsync(Guid conversationId, CancellationToken cancellationToken);

    Task SaveAsync(ConversationSession session, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Per-conversationId lock (design-decisions.md "Concurrency Control for Session
    /// State") — a second concurrent request for the same conversation is rejected, never
    /// silently interleaved. Returns null if the lock could not be acquired.</summary>
    Task<IAsyncDisposable?> TryAcquireLockAsync(Guid conversationId, TimeSpan lockTimeout, CancellationToken cancellationToken);
}
