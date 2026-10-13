using System.Globalization;
using System.Text;
using System.Text.Json;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Chat;
using Channeled.Application.Identity;
using Channeled.Application.Notifications;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Notifications;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Notifications;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Realtime;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Chat;

public sealed class ChatService(
    ApplicationDbContext db,
    IAuthorizer authorizer,
    IClock clock,
    IChannelNotifier notifier,
    PresenceTracker presence) : IChatService
{
    public async Task<AppResult<ChannelMessageDto>> PostMessageAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        PostMessageRequest request,
        CancellationToken ct = default)
    {
        var access = await LoadTextChannelAsync(userId, teamSlug, channelName, ct);
        if (access.Failure is not null)
        {
            return AppResult<ChannelMessageDto>.Fail(access.Failure.Code, access.Failure.Message);
        }

        var channel = access.Channel!;
        var decision = authorizer.Authorize(ToContext(userId, AuthAction.PostChannelMessage, channel, isAuthor: false));
        if (!decision.Allowed)
        {
            return decision.ReasonCode == "channel_archived"
                ? AppResult<ChannelMessageDto>.Fail(ErrorCodes.ChannelArchived, "Channel is archived.")
                : AppResult<ChannelMessageDto>.Fail(ErrorCodes.Forbidden, "You cannot post in this channel.");
        }

        var body = request.Body;
        if (string.IsNullOrWhiteSpace(body) || body.Contains('\0', StringComparison.Ordinal))
        {
            return AppResult<ChannelMessageDto>.Fail(ErrorCodes.Validation, "Message body is required.");
        }

        if (CodePointCount(body) > ChatOptions.MaxBodyLength)
        {
            return AppResult<ChannelMessageDto>.Fail(
                ErrorCodes.MessageTooLong,
                $"Message body must be at most {ChatOptions.MaxBodyLength} characters.");
        }

        var author = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "Unknown user";

        ChannelMessageEntity? parent = null;
        if (request.ReplyToMessageId is Guid replyId)
        {
            parent = await db.ChannelMessages.AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == replyId && m.ChannelId == channel.Channel.Id, ct);
            if (parent is null)
            {
                return AppResult<ChannelMessageDto>.Fail(ErrorCodes.Validation, "Reply target was not found in this channel.");
            }
        }

        var teamMembers = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == channel.Team.Id)
            .Join(db.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.DisplayName })
            .ToListAsync(ct);
        var memberTuples = teamMembers.Select(m => (m.Id, m.DisplayName)).ToList();
        var mentionIds = MentionResolver.Resolve(body, memberTuples, request.MentionedUserIds);

        // Postgres timestamptz is microsecond-precise; truncate so the returned value equals the stored value.
        var now = clock.UtcNow;
        now = now.AddTicks(-(now.Ticks % 10));

        var message = new ChannelMessageEntity
        {
            Id = UuidV7.NewGuid(),
            ChannelId = channel.Channel.Id,
            AuthorId = userId,
            Body = body,
            CreatedAt = now,
            ReplyToMessageId = parent?.Id,
        };
        db.ChannelMessages.Add(message);
        foreach (var mentionedId in mentionIds)
        {
            db.MessageMentions.Add(new MessageMentionEntity { MessageId = message.Id, UserId = mentionedId });
        }

        var preview = Truncate(body.Trim(), 280);
        var notifications = BuildNotifications(
            actorId: userId,
            messageId: message.Id,
            teamId: channel.Team.Id,
            channelId: channel.Channel.Id,
            bodyPreview: preview,
            createdAt: now,
            parentAuthorId: parent is { DeletedAt: null } ? parent.AuthorId : null,
            mentionIds: mentionIds);
        foreach (var n in notifications)
        {
            db.Notifications.Add(n);
        }

        await db.SaveChangesAsync(ct);

        MessageReplyPreviewDto? replyDto = null;
        if (parent is not null)
        {
            var parentName = await db.Users.AsNoTracking()
                .Where(u => u.Id == parent.AuthorId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct) ?? "Unknown user";
            replyDto = ToReplyPreview(parent, parentName);
        }

        var mentionDtos = mentionIds
            .Select(id => new MessageMentionDto(
                id,
                teamMembers.FirstOrDefault(m => m.Id == id)?.DisplayName ?? "Unknown user"))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dto = ToDto(message, author, replyDto, mentionDtos);
        notifier.NotifyMessageCreated(channel.Channel.Id, dto);

        var actorName = author;
        foreach (var n in notifications)
        {
            notifier.NotifyUser(
                n.UserId,
                "notificationCreated",
                new NotificationDto(
                    n.Id,
                    n.Type,
                    n.TeamId,
                    channel.Team.Slug,
                    n.ChannelId,
                    channel.Channel.Name,
                    n.MessageId,
                    n.ActorId,
                    actorName,
                    n.BodyPreview,
                    n.CreatedAt,
                    n.ReadAt));
        }

        return AppResult<ChannelMessageDto>.Ok(dto);
    }

    public async Task<AppResult<MessageListResponse>> ListMessagesAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        ListMessagesQuery query,
        CancellationToken ct = default)
    {
        var access = await LoadTextChannelAsync(userId, teamSlug, channelName, ct);
        if (access.Failure is not null)
        {
            return AppResult<MessageListResponse>.Fail(access.Failure.Code, access.Failure.Message);
        }

        var channelId = access.Channel!.Channel.Id;
        var limit = Math.Clamp(query.Limit <= 0 ? ChatOptions.DefaultPageSize : query.Limit, 1, ChatOptions.MaxPageSize);

        var messages = db.ChannelMessages.AsNoTracking()
            .Where(m => m.ChannelId == channelId && m.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            if (!TryDecodeCursor(query.Cursor, out var cursorTime, out var cursorId))
            {
                return AppResult<MessageListResponse>.Fail(ErrorCodes.Validation, "Invalid cursor.");
            }

            // Strictly older than the cursor position in (created_at, id) order; stable under identical created_at.
            messages = messages.Where(m =>
                m.CreatedAt < cursorTime
                || (m.CreatedAt == cursorTime && m.Id.CompareTo(cursorId) < 0));
        }

        var rows = await messages
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > limit)
        {
            rows = rows.Take(limit).ToList();
            var last = rows[^1];
            next = EncodeCursor(last.CreatedAt, last.Id);
        }

        var messageIds = rows.Select(r => r.Id).ToList();
        var replyIds = rows.Where(r => r.ReplyToMessageId is not null).Select(r => r.ReplyToMessageId!.Value).Distinct().ToList();

        var parents = replyIds.Count == 0
            ? new Dictionary<Guid, ChannelMessageEntity>()
            : await db.ChannelMessages.AsNoTracking()
                .Where(m => replyIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, ct);

        var authorIds = rows.Select(r => r.AuthorId)
            .Concat(parents.Values.Select(p => p.AuthorId))
            .Distinct()
            .ToList();

        var names = await db.Users.AsNoTracking()
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var mentions = messageIds.Count == 0
            ? new List<MessageMentionEntity>()
            : await db.MessageMentions.AsNoTracking()
                .Where(m => messageIds.Contains(m.MessageId))
                .ToListAsync(ct);
        var mentionUserIds = mentions.Select(m => m.UserId).Distinct().ToList();
        var mentionNames = mentionUserIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => mentionUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var items = rows.Select(r =>
        {
            MessageReplyPreviewDto? reply = null;
            if (r.ReplyToMessageId is Guid pid && parents.TryGetValue(pid, out var parent))
            {
                reply = ToReplyPreview(parent, names.GetValueOrDefault(parent.AuthorId, "Deleted user"));
            }

            var msgMentions = mentions
                .Where(m => m.MessageId == r.Id)
                .Select(m => new MessageMentionDto(m.UserId, mentionNames.GetValueOrDefault(m.UserId, "Deleted user")))
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ToDto(r, names.GetValueOrDefault(r.AuthorId, "Deleted user"), reply, msgMentions);
        }).ToList();

        return AppResult<MessageListResponse>.Ok(new MessageListResponse(items, next));
    }

    public async Task<AppResult> DeleteMessageAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid messageId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var access = await LoadTextChannelAsync(userId, teamSlug, channelName, ct);
        if (access.Failure is not null)
        {
            return AppResult.Fail(access.Failure.Code, access.Failure.Message);
        }

        var channel = access.Channel!;
        var message = await db.ChannelMessages.FirstOrDefaultAsync(
            m => m.Id == messageId && m.ChannelId == channel.Channel.Id && m.DeletedAt == null,
            ct);
        if (message is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Message not found.");
        }

        var isAuthor = message.AuthorId == userId;
        var moderated = false;
        var allowed = isAuthor
            && authorizer.Authorize(ToContext(userId, AuthAction.DeleteOwnChannelMessage, channel, isAuthor: true)).Allowed;
        if (!allowed)
        {
            allowed = authorizer.Authorize(
                ToContext(userId, AuthAction.DeleteAnyChannelMessage, channel, isAuthor)).Allowed;
            moderated = allowed && !isAuthor;
        }

        if (!allowed)
        {
            return AppResult.Fail(ErrorCodes.Forbidden, "You cannot delete this message.");
        }

        // Scrub and tombstone in ONE write (the DB check forbids a deleted row that still carries a body).
        message.Body = "";
        message.DeletedAt = clock.UtcNow;
        message.DeletedBy = userId;

        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = userId,
            EventType = AuditEventType.ChannelMessageDelete,
            TeamId = channel.Team.Id,
            TargetType = "channel_message",
            TargetId = message.Id.ToString(),
            // Never the body: the content is scrubbed and must not survive in the audit trail either.
            DetailsJson = JsonSerializer.Serialize(new
            {
                channelId = channel.Channel.Id,
                authorId = message.AuthorId,
                moderated,
            }),
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await db.SaveChangesAsync(ct);

        notifier.NotifyMessageDeleted(channel.Channel.Id, new MessageDeletedEvent(channel.Channel.Id, message.Id));
        return AppResult.Ok();
    }

    public async Task<AppResult<PresenceListResponse>> ListPresenceAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        CancellationToken ct = default)
    {
        var access = await LoadTextChannelAsync(userId, teamSlug, channelName, ct);
        if (access.Failure is not null)
        {
            return AppResult<PresenceListResponse>.Fail(access.Failure.Code, access.Failure.Message);
        }

        var channelId = access.Channel!.Channel.Id;
        return AppResult<PresenceListResponse>.Ok(new PresenceListResponse(channelId, presence.Online(channelId)));
    }

    /// <summary>404 when the channel is not visible to the user; 400 when it is not a text channel.</summary>
    private async Task<ChannelLookup> LoadTextChannelAsync(
        Guid userId,
        string slug,
        string channelName,
        CancellationToken ct)
    {
        var channel = await LoadChannelAsync(userId, slug, channelName, ct);
        if (channel is null
            || !authorizer.Authorize(ToContext(userId, AuthAction.ViewChannel, channel, isAuthor: false)).Allowed)
        {
            return new ChannelLookup(null, new Failure(ErrorCodes.NotFound, "Channel not found."));
        }

        if (channel.Channel.Kind != ChannelKind.Text)
        {
            return new ChannelLookup(null, new Failure(ErrorCodes.WrongChannelKind, "Messages are only available in text channels."));
        }

        return new ChannelLookup(channel, null);
    }

    private async Task<ChannelAccess?> LoadChannelAsync(Guid userId, string slug, string channelName, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (team is null)
        {
            return null;
        }

        var member = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null)
        {
            return null;
        }

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(
            c => c.TeamId == team.Id && c.Name.ToLower() == channelName.ToLower(),
            ct);
        if (channel is null)
        {
            return null;
        }

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);

        return new ChannelAccess(team, member.Role, channel, cm is not null, cm is not null && !cm.ReadOnly, false);
    }

    private static AuthorizationContext ToContext(Guid userId, AuthAction action, ChannelAccess channel, bool isAuthor) =>
        new()
        {
            UserId = userId,
            Action = action,
            IsTeamMember = true,
            TeamRole = channel.Role,
            TeamArchived = channel.Team.ArchivedAt is not null,
            ChannelExists = true,
            ChannelVisibility = channel.Channel.Visibility,
            ChannelArchived = channel.Channel.ArchivedAt is not null,
            IsChannelMember = channel.IsChannelMember,
            ChannelCanWrite = channel.CanWrite,
            ChannelCanManage = channel.CanManage,
            IsMessageAuthor = isAuthor,
        };

    private static ChannelMessageDto ToDto(
        ChannelMessageEntity m,
        string authorDisplayName,
        MessageReplyPreviewDto? replyTo,
        IReadOnlyList<MessageMentionDto> mentions) =>
        new(m.Id, m.ChannelId, m.AuthorId, authorDisplayName, m.Body, m.CreatedAt, replyTo, mentions);

    private static MessageReplyPreviewDto ToReplyPreview(ChannelMessageEntity parent, string authorDisplayName)
    {
        var deleted = parent.DeletedAt is not null;
        var preview = deleted ? "" : Truncate(parent.Body.Trim(), 140);
        return new MessageReplyPreviewDto(parent.Id, parent.AuthorId, authorDisplayName, preview, deleted);
    }

    private static List<NotificationEntity> BuildNotifications(
        Guid actorId,
        Guid messageId,
        Guid teamId,
        Guid channelId,
        string bodyPreview,
        DateTimeOffset createdAt,
        Guid? parentAuthorId,
        HashSet<Guid> mentionIds)
    {
        var rows = new List<NotificationEntity>();
        var covered = new HashSet<Guid>();

        if (parentAuthorId is Guid replyUser && replyUser != actorId)
        {
            rows.Add(new NotificationEntity
            {
                Id = UuidV7.NewGuid(),
                UserId = replyUser,
                Type = NotificationType.Reply,
                TeamId = teamId,
                ChannelId = channelId,
                MessageId = messageId,
                ActorId = actorId,
                BodyPreview = bodyPreview,
                CreatedAt = createdAt,
            });
            covered.Add(replyUser);
        }

        foreach (var mentioned in mentionIds)
        {
            if (mentioned == actorId || covered.Contains(mentioned))
            {
                continue;
            }

            rows.Add(new NotificationEntity
            {
                Id = UuidV7.NewGuid(),
                UserId = mentioned,
                Type = NotificationType.Mention,
                TeamId = teamId,
                ChannelId = channelId,
                MessageId = messageId,
                ActorId = actorId,
                BodyPreview = bodyPreview,
                CreatedAt = createdAt,
            });
            covered.Add(mentioned);
        }

        return rows;
    }

    /// <summary>Unicode code points: matches Postgres <c>char_length</c> used by the DB check constraint.</summary>
    internal static int CodePointCount(string value)
    {
        var count = 0;
        foreach (var _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    internal static string EncodeCursor(DateTimeOffset createdAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdAt.UtcTicks}:{id:N}"));

    internal static bool TryDecodeCursor(string cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks
                || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record Failure(string Code, string Message);

    private sealed record ChannelLookup(ChannelAccess? Channel, Failure? Failure);

    private sealed record ChannelAccess(
        TeamEntity Team,
        MemberRole Role,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);
}
