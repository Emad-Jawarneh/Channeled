using Channeled.Application.Abstractions.Results;
using Channeled.Application.Chat;
using Channeled.Application.Files;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Files;

public sealed class FileReferenceService(ApplicationDbContext db, IAuthorizer authorizer) : IFileReferenceService
{
    public async Task<AppResult<IReadOnlyList<Guid>>> ValidateForMessagePostAsync(
        Guid userId,
        Guid teamId,
        IReadOnlyList<Guid>? fileIds,
        CancellationToken ct = default)
    {
        var normalized = NormalizeIds(fileIds);
        if (normalized.Count == 0)
        {
            return AppResult<IReadOnlyList<Guid>>.Ok([]);
        }

        if (normalized.Count > ChatOptions.MaxFileRefsPerMessage)
        {
            return AppResult<IReadOnlyList<Guid>>.Fail(
                ErrorCodes.Validation,
                $"At most {ChatOptions.MaxFileRefsPerMessage} file attachments are allowed per message.");
        }

        var rows = await db.Files.AsNoTracking()
            .Where(f => normalized.Contains(f.Id))
            .Select(f => new FileRow(f.Id, f.ChannelId, f.OwnerUserId, f.DeletedAt))
            .ToListAsync(ct);

        if (rows.Count != normalized.Count)
        {
            return AppResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Validation, "One or more files were not found.");
        }

        var channelIds = rows.Where(r => r.ChannelId is not null).Select(r => r.ChannelId!.Value).Distinct().ToList();
        var channels = channelIds.Count == 0
            ? new Dictionary<Guid, ChannelEntity>()
            : await db.Channels.AsNoTracking()
                .Where(c => channelIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

        foreach (var id in normalized)
        {
            var row = rows.First(r => r.Id == id);
            if (row.OwnerUserId is not null || row.ChannelId is null)
            {
                return AppResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Validation, "Only team channel files can be attached.");
            }

            if (!channels.TryGetValue(row.ChannelId.Value, out var channel) || channel.TeamId != teamId)
            {
                return AppResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Forbidden, "File is not in this team.");
            }

            if (row.DeletedAt is not null)
            {
                return AppResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Validation, "Trashed files cannot be attached.");
            }

            var access = await LoadChannelAccessAsync(userId, channel, ct);
            if (access is null || !CanBrowse(userId, access))
            {
                return AppResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Forbidden, "You do not have access to one or more files.");
            }
        }

        return AppResult<IReadOnlyList<Guid>>.Ok(normalized);
    }

    public async Task<AppResult<ResolveFilesResponse>> ResolveAsync(
        Guid userId,
        string teamSlug,
        ResolveFilesRequest request,
        CancellationToken ct = default)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == teamSlug, ct);
        if (team is null)
        {
            return AppResult<ResolveFilesResponse>.Fail(ErrorCodes.NotFound, "Team not found.");
        }

        var member = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null)
        {
            return AppResult<ResolveFilesResponse>.Fail(ErrorCodes.NotFound, "Team not found.");
        }

        var ids = NormalizeIds(request.FileIds);
        if (ids.Count == 0)
        {
            return AppResult<ResolveFilesResponse>.Ok(new ResolveFilesResponse([]));
        }

        var files = await db.Files.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new ResolveRow(
                f.Id,
                f.Name,
                f.SizeBytes,
                f.ContentType,
                f.ChannelId,
                f.FolderId,
                f.OwnerUserId,
                f.DeletedAt))
            .ToListAsync(ct);
        var byId = files.ToDictionary(f => f.Id);

        var channelIds = files.Where(f => f.ChannelId is not null).Select(f => f.ChannelId!.Value).Distinct().ToList();
        var channels = channelIds.Count == 0
            ? new Dictionary<Guid, (ChannelEntity Channel, ChannelAccess Access)>()
            : await LoadChannelsWithAccessAsync(userId, team.Id, channelIds, ct);

        var items = new List<ResolvedFileDto>(ids.Count);
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var file))
            {
                items.Add(Inaccessible(id));
                continue;
            }

            if (file.OwnerUserId is not null || file.ChannelId is null)
            {
                items.Add(Inaccessible(id));
                continue;
            }

            if (!channels.TryGetValue(file.ChannelId.Value, out var ctx))
            {
                items.Add(Inaccessible(id));
                continue;
            }

            var trashed = file.DeletedAt is not null;
            if (!CanBrowse(userId, ctx.Access))
            {
                items.Add(Inaccessible(id));
                continue;
            }

            if (trashed)
            {
                items.Add(new ResolvedFileDto(
                    id,
                    true,
                    file.Name,
                    file.SizeBytes,
                    file.ContentType,
                    ctx.Channel.Name,
                    file.FolderId,
                    "trashed"));
                continue;
            }

            items.Add(new ResolvedFileDto(
                id,
                true,
                file.Name,
                file.SizeBytes,
                file.ContentType,
                ctx.Channel.Name,
                file.FolderId,
                "live"));
        }

        return AppResult<ResolveFilesResponse>.Ok(new ResolveFilesResponse(items));
    }

    private static ResolvedFileDto Inaccessible(Guid id) =>
        new(id, false, null, null, null, null, null, "missing");

    private static List<Guid> NormalizeIds(IReadOnlyList<Guid>? fileIds)
    {
        if (fileIds is null || fileIds.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<Guid>();
        var list = new List<Guid>();
        foreach (var id in fileIds)
        {
            if (id == Guid.Empty || !seen.Add(id))
            {
                continue;
            }

            list.Add(id);
        }

        return list;
    }

    private bool CanBrowse(Guid userId, ChannelAccess access) =>
        authorizer.Authorize(new AuthorizationContext
        {
            UserId = userId,
            Action = AuthAction.BrowseFiles,
            IsTeamMember = true,
            TeamRole = access.Role,
            TeamArchived = access.Team.ArchivedAt is not null,
            ChannelExists = true,
            ChannelVisibility = access.Channel.Visibility,
            ChannelArchived = access.Channel.ArchivedAt is not null,
            IsChannelMember = access.IsChannelMember,
            ChannelCanWrite = access.CanWrite,
            ChannelCanManage = access.CanManage,
        }).Allowed;

    private async Task<ChannelAccess?> LoadChannelAccessAsync(Guid userId, ChannelEntity channel, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == channel.TeamId, ct);
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

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);

        return new ChannelAccess(team, member.Role, channel, cm is not null, cm is not null && !cm.ReadOnly, false);
    }

    private async Task<Dictionary<Guid, (ChannelEntity Channel, ChannelAccess Access)>> LoadChannelsWithAccessAsync(
        Guid userId,
        Guid teamId,
        List<Guid> channelIds,
        CancellationToken ct)
    {
        var channels = await db.Channels.AsNoTracking()
            .Where(c => channelIds.Contains(c.Id) && c.TeamId == teamId)
            .ToListAsync(ct);
        var team = await db.Teams.AsNoTracking().FirstAsync(t => t.Id == teamId, ct);
        var member = await db.TeamMembers.AsNoTracking()
            .FirstAsync(m => m.TeamId == teamId && m.UserId == userId, ct);
        var memberships = await db.ChannelMembers.AsNoTracking()
            .Where(m => channelIds.Contains(m.ChannelId) && m.UserId == userId)
            .ToDictionaryAsync(m => m.ChannelId, ct);

        var map = new Dictionary<Guid, (ChannelEntity, ChannelAccess)>();
        foreach (var channel in channels)
        {
            memberships.TryGetValue(channel.Id, out var cm);
            var access = new ChannelAccess(
                team,
                member.Role,
                channel,
                cm is not null,
                cm is not null && !cm.ReadOnly,
                false);
            map[channel.Id] = (channel, access);
        }

        return map;
    }

    private sealed record FileRow(Guid Id, Guid? ChannelId, Guid? OwnerUserId, DateTimeOffset? DeletedAt);

    private sealed record ResolveRow(
        Guid Id,
        string Name,
        long SizeBytes,
        string ContentType,
        Guid? ChannelId,
        Guid? FolderId,
        Guid? OwnerUserId,
        DateTimeOffset? DeletedAt);

    private sealed record ChannelAccess(
        TeamEntity Team,
        MemberRole Role,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);
}
