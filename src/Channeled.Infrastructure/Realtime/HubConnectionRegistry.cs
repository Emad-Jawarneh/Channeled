using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Channeled.Infrastructure.Realtime;

public sealed class HubConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _byConnection = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _byUser = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _byChannel = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _byTeamHint = new();

    public bool TryRegister(HubCallerContext context, Guid userId, int maxPerUser)
    {
        var userMap = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        if (userMap.Count >= maxPerUser)
        {
            return false;
        }

        var entry = new ConnectionEntry(context, userId);
        if (!_byConnection.TryAdd(context.ConnectionId, entry))
        {
            return false;
        }

        userMap[context.ConnectionId] = 0;
        return true;
    }

    public void Unregister(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var entry))
        {
            return;
        }

        if (_byUser.TryGetValue(entry.UserId, out var userMap))
        {
            userMap.TryRemove(connectionId, out _);
        }

        foreach (var channelId in entry.ChannelIds)
        {
            if (_byChannel.TryGetValue(channelId, out var map))
            {
                map.TryRemove(connectionId, out _);
            }
        }
    }

    public bool TryAddGroup(string connectionId, Guid channelId, int maxGroups)
    {
        if (!_byConnection.TryGetValue(connectionId, out var entry))
        {
            return false;
        }

        if (entry.ChannelIds.Count >= maxGroups && !entry.ChannelIds.Contains(channelId))
        {
            return false;
        }

        entry.ChannelIds.Add(channelId);
        _byChannel.GetOrAdd(channelId, _ => new ConcurrentDictionary<string, byte>())[connectionId] = 0;
        return true;
    }

    public void RemoveGroup(string connectionId, Guid channelId)
    {
        if (!_byConnection.TryGetValue(connectionId, out var entry))
        {
            return;
        }

        entry.ChannelIds.Remove(channelId);
        if (_byChannel.TryGetValue(channelId, out var map))
        {
            map.TryRemove(connectionId, out _);
        }
    }

    public void AssociateTeam(string connectionId, Guid teamId)
    {
        _byTeamHint.GetOrAdd(teamId, _ => new ConcurrentDictionary<string, byte>())[connectionId] = 0;
    }

    public IReadOnlyList<HubCallerContext> ConnectionsForUser(Guid userId)
    {
        if (!_byUser.TryGetValue(userId, out var map))
        {
            return [];
        }

        return map.Keys
            .Select(id => _byConnection.TryGetValue(id, out var e) ? e.Context : null)
            .Where(c => c is not null)
            .Cast<HubCallerContext>()
            .ToList();
    }

    public IReadOnlyList<HubCallerContext> ConnectionsForChannel(Guid channelId)
    {
        if (!_byChannel.TryGetValue(channelId, out var map))
        {
            return [];
        }

        return map.Keys
            .Select(id => _byConnection.TryGetValue(id, out var e) ? e.Context : null)
            .Where(c => c is not null)
            .Cast<HubCallerContext>()
            .ToList();
    }

    public IReadOnlyList<(string ConnectionId, Guid UserId, HashSet<Guid> ChannelIds, HubCallerContext Context)> Snapshot()
    {
        return _byConnection.Values
            .Select(e => (e.Context.ConnectionId, e.UserId, e.ChannelIds, e.Context))
            .ToList();
    }

    public void AbortUser(Guid userId)
    {
        foreach (var ctx in ConnectionsForUser(userId))
        {
            try
            {
                ctx.Abort();
            }
            catch
            {
                // ignore
            }
        }
    }

    public void AbortChannel(Guid channelId)
    {
        foreach (var ctx in ConnectionsForChannel(channelId))
        {
            try
            {
                ctx.Abort();
            }
            catch
            {
                // ignore
            }
        }
    }

    private sealed class ConnectionEntry(HubCallerContext context, Guid userId)
    {
        public HubCallerContext Context { get; } = context;
        public Guid UserId { get; } = userId;
        public HashSet<Guid> ChannelIds { get; } = [];
    }
}
