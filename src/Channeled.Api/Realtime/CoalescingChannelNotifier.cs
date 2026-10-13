using System.Collections.Concurrent;
using Channeled.Application.Abstractions.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Channeled.Api.Realtime;

public sealed class CoalescingChannelNotifier(
    IHubContext<ChannelHub> hub,
    IOptions<RealtimeOptions> options,
    ILogger<CoalescingChannelNotifier> logger) : IChannelNotifier, IDisposable
{
    private readonly ConcurrentDictionary<Guid, ChannelCoalesceState> _filesChanged = new();

    public void NotifyFilesChanged(Guid channelId)
    {
        var opts = options.Value;
        var state = _filesChanged.GetOrAdd(channelId, _ => new ChannelCoalesceState());
        lock (state.Gate)
        {
            state.Pending = true;
            state.LastTouchUtc = DateTimeOffset.UtcNow;
            state.FirstPendingUtc ??= DateTimeOffset.UtcNow;

            state.QuietTimer?.Dispose();
            state.QuietTimer = new Timer(
                _ => _ = FlushFilesChangedAsync(channelId, force: false),
                null,
                opts.CoalesceQuietMilliseconds,
                Timeout.Infinite);

            var elapsed = DateTimeOffset.UtcNow - state.FirstPendingUtc.Value;
            var remainingMax = opts.CoalesceMaxWaitMilliseconds - (int)elapsed.TotalMilliseconds;
            if (remainingMax <= 0)
            {
                _ = FlushFilesChangedAsync(channelId, force: true);
            }
            else if (state.MaxWaitTimer is null)
            {
                state.MaxWaitTimer = new Timer(
                    _ => _ = FlushFilesChangedAsync(channelId, force: true),
                    null,
                    remainingMax,
                    Timeout.Infinite);
            }
        }
    }

    public void NotifyMessageCreated(Guid channelId, object payload) =>
        _ = SafeSendAsync(channelId, "messageCreated", payload);

    public void NotifyMessageDeleted(Guid channelId, object payload) =>
        _ = SafeSendAsync(channelId, "messageDeleted", payload);

    public void NotifyPresenceChanged(Guid channelId, object payload) =>
        _ = SafeSendAsync(channelId, "presenceChanged", payload);

    private async Task FlushFilesChangedAsync(Guid channelId, bool force)
    {
        if (!_filesChanged.TryGetValue(channelId, out var state))
        {
            return;
        }

        lock (state.Gate)
        {
            if (!state.Pending)
            {
                return;
            }

            if (!force)
            {
                var quiet = DateTimeOffset.UtcNow - state.LastTouchUtc;
                if (quiet.TotalMilliseconds < options.Value.CoalesceQuietMilliseconds)
                {
                    return;
                }
            }

            state.Pending = false;
            state.FirstPendingUtc = null;
            state.QuietTimer?.Dispose();
            state.QuietTimer = null;
            state.MaxWaitTimer?.Dispose();
            state.MaxWaitTimer = null;
        }

        await SafeSendAsync(channelId, "filesChanged", new { channelId });
    }

    private async Task SafeSendAsync(Guid channelId, string method, object payload)
    {
        try
        {
            await hub.Clients.Group(ChannelHub.GroupName(channelId)).SendAsync(method, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime broadcast {Method} failed for channel {ChannelId}", method, channelId);
        }
    }

    public void Dispose()
    {
        foreach (var state in _filesChanged.Values)
        {
            lock (state.Gate)
            {
                state.QuietTimer?.Dispose();
                state.MaxWaitTimer?.Dispose();
            }
        }
    }

    private sealed class ChannelCoalesceState
    {
        public object Gate { get; } = new();
        public bool Pending { get; set; }
        public DateTimeOffset LastTouchUtc { get; set; }
        public DateTimeOffset? FirstPendingUtc { get; set; }
        public Timer? QuietTimer { get; set; }
        public Timer? MaxWaitTimer { get; set; }
    }
}
