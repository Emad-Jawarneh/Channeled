using Channeled.Application.Chat;

namespace Channeled.Infrastructure.Realtime;

/// <summary>
/// In-memory presence for TEXT channels only (callers decide eligibility). A user is online in a channel while at
/// least one of their connections (tabs) is joined; presence is removed only when the LAST connection leaves or
/// disconnects. Single-node by design (self-hosted, one machine).
/// </summary>
public sealed class PresenceTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<Guid, UserPresence>> _channels = [];
    private readonly Dictionary<string, ConnectionPresence> _connections = [];

    /// <summary>Returns true when this call made the user newly online in the channel (first connection).</summary>
    public bool Join(Guid channelId, Guid userId, string displayName, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var conn))
            {
                conn = new ConnectionPresence(userId);
                _connections[connectionId] = conn;
            }

            if (!conn.Channels.Add(channelId))
            {
                return false;
            }

            if (!_channels.TryGetValue(channelId, out var users))
            {
                users = [];
                _channels[channelId] = users;
            }

            if (!users.TryGetValue(userId, out var presence))
            {
                presence = new UserPresence(displayName);
                users[userId] = presence;
            }

            presence.ConnectionIds.Add(connectionId);
            return presence.ConnectionIds.Count == 1;
        }
    }

    /// <summary>Returns the offline event when this was the user's last connection in the channel.</summary>
    public PresenceChangedEvent? Leave(Guid channelId, string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var conn) || !conn.Channels.Remove(channelId))
            {
                return null;
            }

            var offline = RemoveFromChannel(channelId, conn.UserId, connectionId);
            if (conn.Channels.Count == 0)
            {
                _connections.Remove(connectionId);
            }

            return offline;
        }
    }

    /// <summary>Removes a disconnected connection from every channel; returns users who went offline.</summary>
    public IReadOnlyList<PresenceChangedEvent> LeaveAll(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var conn))
            {
                return [];
            }

            var offline = new List<PresenceChangedEvent>();
            foreach (var channelId in conn.Channels)
            {
                var evt = RemoveFromChannel(channelId, conn.UserId, connectionId);
                if (evt is not null)
                {
                    offline.Add(evt);
                }
            }

            return offline;
        }
    }

    public IReadOnlyList<PresenceUserDto> Online(Guid channelId)
    {
        lock (_gate)
        {
            if (!_channels.TryGetValue(channelId, out var users))
            {
                return [];
            }

            return users
                .Select(kv => new PresenceUserDto(kv.Key, kv.Value.DisplayName))
                .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(u => u.UserId)
                .ToList();
        }
    }

    private PresenceChangedEvent? RemoveFromChannel(Guid channelId, Guid userId, string connectionId)
    {
        if (!_channels.TryGetValue(channelId, out var users) || !users.TryGetValue(userId, out var presence))
        {
            return null;
        }

        presence.ConnectionIds.Remove(connectionId);
        if (presence.ConnectionIds.Count > 0)
        {
            return null;
        }

        users.Remove(userId);
        if (users.Count == 0)
        {
            _channels.Remove(channelId);
        }

        return new PresenceChangedEvent(channelId, userId, presence.DisplayName, Online: false);
    }

    private sealed class UserPresence(string displayName)
    {
        public string DisplayName { get; } = displayName;
        public HashSet<string> ConnectionIds { get; } = [];
    }

    private sealed class ConnectionPresence(Guid userId)
    {
        public Guid UserId { get; } = userId;
        public HashSet<Guid> Channels { get; } = [];
    }
}
