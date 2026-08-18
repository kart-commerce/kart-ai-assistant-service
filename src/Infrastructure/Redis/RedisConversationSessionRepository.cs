using Kart.AiAssistant.Application.Common.Interfaces;
using Kart.AiAssistant.Domain.ConversationSessions;
using StackExchange.Redis;

namespace Kart.AiAssistant.Infrastructure.Redis;

/// <summary>
/// design-decisions.md's "Conversation-Session Storage" decision: a single Redis String per
/// conversation, holding the whole <see cref="ConversationSession"/> value as one JSON blob
/// (never a Hash — see database-design.md's "Why a String... not a Hash" reasoning), replaced
/// wholesale on every turn, with native TTL (sliding refresh on every write). The per-conversationId
/// lock (also here) is the "Concurrency Control for Session State" decision's own mechanism.
/// </summary>
public sealed class RedisConversationSessionRepository : IConversationSessionRepository
{
    private readonly IConnectionMultiplexer _redis;

    public RedisConversationSessionRepository(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<ConversationSession?> GetAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync(SessionKey(conversationId));
        return json.IsNullOrEmpty ? null : ConversationSessionSerializer.Deserialize(json!);
    }

    public async Task SaveAsync(ConversationSession session, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var json = ConversationSessionSerializer.Serialize(session);
        await db.StringSetAsync(SessionKey(session.ConversationId), json, ttl);
    }

    public async Task<IAsyncDisposable?> TryAcquireLockAsync(Guid conversationId, TimeSpan lockTimeout, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var lockKey = LockKey(conversationId);
        var lockToken = Guid.NewGuid().ToString("N");

        // SET NX PX — the standard single-instance Redis lock pattern (design-decisions.md).
        var acquired = await db.StringSetAsync(lockKey, lockToken, lockTimeout, When.NotExists);
        return acquired ? new RedisDistributedLock(_redis, lockKey, lockToken) : null;
    }

    private static string SessionKey(Guid conversationId) => $"ai-assistant:session:{conversationId}";

    private static string LockKey(Guid conversationId) => $"ai-assistant:session-lock:{conversationId}";
}
