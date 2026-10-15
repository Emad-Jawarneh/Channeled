using Channeled.Application.Files;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Files;

/// <summary>In-process concurrent download slots (single instance by design; see DECISIONS "single instance for v1").</summary>
public sealed class DownloadSlots(IOptionsMonitor<DownloadOptions> options) : IDownloadSlots
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, int> _perUser = [];
    private int _global;

    public int ActiveFor(Guid userId)
    {
        lock (_gate)
        {
            return _perUser.GetValueOrDefault(userId);
        }
    }

    public int ActiveGlobal
    {
        get
        {
            lock (_gate)
            {
                return _global;
            }
        }
    }

    public IDisposable? TryAcquire(Guid userId)
    {
        var limits = options.CurrentValue;
        lock (_gate)
        {
            var mine = _perUser.GetValueOrDefault(userId);
            if (mine >= Math.Max(1, limits.MaxConcurrentPerUser) || _global >= Math.Max(1, limits.MaxConcurrentGlobal))
            {
                return null;
            }

            _perUser[userId] = mine + 1;
            _global++;
        }

        return new Lease(this, userId);
    }

    private void Release(Guid userId)
    {
        lock (_gate)
        {
            var mine = _perUser.GetValueOrDefault(userId);
            if (mine <= 1)
            {
                _perUser.Remove(userId);
            }
            else
            {
                _perUser[userId] = mine - 1;
            }

            _global = Math.Max(0, _global - 1);
        }
    }

    private sealed class Lease(DownloadSlots owner, Guid userId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(userId);
            }
        }
    }
}
