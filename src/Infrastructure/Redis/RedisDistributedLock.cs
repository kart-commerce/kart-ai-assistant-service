using StackExchange.Redis;

namespace Kart.AiAssistant.Infrastructure.Redis;

/// <summary>
/// The handle <see cref="RedisConversationSessionRepository.TryAcquireLockAsync"/> hands back —
/// disposing it releases the lock via a compare-and-delete Lua script, so one turn's own (possibly
/// already-expired) lock token can never release a *later* turn's lock on the same conversationId
/// (design-decisions.md's "Concurrency Control for Session State" decision).
/// </summary>
internal sealed class RedisDistributedLock : IAsyncDisposable
{
    // KEYS[1] = lock key, ARGV[1] = this lock instance's own token. Only deletes if the value
    // still matches the token this instance itself set — a plain DEL would risk releasing a
    // different, later request's lock acquired after this one's own PX already expired.
    private const string ReleaseScript = """
        if redis.call("GET", KEYS[1]) == ARGV[1] then
            return redis.call("DEL", KEYS[1])
        else
            return 0
        end
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly string _lockKey;
    private readonly string _lockToken;
    private int _released;

    public RedisDistributedLock(IConnectionMultiplexer redis, string lockKey, string lockToken)
    {
        _redis = redis;
        _lockKey = lockKey;
        _lockToken = lockToken;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        var db = _redis.GetDatabase();
        await db.ScriptEvaluateAsync(ReleaseScript, [(RedisKey)_lockKey], [(RedisValue)_lockToken]);
    }
}
