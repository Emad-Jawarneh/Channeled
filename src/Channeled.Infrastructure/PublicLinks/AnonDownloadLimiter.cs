using System.Diagnostics;
using Channeled.Application.PublicLinks;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.PublicLinks;

/// <summary>
/// Anonymous-only limiter: global and per-IP concurrent stream caps plus a global and a per-stream egress
/// throttle (reservation-style leaky bucket). Authenticated downloads never touch this, so saturating
/// anonymous capacity cannot starve signed-in users.
/// </summary>
public sealed class AnonDownloadLimiter(IOptionsMonitor<PublicFileLinksOptions> options) : IAnonDownloadLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _perIp = new(StringComparer.Ordinal);
    private int _global;
    private long _globalNextFree;

    private IOptionsMonitor<PublicFileLinksOptions> Options => options;

    public int ActiveStreams
    {
        get
        {
            lock (_gate)
            {
                return _global;
            }
        }
    }

    public IAnonStreamLease? TryAcquire(string clientIp)
    {
        var opts = options.CurrentValue;
        var ip = string.IsNullOrEmpty(clientIp) ? "unknown" : clientIp;
        lock (_gate)
        {
            if (_global >= opts.MaxGlobalAnonStreams)
            {
                return null;
            }

            _perIp.TryGetValue(ip, out var perIp);
            if (perIp >= opts.MaxAnonStreamsPerIp)
            {
                return null;
            }

            _global++;
            _perIp[ip] = perIp + 1;
        }

        return new Lease(this, ip);
    }

    private void Release(string ip)
    {
        lock (_gate)
        {
            _global = Math.Max(0, _global - 1);
            if (_perIp.TryGetValue(ip, out var n))
            {
                if (n <= 1)
                {
                    _perIp.Remove(ip);
                }
                else
                {
                    _perIp[ip] = n - 1;
                }
            }
        }
    }

    private long ReserveGlobalDelayTicks(int bytes)
    {
        var rate = options.CurrentValue.GlobalAnonEgressBytesPerSecond;
        if (rate <= 0)
        {
            return 0;
        }

        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var start = Math.Max(now, _globalNextFree);
            _globalNextFree = start + (long)(bytes * (double)Stopwatch.Frequency / rate);
            return start - now;
        }
    }

    private sealed class Lease(AnonDownloadLimiter owner, string ip) : IAnonStreamLease
    {
        private long _nextFree;
        private int _disposed;

        public async ValueTask ThrottleAsync(int bytes, CancellationToken ct)
        {
            if (bytes <= 0)
            {
                return;
            }

            var delayTicks = owner.ReserveGlobalDelayTicks(bytes);
            var rate = owner.Options.CurrentValue.PerStreamBytesPerSecond;
            if (rate > 0)
            {
                var now = Stopwatch.GetTimestamp();
                var start = Math.Max(now, _nextFree);
                _nextFree = start + (long)(bytes * (double)Stopwatch.Frequency / rate);
                delayTicks = Math.Max(delayTicks, start - now);
            }

            var delayMs = delayTicks * 1000.0 / Stopwatch.Frequency;
            if (delayMs >= 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(ip);
            }
        }
    }
}

/// <summary>Tracks bytes served per ticket (in-memory) to enforce the ticket byte cap.</summary>
public sealed class TicketUsageTracker
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Usage> _usage = new();
    private long _ops;

    /// <summary>Adds bytes and returns the new total served for the ticket.</summary>
    public long Add(Guid ticketId, long bytes, DateTimeOffset ticketExpiresAt, DateTimeOffset now)
    {
        if (Interlocked.Increment(ref _ops) % 256 == 0)
        {
            foreach (var kv in _usage)
            {
                if (kv.Value.ExpiresAt <= now)
                {
                    _usage.TryRemove(kv);
                }
            }
        }

        var u = _usage.GetOrAdd(ticketId, _ => new Usage(ticketExpiresAt));
        return Interlocked.Add(ref u.Bytes, bytes);
    }

    public long Get(Guid ticketId) => _usage.TryGetValue(ticketId, out var u) ? Interlocked.Read(ref u.Bytes) : 0;

    private sealed class Usage(DateTimeOffset expiresAt)
    {
        public readonly DateTimeOffset ExpiresAt = expiresAt;
        public long Bytes;
    }
}
