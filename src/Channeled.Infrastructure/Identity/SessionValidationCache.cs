using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Channeled.Infrastructure.Identity;

public sealed class SessionValidationCache(IMemoryCache cache)
{
    private static readonly ConcurrentDictionary<Guid, byte> NoOp = new();

    public bool? TryGet(Guid sessionId)
    {
        if (cache.TryGetValue(CacheKey(sessionId), out bool valid))
        {
            return valid;
        }

        return null;
    }

    public void Set(Guid sessionId, bool valid, TimeSpan ttl)
    {
        cache.Set(CacheKey(sessionId), valid, ttl);
        NoOp.TryAdd(sessionId, 0);
    }

    public void Invalidate(Guid sessionId) => cache.Remove(CacheKey(sessionId));

    private static string CacheKey(Guid sessionId) => $"sess-valid:{sessionId:D}";
}
