using System.Security.Claims;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Chat;
using Channeled.Domain.Authorization;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Realtime;
using Channeled.Infrastructure.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Api.Realtime;

/// <summary>
/// Client-invokable methods: JoinChannel and LeaveChannel only. Mutations (including chat posts/deletes) go
/// through REST; the server broadcasts messageCreated / messageDeleted / presenceChanged after commit.
/// </summary>
[Authorize]
public sealed class ChannelHub(
    ApplicationDbContext db,
    IAuthorizer authorizer,
    HubConnectionRegistry registry,
    PresenceTracker presence,
    IChannelNotifier notifier,
    IOptions<RealtimeOptions> options) : Hub
{
    public static string GroupName(Guid channelId) => $"channel:{channelId:D}";

    public static string UserGroupName(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var userId = RequireUserId();
        if (!registry.TryRegister(Context, userId, options.Value.MaxConnectionsPerUser))
        {
            Context.Abort();
            return;
        }

        // Personal inbox (mentions / replies) — independent of channel joins.
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Unregister(Context.ConnectionId);

        // Closing the LAST tab/connection of a user removes their presence; other tabs keep it.
        foreach (var offline in presence.LeaveAll(Context.ConnectionId))
        {
            notifier.NotifyPresenceChanged(offline.ChannelId, offline);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChannel(Guid channelId)
    {
        var userId = RequireUserId();
        var channel = await FindViewableChannelAsync(userId, channelId);
        if (channel is null)
        {
            throw new HubException("not_found");
        }

        if (!registry.TryAddGroup(Context.ConnectionId, channelId, options.Value.MaxGroupsPerConnection))
        {
            throw new HubException("group_limit");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(channelId));

        // Presence is tracked for text channels only.
        if (channel.Kind == ChannelKind.Text)
        {
            var displayName = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync() ?? "Unknown user";
            if (presence.Join(channelId, userId, displayName, Context.ConnectionId))
            {
                notifier.NotifyPresenceChanged(channelId, new PresenceChangedEvent(channelId, userId, displayName, Online: true));
            }
        }
    }

    public async Task LeaveChannel(Guid channelId)
    {
        registry.RemoveGroup(Context.ConnectionId, channelId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(channelId));

        var offline = presence.Leave(channelId, Context.ConnectionId);
        if (offline is not null)
        {
            notifier.NotifyPresenceChanged(channelId, offline);
        }
    }

    private Guid RequireUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (raw is null || !Guid.TryParse(raw, out var userId))
        {
            throw new HubException("unauthorized");
        }

        return userId;
    }

    private async Task<ChannelEntity?> FindViewableChannelAsync(Guid userId, Guid channelId)
    {
        var channel = await db.Channels.AsNoTracking()
            .Include(c => c.Team)
            .FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel?.Team is null || channel.ArchivedAt != null)
        {
            return null;
        }

        var membership = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == channel.TeamId && m.UserId == userId);
        if (membership is null)
        {
            return null;
        }

        var channelMember = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId);

        var ctx = new AuthorizationContext
        {
            Action = AuthAction.ViewChannel,
            UserId = userId,
            IsTeamMember = true,
            TeamRole = membership.Role,
            TeamArchived = channel.Team.ArchivedAt != null,
            ChannelExists = true,
            ChannelVisibility = channel.Visibility,
            ChannelArchived = channel.ArchivedAt != null,
            IsChannelMember = channelMember is not null,
            ChannelCanWrite = channelMember is not null && !channelMember.ReadOnly,
            ChannelCanManage = false,
        };

        return authorizer.Authorize(ctx).Allowed ? channel : null;
    }
}
