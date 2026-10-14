using System.Text.Json;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;
using Channeled.Application.Notifications;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Notifications;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Notifications;
using Channeled.Infrastructure.PublicLinks;
using Channeled.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Channeled.Infrastructure.Teams;

/// <summary>Team join link, join requests, leave, settings and channel visibility.</summary>
public sealed partial class TeamService
{
    /// <summary>After a reject/withdraw the same user cannot request again until this elapses.</summary>
    public static readonly TimeSpan JoinCooldown = TimeSpan.FromHours(24);

    /// <summary>
    /// RequestToJoin → OpenJoin approves every pending request in ONE transaction. If more than this many are pending
    /// the conversion fails (nothing is applied) and the Owner must clear the queue first.
    /// Configurable via <see cref="TeamJoinOptions.MaxOpenJoinPendingApprove"/> (default 500).
    /// </summary>
    private int MaxOpenJoinPendingApprove => Math.Max(1, joinOptions.Value.MaxOpenJoinPendingApprove);

    /// <summary>
    /// Wall-clock budget for the OpenJoin conversion: enforced both with a linked CancellationToken and a Postgres
    /// <c>SET LOCAL statement_timeout</c> (<see cref="SetStatementTimeoutSql"/> must be kept in sync).
    /// </summary>
    public static readonly TimeSpan OpenJoinConversionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Per-user cap on simultaneously pending join requests across all teams (abuse / spam guard).</summary>
    public const int MaxPendingJoinRequestsPerUser = 10;

    private const int MaxBulkGrants = 500;
    private const string InvalidJoinLinkMessage = "Join link is invalid or expired.";
    private const string SetLockTimeoutSql = "SET LOCAL lock_timeout = '5s'";
    private const string SetStatementTimeoutSql = "SET LOCAL statement_timeout = '30s'";

    // ------------------------------------------------------------------ settings

    public async Task<AppResult<UpdateTeamSettingsResult>> UpdateTeamSettingsAsync(
        Guid userId,
        string slug,
        UpdateTeamSettingsRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.EditTeamSettings, loaded)).Allowed)
        {
            return NotFoundTeam<UpdateTeamSettingsResult>();
        }

        var team = loaded.Team;

        string? newName = null;
        if (request.Name is not null)
        {
            newName = request.Name.Trim();
            if (newName.Length == 0 || newName.Length > 200)
            {
                return AppResult<UpdateTeamSettingsResult>.Fail(ErrorCodes.Validation, "Team name is required (max 200 characters).");
            }
        }

        string? newDescription = null;
        var setDescription = request.Description is not null;
        if (setDescription)
        {
            newDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description!.Trim();
            if (newDescription is { Length: > 2000 })
            {
                return AppResult<UpdateTeamSettingsResult>.Fail(ErrorCodes.Validation, "Description is too long (max 2000 characters).");
            }
        }

        if (request.DefaultJoinRole is { } role && role is not (MemberRole.Viewer or MemberRole.Member))
        {
            return AppResult<UpdateTeamSettingsResult>.Fail(ErrorCodes.Validation, "Default join role must be Viewer or Member.");
        }

        var wantsJoinMode = request.JoinMode is { } jm && jm != team.JoinMode;
        var wantsDefaultRole = request.DefaultJoinRole is { } dr && dr != team.DefaultJoinRole;

        if ((wantsJoinMode || wantsDefaultRole)
            && !authorizer.Authorize(BaseContext(userId, AuthAction.ChangeTeamJoinMode, loaded)).Allowed)
        {
            return AppResult<UpdateTeamSettingsResult>.Fail(ErrorCodes.Forbidden, "Only the Owner can change join settings.");
        }

        if (wantsJoinMode && request.JoinMode == TeamJoinMode.OpenJoin && !request.ConfirmOpenJoinApprovePending)
        {
            return AppResult<UpdateTeamSettingsResult>.Fail(
                ErrorCodes.OpenJoinConfirmationRequired,
                "Switching to Open Join approves all pending join requests. Set confirmOpenJoinApprovePending=true to continue.");
        }

        if (!wantsJoinMode && !wantsDefaultRole)
        {
            if (newName is not null)
            {
                team.Name = newName;
            }

            if (setDescription)
            {
                team.Description = newDescription;
            }

            await db.SaveChangesAsync(ct);
            return AppResult<UpdateTeamSettingsResult>.Ok(new UpdateTeamSettingsResult(ToTeamDto(team, loaded.Role), null));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OpenJoinConversionTimeout);
        var token = timeout.Token;
        int? approvedCount = null;

        try
        {
            await using var tx = await BeginTeamTxAsync(token);
            var locked = await LockTeamAsync(team.Id, token);
            if (locked is null)
            {
                return NotFoundTeam<UpdateTeamSettingsResult>();
            }

            var stillOwner = await db.TeamMembers.AsNoTracking()
                .AnyAsync(m => m.TeamId == locked.Id && m.UserId == userId && m.Role == MemberRole.Owner, token);
            if (!stillOwner)
            {
                return AppResult<UpdateTeamSettingsResult>.Fail(ErrorCodes.Forbidden, "Only the Owner can change join settings.");
            }

            if (newName is not null)
            {
                locked.Name = newName;
            }

            if (setDescription)
            {
                locked.Description = newDescription;
            }

            if (request.DefaultJoinRole is { } newRole && newRole != locked.DefaultJoinRole)
            {
                locked.DefaultJoinRole = newRole;
            }

            var converted = false;
            if (request.JoinMode is { } mode && mode != locked.JoinMode)
            {
                var previous = locked.JoinMode;
                if (mode == TeamJoinMode.OpenJoin)
                {
                    var conversion = await ApproveAllPendingForOpenJoinAsync(userId, locked, context, token);
                    if (!conversion.Succeeded)
                    {
                        return AppResult<UpdateTeamSettingsResult>.Fail(conversion.Code!, conversion.Message!);
                    }

                    approvedCount = conversion.Value;
                    converted = true;
                }

                locked.JoinMode = mode;
                await WriteAuditAsync(
                    userId,
                    AuditEventType.TeamJoinModeChange,
                    "team",
                    locked.Id.ToString(),
                    locked.Id,
                    context,
                    token,
                    JsonSerializer.Serialize(new { from = (int)previous, to = (int)mode, approvedCount }));
            }

            await db.SaveChangesAsync(token);
            if (converted)
            {
                // No pending requests remain: clears every TeamJoinRequest notification for this team.
                await ReconcileJoinNotificationsAsync(locked.Id, token);
            }

            await tx.CommitAsync(token);
            return AppResult<UpdateTeamSettingsResult>.Ok(new UpdateTeamSettingsResult(ToTeamDto(locked, loaded.Role), approvedCount));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AppResult<UpdateTeamSettingsResult>.Fail(
                ErrorCodes.Validation,
                "Updating team settings timed out; nothing was changed. Try again later.");
        }
        catch (Exception ex) when (IsLockTimeout(ex))
        {
            return AppResult<UpdateTeamSettingsResult>.Fail(ErrorCodes.LockTimeout, "Team is busy; retry shortly.");
        }
    }

    /// <summary>Runs inside the team-locked transaction. Does not SaveChanges.</summary>
    private async Task<AppResult<int>> ApproveAllPendingForOpenJoinAsync(
        Guid actorId,
        TeamEntity team,
        RequestContext context,
        CancellationToken ct)
    {
        var pending = await db.TeamJoinRequests
            .Where(r => r.TeamId == team.Id && r.Status == TeamJoinRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(MaxOpenJoinPendingApprove + 1)
            .ToListAsync(ct);

        if (pending.Count > MaxOpenJoinPendingApprove)
        {
            return AppResult<int>.Fail(
                ErrorCodes.OpenJoinTooManyPending,
                $"More than {MaxOpenJoinPendingApprove} join requests are pending. Approve or reject some before switching to Open Join.");
        }

        var userIds = pending.Select(r => r.UserId).ToList();
        var activeUsers = (await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id) && u.Status == "active")
                .Select(u => u.Id)
                .ToListAsync(ct))
            .ToHashSet();
        var alreadyMembers = (await db.TeamMembers.AsNoTracking()
                .Where(m => m.TeamId == team.Id && userIds.Contains(m.UserId))
                .Select(m => m.UserId)
                .ToListAsync(ct))
            .ToHashSet();

        var now = clock.UtcNow;
        var approved = 0;
        foreach (var row in pending)
        {
            row.DecidedAt = now;
            row.DecidedByUserId = actorId;

            if (!activeUsers.Contains(row.UserId))
            {
                // Deleted or disabled account: close the request without granting membership.
                row.Status = TeamJoinRequestStatus.Rejected;
                continue;
            }

            if (!alreadyMembers.Contains(row.UserId))
            {
                db.TeamMembers.Add(new TeamMemberEntity
                {
                    TeamId = team.Id,
                    UserId = row.UserId,
                    Role = team.DefaultJoinRole,
                    JoinedAt = now,
                });
            }

            row.Status = TeamJoinRequestStatus.Approved;
            approved++;
            await WriteAuditAsync(
                actorId,
                AuditEventType.TeamJoinRequestApprove,
                "user",
                row.UserId.ToString(),
                team.Id,
                context,
                ct,
                JsonSerializer.Serialize(new { requestId = row.Id, role = (int)team.DefaultJoinRole, reason = "open_join_conversion" }));
        }

        return AppResult<int>.Ok(approved);
    }

    // ------------------------------------------------------------------ join link (Admin+)

    public async Task<AppResult<TeamJoinLinkDto>> GetJoinLinkAsync(Guid userId, string slug, CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ManageTeamJoinLink, loaded)).Allowed)
        {
            return NotFoundTeam<TeamJoinLinkDto>();
        }

        return ToJoinLinkDto(loaded.Team);
    }

    public async Task<AppResult<TeamJoinLinkDto>> EnsureJoinLinkAsync(
        Guid userId,
        string slug,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ManageTeamJoinLink, loaded)).Allowed)
        {
            return NotFoundTeam<TeamJoinLinkDto>();
        }

        if (!string.IsNullOrEmpty(loaded.Team.JoinTokenCiphertext))
        {
            return ToJoinLinkDto(loaded.Team);
        }

        return await RotateCoreAsync(userId, loaded, null, context, ct);
    }

    public async Task<AppResult<TeamJoinLinkDto>> RotateJoinLinkAsync(
        Guid userId,
        string slug,
        RotateTeamJoinLinkRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ManageTeamJoinLink, loaded)).Allowed)
        {
            return NotFoundTeam<TeamJoinLinkDto>();
        }

        if (request?.ExpiresAt is { } expires && expires <= clock.UtcNow)
        {
            return AppResult<TeamJoinLinkDto>.Fail(ErrorCodes.Validation, "Expiry must be in the future.");
        }

        return await RotateCoreAsync(userId, loaded, request?.ExpiresAt, context, ct);
    }

    private async Task<AppResult<TeamJoinLinkDto>> RotateCoreAsync(
        Guid userId,
        TeamMembership loaded,
        DateTimeOffset? expiresAt,
        RequestContext context,
        CancellationToken ct)
    {
        try
        {
            await using var tx = await BeginTeamTxAsync(ct);
            var team = await LockTeamAsync(loaded.Team.Id, ct);
            if (team is null)
            {
                return NotFoundTeam<TeamJoinLinkDto>();
            }

            // Old hash is overwritten in the same row update: the previous link stops working immediately on commit.
            var oldPrefix = team.JoinTokenPrefix;
            AssignNewJoinToken(team, expiresAt);
            await WriteAuditAsync(
                userId,
                AuditEventType.TeamJoinLinkRotated,
                "team",
                team.Id.ToString(),
                team.Id,
                context,
                ct,
                JsonSerializer.Serialize(new { oldPrefix, newPrefix = team.JoinTokenPrefix, expiresAt }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ToJoinLinkDto(team);
        }
        catch (Exception ex) when (IsLockTimeout(ex))
        {
            return AppResult<TeamJoinLinkDto>.Fail(ErrorCodes.LockTimeout, "Team is busy; retry shortly.");
        }
    }

    private AppResult<TeamJoinLinkDto> ToJoinLinkDto(TeamEntity team)
    {
        if (string.IsNullOrEmpty(team.JoinTokenCiphertext)
            || !joinLinkProtector.TryUnprotect(team.JoinTokenCiphertext, out var raw))
        {
            return AppResult<TeamJoinLinkDto>.Fail(
                ErrorCodes.JoinLinkUnavailable,
                "The join link is unavailable. Rotate the link to create a new one.");
        }

        return AppResult<TeamJoinLinkDto>.Ok(new TeamJoinLinkDto(
            raw,
            team.JoinTokenPrefix ?? (raw.Length >= 8 ? raw[..8] : raw),
            team.JoinTokenExpiresAt,
            $"/join/{raw}"));
    }

    /// <summary>Generates a new raw token and stores hash + prefix + ciphertext on the entity.</summary>
    private string AssignNewJoinToken(TeamEntity team, DateTimeOffset? expiresAt)
    {
        var raw = TokenHasher.CreateRawToken();
        team.JoinTokenHash = TokenHasher.Hash(raw);
        team.JoinTokenPrefix = raw[..8];
        team.JoinTokenCiphertext = joinLinkProtector.Protect(raw);
        team.JoinTokenExpiresAt = expiresAt;
        return raw;
    }

    // ------------------------------------------------------------------ public join by token

    public async Task<AppResult<TeamJoinInfoDto>> GetJoinInfoAsync(Guid? userId, string token, CancellationToken ct = default)
    {
        var team = await FindTeamByJoinTokenAsync(token, ct);
        if (team is null)
        {
            return AppResult<TeamJoinInfoDto>.Fail(ErrorCodes.JoinLinkInvalid, InvalidJoinLinkMessage);
        }

        if (userId is not Guid uid)
        {
            // Anonymous: nothing beyond name + mode.
            return AppResult<TeamJoinInfoDto>.Ok(new TeamJoinInfoDto(team.Name, team.JoinMode, false, null));
        }

        var isMember = await db.TeamMembers.AsNoTracking().AnyAsync(m => m.TeamId == team.Id && m.UserId == uid, ct);
        Guid? pendingId = null;
        if (!isMember)
        {
            pendingId = await db.TeamJoinRequests.AsNoTracking()
                .Where(r => r.TeamId == team.Id && r.UserId == uid && r.Status == TeamJoinRequestStatus.Pending)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);
        }

        return AppResult<TeamJoinInfoDto>.Ok(new TeamJoinInfoDto(team.Name, team.JoinMode, isMember, pendingId));
    }

    public async Task<AppResult<TeamJoinResultDto>> JoinAsync(
        Guid userId,
        string token,
        RequestContext context,
        CancellationToken ct = default)
    {
        var hash = HashJoinToken(token);
        var candidate = await FindTeamByJoinHashAsync(hash, ct);
        if (candidate is null)
        {
            return AppResult<TeamJoinResultDto>.Fail(ErrorCodes.JoinLinkInvalid, InvalidJoinLinkMessage);
        }

        var teamId = candidate.Id;
        IReadOnlyList<NotificationEntity> fanOut = [];
        string slug;
        TeamJoinResultDto result;

        try
        {
            await using var tx = await BeginTeamTxAsync(ct);
            // FOR UPDATE: serializes with OpenJoin conversion, link rotation and request decisions.
            var team = await LockTeamAsync(teamId, ct);
            if (team is null
                || team.ArchivedAt is not null
                || team.JoinTokenHash != hash
                || (team.JoinTokenExpiresAt is { } exp && exp <= clock.UtcNow))
            {
                // Rotated/expired between lookup and lock.
                return AppResult<TeamJoinResultDto>.Fail(ErrorCodes.JoinLinkInvalid, InvalidJoinLinkMessage);
            }

            slug = team.Slug;
            var now = clock.UtcNow;

            var existingMember = await db.TeamMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == userId, ct);
            if (existingMember is not null)
            {
                return AppResult<TeamJoinResultDto>.Ok(new TeamJoinResultDto(slug, existingMember.Role, null, true));
            }

            var lastBlocked = await db.TeamJoinRequests.AsNoTracking()
                .Where(r => r.TeamId == teamId
                            && r.UserId == userId
                            && (r.Status == TeamJoinRequestStatus.Rejected || r.Status == TeamJoinRequestStatus.Withdrawn))
                .OrderByDescending(r => r.DecidedAt)
                .FirstOrDefaultAsync(ct);
            if (lastBlocked?.CooldownUntil is { } until && until > now)
            {
                return AppResult<TeamJoinResultDto>.Fail(
                    ErrorCodes.JoinCooldown,
                    $"You can request to join this team again after {until.UtcDateTime:yyyy-MM-dd HH:mm} UTC.");
            }

            if (team.JoinMode == TeamJoinMode.OpenJoin)
            {
                db.TeamMembers.Add(new TeamMemberEntity
                {
                    TeamId = teamId,
                    UserId = userId,
                    Role = team.DefaultJoinRole,
                    JoinedAt = now,
                });
                await WriteAuditAsync(
                    userId,
                    AuditEventType.TeamJoinRequestApprove,
                    "user",
                    userId.ToString(),
                    teamId,
                    context,
                    ct,
                    JsonSerializer.Serialize(new { role = (int)team.DefaultJoinRole, reason = "open_join", tokenPrefix = team.JoinTokenPrefix }));
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return AppResult<TeamJoinResultDto>.Ok(new TeamJoinResultDto(slug, team.DefaultJoinRole, null, true));
            }

            // RequestToJoin
            var pending = await db.TeamJoinRequests
                .FirstOrDefaultAsync(r => r.TeamId == teamId && r.UserId == userId && r.Status == TeamJoinRequestStatus.Pending, ct);
            if (pending is not null)
            {
                return AppResult<TeamJoinResultDto>.Ok(new TeamJoinResultDto(slug, null, pending.Id, false));
            }

            var pendingAcrossTeams = await db.TeamJoinRequests.AsNoTracking()
                .CountAsync(r => r.UserId == userId && r.Status == TeamJoinRequestStatus.Pending, ct);
            if (pendingAcrossTeams >= MaxPendingJoinRequestsPerUser)
            {
                return AppResult<TeamJoinResultDto>.Fail(
                    ErrorCodes.JoinRateLimited,
                    "You have too many pending join requests. Wait for a decision or withdraw one.");
            }

            var request = new TeamJoinRequestEntity
            {
                Id = UuidV7.NewGuid(),
                TeamId = teamId,
                UserId = userId,
                Status = TeamJoinRequestStatus.Pending,
                CreatedAt = now,
            };
            db.TeamJoinRequests.Add(request);
            await WriteAuditAsync(
                userId,
                AuditEventType.TeamJoinRequestCreate,
                "team_join_request",
                request.Id.ToString(),
                teamId,
                context,
                ct);
            await db.SaveChangesAsync(ct);

            fanOut = await FanOutJoinRequestNotificationsAsync(team, request, ct);
            await tx.CommitAsync(ct);
            result = new TeamJoinResultDto(slug, null, request.Id, false);
        }
        catch (Exception ex) when (IsLockTimeout(ex))
        {
            return AppResult<TeamJoinResultDto>.Fail(ErrorCodes.LockTimeout, "Team is busy; retry shortly.");
        }

        await PublishJoinNotificationsAsync(slug, fanOut, ct);
        return AppResult<TeamJoinResultDto>.Ok(result);
    }

    // ------------------------------------------------------------------ join requests (Admin+ / requester)

    public async Task<AppResult<IReadOnlyList<TeamJoinRequestDto>>> ListJoinRequestsAsync(
        Guid userId,
        string slug,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ApproveTeamJoinRequest, loaded)).Allowed)
        {
            return NotFoundTeam<IReadOnlyList<TeamJoinRequestDto>>();
        }

        var rows = await db.TeamJoinRequests.AsNoTracking()
            .Where(r => r.TeamId == loaded.Team.Id && r.Status == TeamJoinRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        var userIds = rows.Select(r => r.UserId).ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        IReadOnlyList<TeamJoinRequestDto> dtos = rows.Select(r =>
        {
            users.TryGetValue(r.UserId, out var u);
            return new TeamJoinRequestDto(
                r.Id,
                r.UserId,
                u?.Email ?? "",
                u?.DisplayName ?? "",
                u?.UsernameSlug ?? "",
                u?.AvatarUpdatedAt?.ToUnixTimeMilliseconds(),
                r.Status,
                r.CreatedAt);
        }).ToList();
        return AppResult<IReadOnlyList<TeamJoinRequestDto>>.Ok(dtos);
    }

    public Task<AppResult> ApproveJoinRequestAsync(
        Guid userId,
        string slug,
        Guid requestId,
        ApproveTeamJoinRequestRequest? request,
        RequestContext context,
        CancellationToken ct = default) =>
        DecideJoinRequestAsync(userId, slug, requestId, approve: true, request?.Role, context, ct);

    public Task<AppResult> RejectJoinRequestAsync(
        Guid userId,
        string slug,
        Guid requestId,
        RequestContext context,
        CancellationToken ct = default) =>
        DecideJoinRequestAsync(userId, slug, requestId, approve: false, null, context, ct);

    private async Task<AppResult> DecideJoinRequestAsync(
        Guid userId,
        string slug,
        Guid requestId,
        bool approve,
        MemberRole? requestedRole,
        RequestContext context,
        CancellationToken ct)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ApproveTeamJoinRequest, loaded)).Allowed)
        {
            return NotFoundTeam();
        }

        if (approve && requestedRole is { } r && r is not (MemberRole.Viewer or MemberRole.Member))
        {
            return AppResult.Fail(ErrorCodes.Validation, "Join requests can only be approved as Viewer or Member.");
        }

        IReadOnlyList<NotificationEntity> refreshed;
        Guid requesterId;
        try
        {
            await using var tx = await BeginTeamTxAsync(ct);
            var team = await LockTeamAsync(loaded.Team.Id, ct);
            if (team is null)
            {
                return NotFoundTeam();
            }

            var row = await db.TeamJoinRequests.FirstOrDefaultAsync(x => x.Id == requestId && x.TeamId == team.Id, ct);
            if (row is null || row.Status != TeamJoinRequestStatus.Pending)
            {
                return AppResult.Fail(ErrorCodes.NotFound, "Join request not found.");
            }

            requesterId = row.UserId;
            var now = clock.UtcNow;
            row.DecidedAt = now;
            row.DecidedByUserId = userId;

            if (approve)
            {
                var active = await db.Users.AsNoTracking().AnyAsync(u => u.Id == row.UserId && u.Status == "active", ct);
                if (!active)
                {
                    return AppResult.Fail(ErrorCodes.Conflict, "The requesting user is no longer active.");
                }

                var role = requestedRole ?? team.DefaultJoinRole;
                var isMember = await db.TeamMembers.AnyAsync(m => m.TeamId == team.Id && m.UserId == row.UserId, ct);
                if (!isMember)
                {
                    db.TeamMembers.Add(new TeamMemberEntity
                    {
                        TeamId = team.Id,
                        UserId = row.UserId,
                        Role = role,
                        JoinedAt = now,
                    });
                }

                row.Status = TeamJoinRequestStatus.Approved;
                await WriteAuditAsync(
                    userId,
                    AuditEventType.TeamJoinRequestApprove,
                    "user",
                    row.UserId.ToString(),
                    team.Id,
                    context,
                    ct,
                    JsonSerializer.Serialize(new { requestId = row.Id, role = (int)role }));
            }
            else
            {
                row.Status = TeamJoinRequestStatus.Rejected;
                row.CooldownUntil = now + JoinCooldown;
                await WriteAuditAsync(
                    userId,
                    AuditEventType.TeamJoinRequestReject,
                    "user",
                    row.UserId.ToString(),
                    team.Id,
                    context,
                    ct,
                    JsonSerializer.Serialize(new { requestId = row.Id, cooldownUntil = row.CooldownUntil }));
            }

            await db.SaveChangesAsync(ct);
            refreshed = await ReconcileJoinNotificationsAsync(team.Id, ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsLockTimeout(ex))
        {
            return AppResult.Fail(ErrorCodes.LockTimeout, "Team is busy; retry shortly.");
        }

        if (approve)
        {
            // Authorization state changed for the new member (e.g. clients that cached 404s); refresh their sockets.
            authStateChanged.NotifyUser(requesterId);
        }

        channelNotifier.NotifyUser(
            requesterId,
            "teamJoinRequestDecided",
            new { teamSlug = loaded.Team.Slug, approved = approve });
        await PublishJoinNotificationsAsync(loaded.Team.Slug, refreshed, ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> WithdrawJoinRequestAsync(
        Guid userId,
        Guid requestId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var probe = await db.TeamJoinRequests.AsNoTracking()
            .Where(r => r.Id == requestId && r.UserId == userId)
            .Select(r => new { r.TeamId })
            .FirstOrDefaultAsync(ct);
        if (probe is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Join request not found.");
        }

        IReadOnlyList<NotificationEntity> refreshed;
        string slug;
        try
        {
            await using var tx = await BeginTeamTxAsync(ct);
            var team = await LockTeamAsync(probe.TeamId, ct);
            if (team is null)
            {
                return AppResult.Fail(ErrorCodes.NotFound, "Join request not found.");
            }

            slug = team.Slug;
            var row = await db.TeamJoinRequests.FirstOrDefaultAsync(
                x => x.Id == requestId && x.UserId == userId && x.TeamId == team.Id, ct);
            if (row is null || row.Status != TeamJoinRequestStatus.Pending)
            {
                return AppResult.Fail(ErrorCodes.NotFound, "Join request not found.");
            }

            var now = clock.UtcNow;
            row.Status = TeamJoinRequestStatus.Withdrawn;
            row.DecidedAt = now;
            row.DecidedByUserId = userId;
            row.CooldownUntil = now + JoinCooldown;
            await WriteAuditAsync(
                userId,
                AuditEventType.TeamJoinRequestWithdraw,
                "team_join_request",
                row.Id.ToString(),
                team.Id,
                context,
                ct);
            await db.SaveChangesAsync(ct);
            refreshed = await ReconcileJoinNotificationsAsync(team.Id, ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsLockTimeout(ex))
        {
            return AppResult.Fail(ErrorCodes.LockTimeout, "Team is busy; retry shortly.");
        }

        await PublishJoinNotificationsAsync(slug, refreshed, ct);
        return AppResult.Ok();
    }

    // ------------------------------------------------------------------ leave

    public async Task<AppResult> LeaveTeamAsync(Guid userId, string slug, RequestContext context, CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.LeaveTeam, loaded)).Allowed)
        {
            return NotFoundTeam();
        }

        if (loaded.Role == MemberRole.Owner)
        {
            return AppResult.Fail(ErrorCodes.OwnerMustTransfer, "Transfer ownership before leaving the team.");
        }

        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == loaded.Team.Id && m.UserId == userId, ct);
        if (member is null)
        {
            return NotFoundTeam();
        }

        IReadOnlyList<NotificationEntity> refreshed;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.TeamMembers.Remove(member);
            await WriteAuditAsync(
                userId,
                AuditEventType.MemberLeave,
                "user",
                userId.ToString(),
                loaded.Team.Id,
                context,
                ct,
                JsonSerializer.Serialize(new { role = (int)loaded.Role }));
            await db.SaveChangesAsync(ct);
            // Content (files, messages) stays; grants, notifications and pending requests go.
            refreshed = await RemoveMembershipSideEffectsAsync(loaded.Team.Id, userId, ct);
            await tx.CommitAsync(ct);
        }

        authStateChanged.NotifyUser(userId);
        await PublishJoinNotificationsAsync(loaded.Team.Slug, refreshed, ct);
        return AppResult.Ok();
    }

    // ------------------------------------------------------------------ channel visibility

    public async Task<AppResult<SetChannelVisibilityResult>> SetChannelVisibilityAsync(
        Guid userId,
        string slug,
        string channelName,
        SetChannelVisibilityRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ManageChannelMembership, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel<SetChannelVisibilityResult>();
        }

        if (!Enum.IsDefined(request.Visibility))
        {
            return AppResult<SetChannelVisibilityResult>.Fail(ErrorCodes.Validation, "Unknown visibility.");
        }

        var channel = loaded.Channel;
        var teamId = loaded.TeamMembership.Team.Id;

        if (request.Visibility == channel.Visibility)
        {
            return AppResult<SetChannelVisibilityResult>.Ok(new SetChannelVisibilityResult(ToChannelDto(channel), 0, 0));
        }

        List<Guid> losers = [];
        var linksRevoked = 0;
        var from = channel.Visibility;

        try
        {
            await using var tx = await BeginTeamTxAsync(ct);
            var lockResult = await channelWriteGuard.LockForUpdateAsync(channel.Id, ct);
            if (!lockResult.Succeeded)
            {
                return AppResult<SetChannelVisibilityResult>.Fail(lockResult.Code!, lockResult.Message!);
            }

            await db.Entry(channel).ReloadAsync(ct);
            if (channel.Visibility == request.Visibility)
            {
                return AppResult<SetChannelVisibilityResult>.Ok(new SetChannelVisibilityResult(ToChannelDto(channel), 0, 0));
            }

            from = channel.Visibility;
            var channelId = channel.Id;

            if (request.Visibility == ChannelVisibility.Restricted)
            {
                // Team members below Admin without an explicit grant lose access.
                losers = await db.TeamMembers.AsNoTracking()
                    .Where(m => m.TeamId == teamId
                                && m.Role < MemberRole.Admin
                                && !db.ChannelMembers.Any(g => g.ChannelId == channelId && g.UserId == m.UserId))
                    .Select(m => m.UserId)
                    .ToListAsync(ct);

                if (losers.Count > 0)
                {
                    // Share links whose creators can no longer see the channel are revoked in this transaction.
                    linksRevoked = await ShareLinkRevoker.RevokeAsync(
                        db,
                        clock,
                        l => l.ChannelId == channelId && losers.Contains(l.CreatedBy),
                        ShareLinkRevoker.ReasonChannelRestricted,
                        userId,
                        context,
                        ct);
                }
            }

            channel.Visibility = request.Visibility;
            await WriteAuditAsync(
                userId,
                AuditEventType.ChannelVisibilityChange,
                "channel",
                channel.Id.ToString(),
                teamId,
                context,
                ct,
                JsonSerializer.Serialize(new
                {
                    from = (int)from,
                    to = (int)request.Visibility,
                    membersLosingAccess = losers.Count,
                    shareLinksRevoked = linksRevoked,
                }));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsLockTimeout(ex))
        {
            return AppResult<SetChannelVisibilityResult>.Fail(ErrorCodes.LockTimeout, "Channel is busy; retry shortly.");
        }

        foreach (var loserId in losers)
        {
            authStateChanged.NotifyUser(loserId);
        }

        channelNotifier.NotifyChannelVisibilityChanged(channel.Id);
        return AppResult<SetChannelVisibilityResult>.Ok(
            new SetChannelVisibilityResult(ToChannelDto(channel), losers.Count, linksRevoked));
    }

    // ------------------------------------------------------------------ helpers: locking / tx

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTeamTxAsync(CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(SetLockTimeoutSql, ct);
        await db.Database.ExecuteSqlRawAsync(SetStatementTimeoutSql, ct);
        return tx;
    }

    /// <summary><c>SELECT … FOR UPDATE</c> on the team row, then reloads the tracked entity so state is post-lock.</summary>
    private async Task<TeamEntity?> LockTeamAsync(Guid teamId, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQuery<Guid>($"""SELECT "Id" AS "Value" FROM teams WHERE "Id" = {teamId} FOR UPDATE""")
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return null;
        }

        var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == teamId, ct);
        if (team is null)
        {
            return null;
        }

        await db.Entry(team).ReloadAsync(ct);
        return team;
    }

    private static bool IsLockTimeout(Exception ex) =>
        ex is PostgresException { SqlState: PostgresErrorCodes.QueryCanceled or PostgresErrorCodes.LockNotAvailable }
        || ex is { InnerException: PostgresException { SqlState: PostgresErrorCodes.QueryCanceled or PostgresErrorCodes.LockNotAvailable } };

    // ------------------------------------------------------------------ helpers: join tokens

    private static string HashJoinToken(string? token)
    {
        // Always hash (even for garbage input) so timing does not depend on token shape.
        var value = token ?? "";
        if (value.Length > 256)
        {
            value = value[..256];
        }

        return TokenHasher.Hash(value);
    }

    private async Task<TeamEntity?> FindTeamByJoinTokenAsync(string? token, CancellationToken ct) =>
        await FindTeamByJoinHashAsync(HashJoinToken(token), ct);

    private async Task<TeamEntity?> FindTeamByJoinHashAsync(string hash, CancellationToken ct)
    {
        var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.JoinTokenHash == hash, ct);
        if (team is null
            || team.ArchivedAt is not null
            || (team.JoinTokenExpiresAt is { } expires && expires <= clock.UtcNow))
        {
            return null;
        }

        return team;
    }

    // ------------------------------------------------------------------ helpers: membership cleanup

    private async Task DeleteChannelGrantsForUserAsync(Guid teamId, Guid userId, CancellationToken ct)
    {
        // All grants in the team's channels, including dormant ones (channel currently Everyone).
        await db.ChannelMembers
            .Where(m => m.UserId == userId && db.Channels.Any(c => c.Id == m.ChannelId && c.TeamId == teamId))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Leave / remove cleanup (runs after the membership delete was saved, inside the caller's transaction):
    /// all channel grants, the user's notifications for the team (join-request and mention/reply previews), and their
    /// pending join requests; then re-balances Admin join-request notifications.
    /// </summary>
    private async Task<IReadOnlyList<NotificationEntity>> RemoveMembershipSideEffectsAsync(
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        await DeleteChannelGrantsForUserAsync(teamId, userId, ct);
        await db.Notifications.Where(n => n.UserId == userId && n.TeamId == teamId).ExecuteDeleteAsync(ct);
        await db.TeamJoinRequests
            .Where(r => r.TeamId == teamId && r.UserId == userId && r.Status == TeamJoinRequestStatus.Pending)
            .ExecuteDeleteAsync(ct);
        return await ReconcileJoinNotificationsAsync(teamId, ct);
    }

    // ------------------------------------------------------------------ helpers: join-request notifications

    /// <summary>
    /// New request: one TeamJoinRequest notification per Admin+ for this request (MessageId = request id).
    /// Each pending join is its own list item so the bell count matches what the panel shows.
    /// </summary>
    private async Task<IReadOnlyList<NotificationEntity>> FanOutJoinRequestNotificationsAsync(
        TeamEntity team,
        TeamJoinRequestEntity request,
        CancellationToken ct)
    {
        var adminIds = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == team.Id && m.Role >= MemberRole.Admin)
            .Select(m => m.UserId)
            .ToListAsync(ct);
        if (adminIds.Count == 0)
        {
            return [];
        }

        var name = await db.Users.AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "Someone";

        var created = adminIds
            .Select(adminId => NewJoinNotification(
                adminId,
                team,
                request.Id,
                request.UserId,
                $"{name} requested to join {team.Name}"))
            .ToList();
        db.Notifications.AddRange(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// Keeps TeamJoinRequest notifications consistent with reality (call after request status / role changes were saved):
    /// removes notifications for decided/missing requests or non-admins, then ensures every Admin+ has
    /// one notification per still-pending request.
    /// Returns the newly created notifications (for realtime push after commit).
    /// </summary>
    private async Task<IReadOnlyList<NotificationEntity>> ReconcileJoinNotificationsAsync(Guid teamId, CancellationToken ct)
    {
        var pending = await db.TeamJoinRequests.AsNoTracking()
            .Where(r => r.TeamId == teamId && r.Status == TeamJoinRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Id, r.UserId, r.CreatedAt })
            .ToListAsync(ct);
        var pendingIds = pending.Select(p => p.Id).ToList();
        var adminIds = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.Role >= MemberRole.Admin)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        await db.Notifications
            .Where(n => n.Type == NotificationType.TeamJoinRequest
                        && n.TeamId == teamId
                        && (!pendingIds.Contains(n.MessageId) || !adminIds.Contains(n.UserId)))
            .ExecuteDeleteAsync(ct);

        if (pending.Count == 0 || adminIds.Count == 0)
        {
            return [];
        }

        var existing = await db.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.TeamJoinRequest && n.TeamId == teamId)
            .Select(n => new { n.UserId, n.MessageId })
            .ToListAsync(ct);
        var have = existing.Select(e => (e.UserId, e.MessageId)).ToHashSet();

        var team = await db.Teams.AsNoTracking().FirstAsync(t => t.Id == teamId, ct);
        var names = await db.Users.AsNoTracking()
            .Where(u => pending.Select(p => p.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var created = new List<NotificationEntity>();
        foreach (var adminId in adminIds)
        {
            foreach (var req in pending)
            {
                if (have.Contains((adminId, req.Id)))
                {
                    continue;
                }

                var name = names.GetValueOrDefault(req.UserId, "Someone");
                created.Add(NewJoinNotification(
                    adminId,
                    team,
                    req.Id,
                    req.UserId,
                    $"{name} requested to join {team.Name}"));
            }
        }

        if (created.Count == 0)
        {
            return [];
        }

        db.Notifications.AddRange(created);
        await db.SaveChangesAsync(ct);
        return created;
    }

    private NotificationEntity NewJoinNotification(
        Guid adminId,
        TeamEntity team,
        Guid requestId,
        Guid requesterId,
        string preview) =>
        new()
        {
            Id = UuidV7.NewGuid(),
            UserId = adminId,
            Type = NotificationType.TeamJoinRequest,
            TeamId = team.Id,
            ChannelId = Guid.Empty,
            MessageId = requestId,
            ActorId = requesterId,
            BodyPreview = preview.Length <= 280 ? preview : preview[..280],
            CreatedAt = clock.UtcNow,
        };

    private async Task PublishJoinNotificationsAsync(
        string teamSlug,
        IReadOnlyList<NotificationEntity> rows,
        CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var actorIds = rows.Select(r => r.ActorId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        foreach (var n in rows)
        {
            channelNotifier.NotifyUser(
                n.UserId,
                "notificationCreated",
                new NotificationDto(
                    n.Id,
                    n.Type,
                    n.TeamId,
                    teamSlug,
                    n.ChannelId,
                    "",
                    n.MessageId,
                    n.ActorId,
                    names.GetValueOrDefault(n.ActorId, "Unknown user"),
                    n.BodyPreview,
                    n.CreatedAt,
                    n.ReadAt));
        }
    }
}
