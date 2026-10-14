using System.Text.Json;
using System.Text.RegularExpressions;
using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Files;
using Channeled.Domain.Identity;
using Channeled.Domain.Storage;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Jobs;
using Channeled.Infrastructure.Notifications;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.PublicLinks;
using Channeled.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Channeled.Infrastructure.Teams;

public sealed partial class TeamService(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    IAuthorizer authorizer,
    IClock clock,
    IEmailSender emailSender,
    IAuthStateChanged authStateChanged,
    IChannelNotifier channelNotifier,
    ChannelWriteGuard channelWriteGuard,
    IOptions<ChannelDeleteOptions> channelDeleteOptions,
    TeamJoinLinkProtector joinLinkProtector,
    IOptions<TeamJoinOptions> joinOptions,
    IFileStore store) : ITeamService
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    private static readonly HashSet<string> ReservedSlugs =
        new(["join", "join-requests", "invitations", "pending-join-count"], StringComparer.Ordinal);

    public async Task<AppResult<TeamDto>> CreateTeamAsync(
        Guid userId,
        CreateTeamRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.Validation, "Team name is required.");
        }

        var slug = await AllocateSlugAsync(Slugify(request.Name), ct);
        var team = new TeamEntity
        {
            Id = UuidV7.NewGuid(),
            Name = request.Name.Trim(),
            Slug = slug,
            Description = request.Description?.Trim(),
            CreatedAt = clock.UtcNow,
            JoinMode = TeamJoinMode.RequestToJoin,
            DefaultJoinRole = MemberRole.Viewer,
        };
        AssignNewJoinToken(team, expiresAt: null);
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMemberEntity
        {
            TeamId = team.Id,
            UserId = userId,
            Role = MemberRole.Owner,
            JoinedAt = clock.UtcNow,
        });
        await WriteAuditAsync(userId, AuditEventType.TeamCreate, "team", team.Id.ToString(), team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<TeamDto>.Ok(ToTeamDto(team, MemberRole.Owner));
    }

    public async Task<AppResult<IReadOnlyList<TeamDto>>> ListMyTeamsAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Teams.AsNoTracking(), m => m.TeamId, t => t.Id, (m, t) => new { t, m.Role })
            .OrderBy(x => x.t.Name)
            .ToListAsync(ct);

        IReadOnlyList<TeamDto> list = rows.Select(x => ToTeamDto(x.t, x.Role)).ToList();
        return AppResult<IReadOnlyList<TeamDto>>.Ok(list);
    }

    public async Task<AppResult<TeamDto>> GetTeamAsync(Guid userId, string slug, CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam<TeamDto>();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.ViewTeam, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam<TeamDto>();
        }

        return AppResult<TeamDto>.Ok(ToTeamDto(loaded.Team, loaded.Role));
    }

    public async Task<AppResult<TeamDto>> UpdateTeamAsync(
        Guid userId,
        string slug,
        UpdateTeamRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam<TeamDto>();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.EditTeamSettings, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam<TeamDto>();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.Validation, "Team name is required.");
        }

        loaded.Team.Name = request.Name.Trim();
        loaded.Team.Description = request.Description?.Trim();
        await db.SaveChangesAsync(ct);
        return AppResult<TeamDto>.Ok(ToTeamDto(loaded.Team, loaded.Role));
    }

    public async Task<AppResult> ArchiveTeamAsync(Guid userId, string slug, RequestContext context, CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.EditTeamSettings, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam();
        }

        loaded.Team.ArchivedAt ??= clock.UtcNow;
        var archivedTeamId = loaded.Team.Id;
        await ShareLinkRevoker.RevokeAsync(
            db, clock, l => l.TeamId == archivedTeamId, ShareLinkRevoker.ReasonTeamArchive, userId, context, ct);
        await WriteAuditAsync(userId, AuditEventType.TeamArchive, "team", loaded.Team.Id.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> DeleteTeamAsync(Guid userId, string slug, RequestContext context, CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.DeleteTeam, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam();
        }

        var deletedTeamId = loaded.Team.Id;
        await ShareLinkRevoker.RevokeAsync(
            db, clock, l => l.TeamId == deletedTeamId, ShareLinkRevoker.ReasonTeamDelete, userId, context, ct);
        db.Teams.Remove(loaded.Team);
        await WriteAuditAsync(userId, AuditEventType.TeamDelete, "team", loaded.Team.Id.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> TransferOwnershipAsync(
        Guid userId,
        string slug,
        Guid newOwnerUserId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null)
        {
            return NotFoundTeam();
        }

        var decision = authorizer.Authorize(BaseContext(userId, AuthAction.TransferOwnership, loaded));
        if (!decision.Allowed)
        {
            return NotFoundTeam();
        }

        var target = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == loaded.Team.Id && m.UserId == newOwnerUserId, ct);
        if (target is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Member not found.");
        }

        var currentOwner = await db.TeamMembers.FirstAsync(m => m.TeamId == loaded.Team.Id && m.UserId == userId, ct);
        currentOwner.Role = MemberRole.Admin;
        target.Role = MemberRole.Owner;
        await WriteAuditAsync(userId, AuditEventType.RoleChange, "user", newOwnerUserId.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<IReadOnlyList<TeamMemberDto>>> ListMembersAsync(Guid userId, string slug, CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ViewTeam, loaded)).Allowed)
        {
            return NotFoundTeam<IReadOnlyList<TeamMemberDto>>();
        }

        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == loaded.Team.Id)
            .ToListAsync(ct);
        var users = await userManager.Users.AsNoTracking()
            .Where(u => members.Select(m => m.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        IReadOnlyList<TeamMemberDto> dtos = members
            .Select(m =>
            {
                users.TryGetValue(m.UserId, out var u);
                return new TeamMemberDto(
                    m.UserId,
                    u?.Email ?? "",
                    u?.DisplayName ?? "",
                    u?.UsernameSlug ?? "",
                    u?.AvatarUpdatedAt?.ToUnixTimeMilliseconds(),
                    m.Role,
                    m.JoinedAt);
            })
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.Email)
            .ToList();

        return AppResult<IReadOnlyList<TeamMemberDto>>.Ok(dtos);
    }

    public async Task<AppResult> ChangeMemberRoleAsync(
        Guid userId,
        string slug,
        Guid memberUserId,
        ChangeMemberRoleRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ChangeMemberRole, loaded)).Allowed)
        {
            return NotFoundTeam();
        }

        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == loaded.Team.Id && m.UserId == memberUserId, ct);
        if (member is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Member not found.");
        }

        // Admins cannot change Owners; cannot promote to Owner (transfer only)
        if (member.Role == MemberRole.Owner || request.Role == MemberRole.Owner)
        {
            return AppResult.Fail(ErrorCodes.Forbidden, "Use ownership transfer for Owner role.");
        }

        var teamId = loaded.Team.Id;
        var fromRole = member.Role;
        var toRole = request.Role;
        if (fromRole == toRole)
        {
            return AppResult.Ok();
        }

        var demotingAdmin = fromRole == MemberRole.Admin && toRole < MemberRole.Admin;
        var promotingToAdmin = fromRole < MemberRole.Admin && toRole == MemberRole.Admin;

        List<(Guid Id, string Name)> channelsLosingAccess = [];
        IReadOnlyList<NotificationEntity> newJoinNotifications = [];

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (demotingAdmin)
            {
                // Restricted channels the user could only see through Admin implicit access (no explicit grant).
                var rows = await db.Channels.AsNoTracking()
                    .Where(c => c.TeamId == teamId
                                && c.Visibility == ChannelVisibility.Restricted
                                && !db.ChannelMembers.Any(m => m.ChannelId == c.Id && m.UserId == memberUserId))
                    .OrderBy(c => c.Name)
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync(ct);
                channelsLosingAccess = rows.Select(r => (r.Id, r.Name)).ToList();
            }

            member.Role = toRole;

            if (promotingToAdmin)
            {
                // Admin access is implicit: drop redundant explicit grants in this team.
                await DeleteChannelGrantsForUserAsync(teamId, memberUserId, ct);
            }

            var details = JsonSerializer.Serialize(new
            {
                from = (int)fromRole,
                to = (int)toRole,
                channelsLosingAccess = channelsLosingAccess.Select(c => new { id = c.Id, name = c.Name }),
            });
            await WriteAuditAsync(userId, AuditEventType.RoleChange, "user", memberUserId.ToString(), teamId, context, ct, details);
            await db.SaveChangesAsync(ct);

            if (demotingAdmin || promotingToAdmin)
            {
                // Demoted Admins stop receiving join-request notifications; new Admins start receiving them.
                newJoinNotifications = await ReconcileJoinNotificationsAsync(teamId, ct);
            }

            await tx.CommitAsync(ct);
        }

        authStateChanged.NotifyUser(memberUserId);
        await PublishJoinNotificationsAsync(loaded.Team.Slug, newJoinNotifications, ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> RemoveMemberAsync(
        Guid userId,
        string slug,
        Guid memberUserId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.RemoveMember, loaded)).Allowed)
        {
            return NotFoundTeam();
        }

        var member = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == loaded.Team.Id && m.UserId == memberUserId, ct);
        if (member is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Member not found.");
        }

        if (memberUserId == userId)
        {
            return AppResult.Fail(ErrorCodes.Validation, "Use leave to remove yourself from the team.");
        }

        if (member.Role == MemberRole.Owner)
        {
            // Covers the last-Owner rule: an Owner is never removable, ownership must be transferred first.
            return AppResult.Fail(ErrorCodes.Forbidden, "Cannot remove an Owner.");
        }

        // Admins may only remove Viewers/Members; Owners may remove anyone except themselves/Owners.
        if (loaded.Role == MemberRole.Admin && member.Role >= MemberRole.Admin)
        {
            return AppResult.Fail(ErrorCodes.Forbidden, "Admins can only remove Viewers and Members.");
        }

        IReadOnlyList<NotificationEntity> newJoinNotifications;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            db.TeamMembers.Remove(member);
            await WriteAuditAsync(userId, AuditEventType.MemberRemove, "user", memberUserId.ToString(), loaded.Team.Id, context, ct);
            await db.SaveChangesAsync(ct);
            newJoinNotifications = await RemoveMembershipSideEffectsAsync(loaded.Team.Id, memberUserId, ct);
            await tx.CommitAsync(ct);
        }

        authStateChanged.NotifyUser(memberUserId);
        await PublishJoinNotificationsAsync(loaded.Team.Slug, newJoinNotifications, ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<TeamInvitationDto>> InviteAsync(
        Guid userId,
        string slug,
        CreateTeamInvitationRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.InviteMember, loaded)).Allowed)
        {
            return NotFoundTeam<TeamInvitationDto>();
        }

        if (request.Role == MemberRole.Owner)
        {
            return AppResult<TeamInvitationDto>.Fail(ErrorCodes.Validation, "Cannot invite as Owner.");
        }

        EmailAddress email;
        try
        {
            email = EmailAddress.Parse(request.Email);
        }
        catch (ArgumentException ex)
        {
            return AppResult<TeamInvitationDto>.Fail(ErrorCodes.Validation, ex.Message);
        }

        var raw = TokenHasher.CreateRawToken();
        var invite = new TeamInvitationEntity
        {
            Id = UuidV7.NewGuid(),
            TeamId = loaded.Team.Id,
            Email = email.Value,
            Role = request.Role,
            TokenHash = TokenHasher.Hash(raw),
            InvitedByUserId = userId,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.Add(InvitationLifetime),
        };
        db.TeamInvitations.Add(invite);
        await WriteAuditAsync(userId, AuditEventType.InviteCreate, "invitation", invite.Id.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await emailSender.SendAsync(email.Value, "Team invitation", $"Token: {raw}", ct);
        return AppResult<TeamInvitationDto>.Ok(new TeamInvitationDto(invite.Id, invite.Email, invite.Role, invite.ExpiresAt, raw));
    }

    public async Task<AppResult<TeamDto>> AcceptInvitationAsync(
        Guid userId,
        AcceptTeamInvitationRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var hash = TokenHasher.Hash(request.Token);
        var invite = await db.TeamInvitations.Include(i => i.Team).FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invite is null || invite.RevokedAt is not null || invite.AcceptedAt is not null || invite.ExpiresAt <= clock.UtcNow || invite.Team is null)
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.InvalidToken, "Invitation token is invalid.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user?.Email is null || !string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
        {
            return AppResult<TeamDto>.Fail(ErrorCodes.Forbidden, "Invitation email does not match signed-in user.");
        }

        var existing = await db.TeamMembers.FirstOrDefaultAsync(m => m.TeamId == invite.TeamId && m.UserId == userId, ct);
        if (existing is null)
        {
            db.TeamMembers.Add(new TeamMemberEntity
            {
                TeamId = invite.TeamId,
                UserId = userId,
                Role = invite.Role,
                JoinedAt = clock.UtcNow,
            });
        }

        invite.AcceptedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.InviteAccept, "invitation", invite.Id.ToString(), invite.TeamId, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<TeamDto>.Ok(ToTeamDto(invite.Team, invite.Role));
    }

    public async Task<AppResult> RevokeInvitationAsync(
        Guid userId,
        string slug,
        Guid invitationId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.InviteMember, loaded)).Allowed)
        {
            return NotFoundTeam();
        }

        var invite = await db.TeamInvitations.FirstOrDefaultAsync(
            i => i.Id == invitationId && i.TeamId == loaded.Team.Id,
            ct);
        if (invite is null || invite.AcceptedAt is not null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Invitation not found.");
        }

        invite.RevokedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.InviteRevoke, "invitation", invite.Id.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<ChannelDto>> CreateChannelAsync(
        Guid userId,
        string slug,
        CreateChannelRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.CreateChannel, loaded)).Allowed)
        {
            return NotFoundTeam<ChannelDto>();
        }

        var nameError = ChannelNameRules.Validate(request.Name);
        if (nameError is not null)
        {
            return AppResult<ChannelDto>.Fail(ErrorCodes.Validation, nameError);
        }

        var name = ChannelNameRules.Normalize(request.Name!);
        var exists = await db.Channels.AnyAsync(
            c => c.TeamId == loaded.Team.Id && c.Name.ToLower() == name.ToLower(),
            ct);
        if (exists)
        {
            return AppResult<ChannelDto>.Fail(ErrorCodes.Conflict, "Channel name already exists.");
        }

        if (request.Kind is not (ChannelKind.File or ChannelKind.Text))
        {
            return AppResult<ChannelDto>.Fail(ErrorCodes.Validation, "Channel kind is required (file or text).");
        }

        var kind = request.Kind.Value;
        var channel = new ChannelEntity
        {
            Id = UuidV7.NewGuid(),
            TeamId = loaded.Team.Id,
            Name = name,
            Description = request.Description?.Trim(),
            Kind = kind,
            Visibility = request.Visibility,
            CreatedAt = clock.UtcNow,
        };
        db.Channels.Add(channel);
        await WriteAuditAsync(userId, AuditEventType.ChannelCreate, "channel", channel.Id.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<ChannelDto>.Ok(ToChannelDto(channel));
    }

    public async Task<AppResult<IReadOnlyList<ChannelDto>>> ListChannelsAsync(Guid userId, string slug, CancellationToken ct = default)
    {
        var loaded = await LoadTeamMembershipAsync(userId, slug, ct);
        if (loaded is null || !authorizer.Authorize(BaseContext(userId, AuthAction.ViewTeam, loaded)).Allowed)
        {
            return NotFoundTeam<IReadOnlyList<ChannelDto>>();
        }

        var channels = await db.Channels.AsNoTracking()
            .Where(c => c.TeamId == loaded.Team.Id)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var privateIds = channels.Where(c => c.Visibility == ChannelVisibility.Restricted).Select(c => c.Id).ToList();
        var memberships = await db.ChannelMembers.AsNoTracking()
            .Where(m => m.UserId == userId && privateIds.Contains(m.ChannelId))
            .Select(m => m.ChannelId)
            .ToListAsync(ct);
        var memberSet = memberships.ToHashSet();

        IReadOnlyList<ChannelDto> visible = channels
            .Where(c =>
            {
                var ctx = ChannelContext(userId, AuthAction.ViewChannel, loaded, c, memberSet.Contains(c.Id), false, false);
                return authorizer.Authorize(ctx).Allowed;
            })
            .Select(ToChannelDto)
            .ToList();

        return AppResult<IReadOnlyList<ChannelDto>>.Ok(visible);
    }

    public async Task<AppResult<ChannelDto>> GetChannelAsync(Guid userId, string slug, string channelName, CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null)
        {
            return NotFoundChannel<ChannelDto>();
        }

        var decision = authorizer.Authorize(ChannelContext(
            userId,
            AuthAction.ViewChannel,
            loaded.TeamMembership,
            loaded.Channel,
            loaded.IsChannelMember,
            loaded.CanWrite,
            loaded.CanManage));
        if (!decision.Allowed)
        {
            return NotFoundChannel<ChannelDto>();
        }

        return AppResult<ChannelDto>.Ok(ToChannelDto(loaded.Channel));
    }

    public async Task<AppResult<ChannelDto>> UpdateChannelAsync(
        Guid userId,
        string slug,
        string channelName,
        UpdateChannelRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(
                userId,
                AuthAction.CreateChannel,
                loaded.TeamMembership,
                loaded.Channel,
                loaded.IsChannelMember,
                loaded.CanWrite,
                loaded.CanManage)).Allowed)
        {
            return NotFoundChannel<ChannelDto>();
        }

        var nameChanged = false;
        if (request.Name is not null)
        {
            var nameError = ChannelNameRules.Validate(request.Name);
            if (nameError is not null)
            {
                return AppResult<ChannelDto>.Fail(ErrorCodes.Validation, nameError);
            }

            var name = ChannelNameRules.Normalize(request.Name);
            if (!string.Equals(name, loaded.Channel.Name, StringComparison.Ordinal))
            {
                var clash = await db.Channels.AnyAsync(
                    c => c.TeamId == loaded.TeamMembership.Team.Id
                         && c.Id != loaded.Channel.Id
                         && c.Name.ToLower() == name.ToLower(),
                    ct);
                if (clash)
                {
                    return AppResult<ChannelDto>.Fail(ErrorCodes.Conflict, "Channel name already exists.");
                }

                loaded.Channel.Name = name;
                nameChanged = true;
            }
        }

        var descriptionChanged = false;
        if (request.Description is not null)
        {
            var nextDescription = string.IsNullOrWhiteSpace(request.Description)
                ? null
                : request.Description.Trim();
            if (!string.Equals(nextDescription, loaded.Channel.Description, StringComparison.Ordinal))
            {
                loaded.Channel.Description = nextDescription;
                descriptionChanged = true;
            }
        }

        if (!nameChanged && !descriptionChanged)
        {
            return AppResult<ChannelDto>.Ok(ToChannelDto(loaded.Channel));
        }

        await WriteAuditAsync(
            userId,
            AuditEventType.ChannelChange,
            "channel",
            loaded.Channel.Id.ToString(),
            loaded.TeamMembership.Team.Id,
            context,
            ct);
        await db.SaveChangesAsync(ct);
        return AppResult<ChannelDto>.Ok(ToChannelDto(loaded.Channel));
    }

    public async Task<AppResult> ArchiveChannelAsync(
        Guid userId,
        string slug,
        string channelName,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ArchiveChannel, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        loaded.Channel.ArchivedAt ??= clock.UtcNow;
        var archivedChannelId = loaded.Channel.Id;
        await ShareLinkRevoker.RevokeAsync(
            db, clock, l => l.ChannelId == archivedChannelId, ShareLinkRevoker.ReasonChannelArchive, userId, context, ct);
        await WriteAuditAsync(userId, AuditEventType.ChannelArchive, "channel", loaded.Channel.Id.ToString(), loaded.TeamMembership.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        authStateChanged.NotifyChannel(loaded.Channel.Id);
        return AppResult.Ok();
    }

    public async Task<AppResult> DeleteChannelAsync(
        Guid userId,
        string slug,
        string channelName,
        DeleteChannelRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.DeleteChannel, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        var opts = channelDeleteOptions.Value;
        var channel = loaded.Channel;
        var team = loaded.TeamMembership.Team;
        var deletedChannelId = channel.Id;
        var channelKind = channel.Kind;
        var channelNameSnapshot = channel.Name;
        Guid? notifyTargetChannelId = null;
        Guid? moveTargetId = null;
        string? moveTargetName = null;

        var messageCount = await db.ChannelMessages.CountAsync(m => m.ChannelId == deletedChannelId, ct);
        if (messageCount > opts.MaxMessagesPerTxn)
        {
            return AppResult.Fail(
                ErrorCodes.Validation,
                $"Channel has too many messages to delete in one request (max {opts.MaxMessagesPerTxn:N0}).");
        }

        var fileCount = channel.Kind == ChannelKind.File
            ? await db.Files.CountAsync(f => f.ChannelId == deletedChannelId, ct)
            : 0;

        var discard = request.DiscardFiles == true;
        var moveTo = string.IsNullOrWhiteSpace(request.MoveFilesToChannel)
            ? null
            : request.MoveFilesToChannel.Trim();

        if (channel.Kind == ChannelKind.File)
        {
            if (discard && moveTo is not null)
            {
                return AppResult.Fail(ErrorCodes.Validation, "Choose either discard or move files, not both.");
            }

            if (fileCount > 0 && !discard && moveTo is null)
            {
                return AppResult.Fail(
                    ErrorCodes.Validation,
                    "File channel has files: set discardFiles=true or moveFilesToChannel to another file channel.");
            }

            if (fileCount > opts.MaxFilesPerTxn)
            {
                return AppResult.Fail(
                    ErrorCodes.Validation,
                    $"Channel has too many files to delete or move in one request (max {opts.MaxFilesPerTxn:N0}).");
            }

            if (moveTo is not null)
            {
                if (string.Equals(moveTo, channel.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return AppResult.Fail(ErrorCodes.Validation, "Cannot move files into the channel being deleted.");
                }

                var target = await db.Channels.AsNoTracking().FirstOrDefaultAsync(
                    c => c.TeamId == team.Id && c.Name.ToLower() == moveTo.ToLower(),
                    ct);
                if (target is null || target.Kind != ChannelKind.File || target.ArchivedAt is not null)
                {
                    return AppResult.Fail(ErrorCodes.Validation, "Move target must be an active file channel in this team.");
                }

                moveTargetId = target.Id;
                moveTargetName = target.Name;
            }
        }

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Timeouts are sanitized to [A-Za-z0-9] only (e.g. 5s); cannot be bound as parameters for SET LOCAL.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"SET LOCAL lock_timeout = '{SanitizeTimeout(opts.LockTimeout)}'",
                ct);
            await db.Database.ExecuteSqlRawAsync(
                $"SET LOCAL statement_timeout = '{SanitizeTimeout(opts.StatementTimeout)}'",
                ct);
#pragma warning restore EF1002

            // Lock channels in ascending Id order (delete FOR UPDATE; move target FOR SHARE).
            var locks = new List<(Guid Id, bool ForUpdate)> { (deletedChannelId, true) };
            if (moveTargetId is Guid tid)
            {
                locks.Add((tid, false));
            }

            var lockResult = await channelWriteGuard.LockOrderedAsync(locks, ct);
            if (!lockResult.Succeeded)
            {
                return lockResult;
            }

            // Re-check existence after lock (race with concurrent delete).
            if (!await db.Channels.AnyAsync(c => c.Id == deletedChannelId, ct))
            {
                return NotFoundChannel();
            }

            if (moveTargetId is Guid targetLockId)
            {
                var targetStillOk = await db.Channels.AnyAsync(
                    c => c.Id == targetLockId && c.Kind == ChannelKind.File && c.ArchivedAt == null,
                    ct);
                if (!targetStillOk)
                {
                    return AppResult.Fail(ErrorCodes.Validation, "Move target must be an active file channel in this team.");
                }
            }

            if (channel.Kind == ChannelKind.File)
            {
                if (moveTargetId is Guid targetId)
                {
                    var moveResult = await MoveChannelFilesInTxnAsync(userId, deletedChannelId, targetId, context, ct);
                    if (!moveResult.Succeeded)
                    {
                        return moveResult;
                    }

                    notifyTargetChannelId = targetId;
                }
                else
                {
                    var discardResult = await DiscardChannelFilesInTxnAsync(deletedChannelId, team.Id, opts.ObjectDeleteChunkSize, ct);
                    if (!discardResult.Succeeded)
                    {
                        return discardResult;
                    }
                }
            }
            else
            {
                await CollectAndRemoveSessionsAsync(deletedChannelId, enqueueDeletes: true, opts.ObjectDeleteChunkSize, ct);
            }

            if (messageCount > 0)
            {
                await db.ChannelMessages.Where(m => m.ChannelId == deletedChannelId).ExecuteDeleteAsync(ct);
            }

            await ShareLinkRevoker.RevokeAsync(
                db, clock, l => l.ChannelId == deletedChannelId, ShareLinkRevoker.ReasonChannelDelete, userId, context, ct);

            await db.Notifications.Where(n => n.ChannelId == deletedChannelId).ExecuteDeleteAsync(ct);

            var tracked = await db.Channels.FirstAsync(c => c.Id == deletedChannelId, ct);
            db.Channels.Remove(tracked);

            var auditMove = moveTargetId is Guid mid
                ? $",\"moveTargetId\":\"{mid}\",\"moveTargetName\":{JsonString(moveTargetName)}"
                : ",\"moveTargetId\":null,\"moveTargetName\":null";
            await WriteAuditAsync(
                userId,
                AuditEventType.ChannelDelete,
                "channel",
                deletedChannelId.ToString(),
                team.Id,
                context,
                ct,
                $"{{\"kind\":{(int)channelKind},\"name\":{JsonString(channelNameSnapshot)},\"discardFiles\":{(discard).ToString().ToLowerInvariant()},\"moveFilesToChannel\":{JsonString(moveTo)}{auditMove}}}");

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.QueryCanceled or PostgresErrorCodes.LockNotAvailable)
        {
            return ex.SqlState == PostgresErrorCodes.LockNotAvailable
                ? AppResult.Fail(ErrorCodes.LockTimeout, "Channel is busy; retry shortly.")
                : AppResult.Fail(ErrorCodes.Validation, "Channel delete timed out; try again later.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg
                                            && pg.SqlState is PostgresErrorCodes.QueryCanceled or PostgresErrorCodes.LockNotAvailable)
        {
            return pg.SqlState == PostgresErrorCodes.LockNotAvailable
                ? AppResult.Fail(ErrorCodes.LockTimeout, "Channel is busy; retry shortly.")
                : AppResult.Fail(ErrorCodes.Validation, "Channel delete timed out; try again later.");
        }

        authStateChanged.NotifyChannel(deletedChannelId);
        if (notifyTargetChannelId is Guid notifyId)
        {
            channelNotifier.NotifyFilesChanged(notifyId);
        }

        return AppResult.Ok();
    }

    private async Task<AppResult> MoveChannelFilesInTxnAsync(
        Guid userId,
        Guid sourceId,
        Guid targetId,
        RequestContext context,
        CancellationToken ct)
    {
        var files = await db.Files.Where(f => f.ChannelId == sourceId).ToListAsync(ct);
        var taken = await db.Files.AsNoTracking()
            .Where(f => f.ChannelId == targetId && f.FolderId == null && f.DeletedAt == null)
            .Select(f => f.Name.ToLower())
            .ToListAsync(ct);
        var takenSet = taken.ToHashSet(StringComparer.Ordinal);

        foreach (var file in files)
        {
            await ShareLinkRevoker.RevokeAsync(
                db, clock, l => l.FileId == file.Id, ShareLinkRevoker.ReasonMove, userId, context, ct);

            // Trashed files stay trashed; active files flatten to target root with unique names.
            if (file.DeletedAt is null)
            {
                file.Name = AllocateUniqueFileName(file.Name, takenSet);
                takenSet.Add(file.Name.ToLowerInvariant());
            }

            file.ChannelId = targetId;
            file.FolderId = null;
            file.UpdatedAt = clock.UtcNow;
        }

        await db.Folders.Where(f => f.ChannelId == sourceId).ExecuteDeleteAsync(ct);
        await CollectAndRemoveSessionsAsync(sourceId, enqueueDeletes: true, channelDeleteOptions.Value.ObjectDeleteChunkSize, ct);
        return AppResult.Ok();
    }

    private async Task<AppResult> DiscardChannelFilesInTxnAsync(
        Guid channelId,
        Guid teamId,
        int chunkSize,
        CancellationToken ct)
    {
        var files = await db.Files
            .Where(f => f.ChannelId == channelId)
            .Select(f => new { f.Id, f.StorageKey, f.SizeBytes })
            .ToListAsync(ct);

        var totalBytes = files.Sum(f => f.SizeBytes);
        var fileIds = files.Select(f => f.Id).ToList();
        var keys = files.Select(f => f.StorageKey).ToList();

        if (fileIds.Count > 0)
        {
            await db.FileStars.Where(s => fileIds.Contains(s.FileId)).ExecuteDeleteAsync(ct);
            await db.Files.Where(f => f.ChannelId == channelId).ExecuteDeleteAsync(ct);
        }

        await db.Folders.Where(f => f.ChannelId == channelId).ExecuteDeleteAsync(ct);

        var multiparts = await CollectAndRemoveSessionsAsync(channelId, enqueueDeletes: false, chunkSize, ct);

        ObjectDeleteOutbox.Enqueue(db, clock.UtcNow, keys, multiparts, chunkSize);

        if (totalBytes > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE teams SET "UsageBytes" = GREATEST("UsageBytes" - {totalBytes}, 0) WHERE "Id" = {teamId}""",
                ct);
        }

        return AppResult.Ok();
    }

    private async Task<List<ObjectDeleteMultipart>> CollectAndRemoveSessionsAsync(
        Guid channelId,
        bool enqueueDeletes,
        int chunkSize,
        CancellationToken ct)
    {
        var sessions = await db.UploadSessions.Where(s => s.ChannelId == channelId).ToListAsync(ct);
        var multiparts = sessions
            .Where(s => s.State == UploadSessionState.Pending)
            .Select(s => new ObjectDeleteMultipart(s.ObjectKey, s.MultipartId))
            .ToList();

        // Pending keys only — completed sessions share ObjectKey with live files.
        var pendingKeys = multiparts.Select(m => m.ObjectKey).ToList();

        if (sessions.Count > 0)
        {
            var sessionIds = sessions.Select(s => s.Id).ToList();
            await db.UploadParts.Where(p => sessionIds.Contains(p.UploadSessionId)).ExecuteDeleteAsync(ct);
            db.UploadSessions.RemoveRange(sessions);
        }

        if (enqueueDeletes)
        {
            ObjectDeleteOutbox.Enqueue(db, clock.UtcNow, pendingKeys, multiparts, chunkSize);
        }

        return multiparts;
    }

    private static string AllocateUniqueFileName(string desired, HashSet<string> takenLower)
    {
        if (!takenLower.Contains(desired.ToLowerInvariant()))
        {
            return desired;
        }

        var ext = Path.GetExtension(desired);
        var stem = Path.GetFileNameWithoutExtension(desired);
        for (var i = 2; i < 10_000; i++)
        {
            var candidate = $"{stem}-{i}{ext}";
            if (!takenLower.Contains(candidate.ToLowerInvariant()))
            {
                return candidate;
            }
        }

        return $"{stem}-{Guid.NewGuid():N}{ext}";
    }

    private static string SanitizeTimeout(string value)
    {
        // Only allow simple Postgres interval literals like 5s / 60s / 1min.
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16)
        {
            return "5s";
        }

        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                return "5s";
            }
        }

        return value;
    }

    private static string JsonString(string? value) =>
        value is null
            ? "null"
            : $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    public async Task<AppResult<IReadOnlyList<ChannelMemberDto>>> ListChannelMembersAsync(
        Guid userId,
        string slug,
        string channelName,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ViewChannel, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel<IReadOnlyList<ChannelMemberDto>>();
        }

        var members = await db.ChannelMembers.AsNoTracking().Where(m => m.ChannelId == loaded.Channel.Id).ToListAsync(ct);
        var users = await userManager.Users.AsNoTracking()
            .Where(u => members.Select(m => m.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        IReadOnlyList<ChannelMemberDto> dtos = members.Select(m =>
        {
            users.TryGetValue(m.UserId, out var u);
            return new ChannelMemberDto(
                m.UserId,
                u?.Email ?? "",
                u?.DisplayName ?? "",
                u?.UsernameSlug ?? "",
                u?.AvatarUpdatedAt?.ToUnixTimeMilliseconds(),
                m.ReadOnly);
        }).ToList();

        return AppResult<IReadOnlyList<ChannelMemberDto>>.Ok(dtos);
    }

    public async Task<AppResult> UpsertChannelMemberAsync(
        Guid userId,
        string slug,
        string channelName,
        UpsertChannelMemberRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ManageChannelMembership, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        // Grants may be created while the channel is Everyone: they stay dormant until it becomes Restricted.
        var applied = await ApplyGrantsAsync(
            userId, loaded.TeamMembership.Team.Id, loaded.Channel, [request], context, ct);
        if (!applied.Succeeded)
        {
            return applied;
        }

        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> BulkUpsertChannelMembersAsync(
        Guid userId,
        string slug,
        string channelName,
        BulkUpsertChannelMembersRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ManageChannelMembership, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        if (request.Members is null || request.Members.Count == 0)
        {
            return AppResult.Fail(ErrorCodes.Validation, "At least one member is required.");
        }

        if (request.Members.Count > MaxBulkGrants)
        {
            return AppResult.Fail(ErrorCodes.Validation, $"At most {MaxBulkGrants} members per request.");
        }

        var applied = await ApplyGrantsAsync(
            userId, loaded.TeamMembership.Team.Id, loaded.Channel, request.Members, context, ct);
        if (!applied.Succeeded)
        {
            return applied;
        }

        // Single SaveChanges: all-or-nothing.
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    /// <summary>Stages grant upserts (no SaveChanges). Validates every user is a team member first.</summary>
    private async Task<AppResult> ApplyGrantsAsync(
        Guid actorId,
        Guid teamId,
        ChannelEntity channel,
        IReadOnlyList<UpsertChannelMemberRequest> grants,
        RequestContext context,
        CancellationToken ct)
    {
        // Last write wins for duplicate users in one request.
        var byUser = grants.GroupBy(g => g.UserId).ToDictionary(g => g.Key, g => g.Last());
        var userIds = byUser.Keys.ToList();

        var memberIds = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == teamId && userIds.Contains(m.UserId))
            .Select(m => m.UserId)
            .ToListAsync(ct);
        if (memberIds.Count != userIds.Count)
        {
            return AppResult.Fail(ErrorCodes.Validation, "User must be a team member.");
        }

        var existing = await db.ChannelMembers
            .Where(m => m.ChannelId == channel.Id && userIds.Contains(m.UserId))
            .ToDictionaryAsync(m => m.UserId, ct);

        foreach (var (uid, grant) in byUser)
        {
            if (existing.TryGetValue(uid, out var row))
            {
                row.ReadOnly = grant.ReadOnly;
            }
            else
            {
                db.ChannelMembers.Add(new ChannelMemberEntity
                {
                    ChannelId = channel.Id,
                    UserId = uid,
                    ReadOnly = grant.ReadOnly,
                });
            }

            await WriteAuditAsync(
                actorId,
                AuditEventType.ChannelMembershipAdd,
                "channel",
                channel.Id.ToString(),
                teamId,
                context,
                ct,
                JsonSerializer.Serialize(new { userId = uid, readOnly = grant.ReadOnly }));
        }

        return AppResult.Ok();
    }

    public async Task<AppResult> RemoveChannelMemberAsync(
        Guid userId,
        string slug,
        string channelName,
        Guid memberUserId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ManageChannelMembership, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        var row = await db.ChannelMembers.FirstOrDefaultAsync(m => m.ChannelId == loaded.Channel.Id && m.UserId == memberUserId, ct);
        if (row is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Channel member not found.");
        }

        db.ChannelMembers.Remove(row);
        await WriteAuditAsync(userId, AuditEventType.ChannelMembershipRemove, "channel", loaded.Channel.Id.ToString(), loaded.TeamMembership.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        authStateChanged.NotifyUser(memberUserId);
        return AppResult.Ok();
    }

    public async Task<AppResult<ChannelJoinInfoDto>> GetChannelJoinInfoAsync(
        Guid userId,
        string slug,
        string channelName,
        CancellationToken ct = default)
    {
        var membership = await LoadTeamMembershipAsync(userId, slug, ct);
        if (membership is null)
        {
            return NotFoundTeam<ChannelJoinInfoDto>();
        }

        var channel = await db.Channels.AsNoTracking().FirstOrDefaultAsync(
            c => c.TeamId == membership.Team.Id && c.Name.ToLower() == channelName.ToLower(),
            ct);
        if (channel is null)
        {
            return NotFoundChannel<ChannelJoinInfoDto>();
        }

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);
        var hasAccess = authorizer.Authorize(
            ChannelContext(userId, AuthAction.ViewChannel, membership, channel, cm is not null, cm is not null && !cm.ReadOnly, false)).Allowed;

        Guid? pendingId = null;
        if (!hasAccess && channel.Visibility == ChannelVisibility.Restricted)
        {
            var pending = await db.ChannelJoinRequests.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.ChannelId == channel.Id && r.UserId == userId && r.Status == ChannelJoinRequestStatus.Pending,
                    ct);
            pendingId = pending?.Id;
        }

        return AppResult<ChannelJoinInfoDto>.Ok(new ChannelJoinInfoDto(
            membership.Team.Slug,
            channel.Name,
            channel.Visibility,
            hasAccess,
            pendingId,
            $"/join/{membership.Team.Slug}/{channel.Name}"));
    }

    public async Task<AppResult<ChannelJoinRequestDto>> RequestChannelJoinAsync(
        Guid userId,
        string slug,
        string channelName,
        RequestContext context,
        CancellationToken ct = default)
    {
        var membership = await LoadTeamMembershipAsync(userId, slug, ct);
        if (membership is null)
        {
            return NotFoundTeam<ChannelJoinRequestDto>();
        }

        var channel = await db.Channels.FirstOrDefaultAsync(
            c => c.TeamId == membership.Team.Id && c.Name.ToLower() == channelName.ToLower(),
            ct);
        if (channel is null)
        {
            return NotFoundChannel<ChannelJoinRequestDto>();
        }

        if (channel.Visibility != ChannelVisibility.Restricted)
        {
            return AppResult<ChannelJoinRequestDto>.Fail(
                ErrorCodes.Validation,
                "Open channels are available to all team members — no join request needed.");
        }

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);
        var hasAccess = authorizer.Authorize(
            ChannelContext(userId, AuthAction.ViewChannel, membership, channel, cm is not null, cm is not null && !cm.ReadOnly, false)).Allowed;
        if (hasAccess)
        {
            return AppResult<ChannelJoinRequestDto>.Fail(ErrorCodes.Conflict, "You already have access to this channel.");
        }

        var existing = await db.ChannelJoinRequests
            .FirstOrDefaultAsync(
                r => r.ChannelId == channel.Id && r.UserId == userId && r.Status == ChannelJoinRequestStatus.Pending,
                ct);
        if (existing is not null)
        {
            return AppResult<ChannelJoinRequestDto>.Ok(await ToJoinRequestDtoAsync(existing, ct));
        }

        var row = new ChannelJoinRequestEntity
        {
            Id = UuidV7.NewGuid(),
            ChannelId = channel.Id,
            UserId = userId,
            Status = ChannelJoinRequestStatus.Pending,
            CreatedAt = clock.UtcNow,
        };
        db.ChannelJoinRequests.Add(row);
        await WriteAuditAsync(
            userId,
            AuditEventType.ChannelChange,
            "channel_join_request",
            row.Id.ToString(),
            membership.Team.Id,
            context,
            ct,
            """{"action":"join_request_create"}""");
        await db.SaveChangesAsync(ct);
        return AppResult<ChannelJoinRequestDto>.Ok(await ToJoinRequestDtoAsync(row, ct));
    }

    public async Task<AppResult<IReadOnlyList<ChannelJoinRequestDto>>> ListChannelJoinRequestsAsync(
        Guid userId,
        string slug,
        string channelName,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ManageChannelMembership, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel<IReadOnlyList<ChannelJoinRequestDto>>();
        }

        var rows = await db.ChannelJoinRequests.AsNoTracking()
            .Where(r => r.ChannelId == loaded.Channel.Id && r.Status == ChannelJoinRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        var userIds = rows.Select(r => r.UserId).ToList();
        var users = await userManager.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        IReadOnlyList<ChannelJoinRequestDto> dtos = rows.Select(r =>
        {
            users.TryGetValue(r.UserId, out var u);
            return new ChannelJoinRequestDto(
                r.Id,
                r.UserId,
                u?.Email ?? "",
                u?.DisplayName ?? "",
                u?.UsernameSlug ?? "",
                u?.AvatarUpdatedAt?.ToUnixTimeMilliseconds(),
                r.Status,
                r.CreatedAt);
        }).ToList();

        return AppResult<IReadOnlyList<ChannelJoinRequestDto>>.Ok(dtos);
    }

    public async Task<AppResult> ApproveChannelJoinRequestAsync(
        Guid userId,
        string slug,
        string channelName,
        Guid requestId,
        ApproveChannelJoinRequestRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ManageChannelMembership, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        var row = await db.ChannelJoinRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.ChannelId == loaded.Channel.Id, ct);
        if (row is null || row.Status != ChannelJoinRequestStatus.Pending)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Join request not found.");
        }

        var upsert = await UpsertChannelMemberAsync(
            userId,
            slug,
            channelName,
            new UpsertChannelMemberRequest(row.UserId, ReadOnly: !request.CanWrite),
            context,
            ct);
        if (!upsert.Succeeded)
        {
            return upsert;
        }

        row.Status = ChannelJoinRequestStatus.Approved;
        row.DecidedAt = clock.UtcNow;
        row.DecidedByUserId = userId;
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> RejectChannelJoinRequestAsync(
        Guid userId,
        string slug,
        string channelName,
        Guid requestId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.ManageChannelMembership, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        var row = await db.ChannelJoinRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.ChannelId == loaded.Channel.Id, ct);
        if (row is null || row.Status != ChannelJoinRequestStatus.Pending)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Join request not found.");
        }

        row.Status = ChannelJoinRequestStatus.Rejected;
        row.DecidedAt = clock.UtcNow;
        row.DecidedByUserId = userId;
        await WriteAuditAsync(
            userId,
            AuditEventType.ChannelChange,
            "channel_join_request",
            row.Id.ToString(),
            loaded.TeamMembership.Team.Id,
            context,
            ct,
            """{"action":"join_request_reject"}""");
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    private async Task<ChannelJoinRequestDto> ToJoinRequestDtoAsync(ChannelJoinRequestEntity row, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(row.UserId.ToString());
        return new ChannelJoinRequestDto(
            row.Id,
            row.UserId,
            user?.Email ?? "",
            user?.DisplayName ?? "",
            user?.UsernameSlug ?? "",
            user?.AvatarUpdatedAt?.ToUnixTimeMilliseconds(),
            row.Status,
            row.CreatedAt);
    }

    private async Task<TeamMembership?> LoadTeamMembershipAsync(Guid userId, string slug, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (team is null)
        {
            return null;
        }

        var member = await db.TeamMembers.AsNoTracking().FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null)
        {
            return null;
        }

        return new TeamMembership(team, member.Role);
    }

    private async Task<ChannelLoad?> LoadTeamAndChannelAsync(Guid userId, string slug, string channelName, CancellationToken ct)
    {
        var membership = await LoadTeamMembershipAsync(userId, slug, ct);
        if (membership is null)
        {
            return null;
        }

        var channel = await db.Channels.FirstOrDefaultAsync(
            c => c.TeamId == membership.Team.Id && c.Name.ToLower() == channelName.ToLower(),
            ct);
        if (channel is null)
        {
            return null;
        }

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);

        return new ChannelLoad(membership, channel, cm is not null, cm is not null && !cm.ReadOnly, false);
    }

    private static AuthorizationContext BaseContext(Guid userId, AuthAction action, TeamMembership loaded) =>
        new()
        {
            UserId = userId,
            Action = action,
            IsTeamMember = true,
            TeamRole = loaded.Role,
            TeamArchived = loaded.Team.ArchivedAt is not null,
            ChannelExists = false,
        };

    private static AuthorizationContext ChannelContext(
        Guid userId,
        AuthAction action,
        TeamMembership loaded,
        ChannelEntity channel,
        bool isChannelMember,
        bool canWrite,
        bool canManage) =>
        new()
        {
            UserId = userId,
            Action = action,
            IsTeamMember = true,
            TeamRole = loaded.Role,
            TeamArchived = loaded.Team.ArchivedAt is not null,
            ChannelExists = true,
            ChannelVisibility = channel.Visibility,
            ChannelArchived = channel.ArchivedAt is not null,
            IsChannelMember = isChannelMember,
            ChannelCanWrite = canWrite,
            ChannelCanManage = canManage,
        };

    private async Task WriteAuditAsync(
        Guid? actorId,
        AuditEventType type,
        string targetType,
        string targetId,
        Guid? teamId,
        RequestContext context,
        CancellationToken ct,
        string detailsJson = "{}")
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            EventType = type,
            TeamId = teamId,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = detailsJson,
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
    }

    private async Task<string> AllocateSlugAsync(string basSlug, CancellationToken ct)
    {
        var slug = basSlug;
        var i = 2;
        // Slugs that collide with literal route segments under /api/v1/teams/ are never allocated.
        while (ReservedSlugs.Contains(slug) || await db.Teams.AnyAsync(t => t.Slug == slug, ct))
        {
            slug = $"{basSlug}-{i++}";
        }

        return slug;
    }

    private static string Slugify(string name)
    {
        var normalized = name.Trim().ToLowerInvariant();
        normalized = SlugRegex().Replace(normalized, "-");
        normalized = normalized.Trim('-');
        return string.IsNullOrEmpty(normalized) ? "team" : normalized;
    }

    private static TeamDto ToTeamDto(TeamEntity team, MemberRole? role) =>
        new(
            team.Id,
            team.Name,
            team.Slug,
            team.Description,
            team.QuotaBytes,
            team.ArchivedAt,
            role,
            team.JoinMode,
            team.DefaultJoinRole,
            !string.IsNullOrEmpty(team.LogoObjectKey),
            team.LogoUpdatedAt);

    private static ChannelDto ToChannelDto(ChannelEntity channel) =>
        new(channel.Id, channel.TeamId, channel.Name, channel.Description, channel.Visibility, channel.Kind, channel.ArchivedAt);

    private static AppResult NotFoundTeam() =>
        AppResult.Fail(ErrorCodes.NotFound, "Team not found.");

    private static AppResult<T> NotFoundTeam<T>() =>
        AppResult<T>.Fail(ErrorCodes.NotFound, "Team not found.");

    private static AppResult NotFoundChannel() =>
        AppResult.Fail(ErrorCodes.NotFound, "Channel not found.");

    private static AppResult<T> NotFoundChannel<T>() =>
        AppResult<T>.Fail(ErrorCodes.NotFound, "Channel not found.");

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record TeamMembership(TeamEntity Team, MemberRole Role);

    private sealed record ChannelLoad(
        TeamMembership TeamMembership,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
