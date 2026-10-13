using System.Collections.Concurrent;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.PublicLinks;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.PublicLinks;

/// <summary>
/// In-memory idempotency for claims: <c>ConcurrentDictionary.GetOrAdd</c> of a lazy Task so concurrent
/// duplicates share exactly one consume. Scoped to the token hash; entries live for
/// <c>ClaimIdempotencySeconds</c>.
///
/// NOTE (known limitation, see DECISIONS): the store is per-process memory. A process restart (or a second
/// instance) forgets prior claims, so a retry after a restart can double-count a download. Failures are
/// not retained, so a failed claim can be retried.
/// </summary>
public sealed class ClaimIdempotencyStore(IOptionsMonitor<PublicFileLinksOptions> options, IClock clock)
{
    private readonly ConcurrentDictionary<(string TokenHash, string Key), Entry> _entries = new();
    private long _ops;

    public async Task<AppResult<ShareClaimDto>> GetOrAddAsync(
        string tokenHash,
        string key,
        Func<Task<AppResult<ShareClaimDto>>> factory)
    {
        var window = TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.ClaimIdempotencySeconds));
        var now = clock.UtcNow;
        SweepOccasionally(now);

        var dictKey = (tokenHash, key);
        if (_entries.TryGetValue(dictKey, out var existing) && now - existing.CreatedAt >= window)
        {
            _entries.TryRemove(new KeyValuePair<(string, string), Entry>(dictKey, existing));
        }

        var entry = _entries.GetOrAdd(dictKey, _ => new Entry(now, new Lazy<Task<AppResult<ShareClaimDto>>>(factory)));

        try
        {
            var result = await entry.Lazy.Value.ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _entries.TryRemove(new KeyValuePair<(string, string), Entry>(dictKey, entry));
            }

            return result;
        }
        catch
        {
            _entries.TryRemove(new KeyValuePair<(string, string), Entry>(dictKey, entry));
            throw;
        }
    }

    public int Count => _entries.Count;

    private void SweepOccasionally(DateTimeOffset now)
    {
        if (Interlocked.Increment(ref _ops) % 64 != 0)
        {
            return;
        }

        var window = TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.ClaimIdempotencySeconds));
        foreach (var kv in _entries)
        {
            if (now - kv.Value.CreatedAt >= window)
            {
                _entries.TryRemove(kv);
            }
        }
    }

    private sealed record Entry(DateTimeOffset CreatedAt, Lazy<Task<AppResult<ShareClaimDto>>> Lazy);
}
