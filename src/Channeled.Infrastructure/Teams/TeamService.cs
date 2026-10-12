using System.Text.RegularExpressions;
using Channeled.Application.Abstractions.Email;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Identity;
using Channeled.Application.Teams;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Identity;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Identity;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Teams;

public sealed partial class TeamService(
    ApplicationDbContext db,
    UserManager<AppUser> userManager,
    IAuthorizer authorizer,
    IClock clock,
    IEmailSender emailSender) : ITeamService
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

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
        };
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
                return new TeamMemberDto(m.UserId, u?.Email ?? "", u?.DisplayName ?? "", m.Role, m.JoinedAt);
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

        if (loaded.Role == MemberRole.Admin && request.Role == MemberRole.Admin && member.Role != MemberRole.Admin)
        {
            // Admin can set Admin
        }

        member.Role = request.Role;
        await WriteAuditAsync(userId, AuditEventType.RoleChange, "user", memberUserId.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
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

        if (member.Role == MemberRole.Owner)
        {
            return AppResult.Fail(ErrorCodes.Forbidden, "Cannot remove an Owner.");
        }

        db.TeamMembers.Remove(member);
        await WriteAuditAsync(userId, AuditEventType.MemberRemove, "user", memberUserId.ToString(), loaded.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
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

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<ChannelDto>.Fail(ErrorCodes.Validation, "Channel name is required.");
        }

        var name = request.Name.Trim();
        var exists = await db.Channels.AnyAsync(
            c => c.TeamId == loaded.Team.Id && c.Name.ToLower() == name.ToLower(),
            ct);
        if (exists)
        {
            return AppResult<ChannelDto>.Fail(ErrorCodes.Conflict, "Channel name already exists.");
        }

        var channel = new ChannelEntity
        {
            Id = UuidV7.NewGuid(),
            TeamId = loaded.Team.Id,
            Name = name,
            Description = request.Description?.Trim(),
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

        var privateIds = channels.Where(c => c.Visibility == ChannelVisibility.Private).Select(c => c.Id).ToList();
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

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var name = request.Name.Trim();
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
        }

        if (request.Description is not null)
        {
            loaded.Channel.Description = request.Description.Trim();
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
        await WriteAuditAsync(userId, AuditEventType.ChannelArchive, "channel", loaded.Channel.Id.ToString(), loaded.TeamMembership.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult> DeleteChannelAsync(
        Guid userId,
        string slug,
        string channelName,
        RequestContext context,
        CancellationToken ct = default)
    {
        var loaded = await LoadTeamAndChannelAsync(userId, slug, channelName, ct);
        if (loaded is null
            || !authorizer.Authorize(ChannelContext(userId, AuthAction.DeleteChannel, loaded.TeamMembership, loaded.Channel, loaded.IsChannelMember, loaded.CanWrite, loaded.CanManage)).Allowed)
        {
            return NotFoundChannel();
        }

        db.Channels.Remove(loaded.Channel);
        await WriteAuditAsync(userId, AuditEventType.ChannelDelete, "channel", loaded.Channel.Id.ToString(), loaded.TeamMembership.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

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
            return new ChannelMemberDto(m.UserId, u?.Email ?? "", u?.DisplayName ?? "", m.CanWrite, m.CanManage);
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

        if (loaded.Channel.Visibility != ChannelVisibility.Private)
        {
            return AppResult.Fail(ErrorCodes.Validation, "Only private channels have membership lists.");
        }

        var teamMember = await db.TeamMembers.AnyAsync(m => m.TeamId == loaded.TeamMembership.Team.Id && m.UserId == request.UserId, ct);
        if (!teamMember)
        {
            return AppResult.Fail(ErrorCodes.Validation, "User must be a team member.");
        }

        var row = await db.ChannelMembers.FirstOrDefaultAsync(m => m.ChannelId == loaded.Channel.Id && m.UserId == request.UserId, ct);
        if (row is null)
        {
            db.ChannelMembers.Add(new ChannelMemberEntity
            {
                ChannelId = loaded.Channel.Id,
                UserId = request.UserId,
                CanWrite = request.CanWrite,
                CanManage = request.CanManage,
            });
        }
        else
        {
            row.CanWrite = request.CanWrite;
            row.CanManage = request.CanManage;
        }

        await WriteAuditAsync(userId, AuditEventType.ChannelMembershipAdd, "channel", loaded.Channel.Id.ToString(), loaded.TeamMembership.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
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
        return AppResult.Ok();
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

        return new ChannelLoad(membership, channel, cm is not null, cm?.CanWrite ?? false, cm?.CanManage ?? false);
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
        CancellationToken ct)
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
            DetailsJson = "{}",
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
    }

    private async Task<string> AllocateSlugAsync(string basSlug, CancellationToken ct)
    {
        var slug = basSlug;
        var i = 2;
        while (await db.Teams.AnyAsync(t => t.Slug == slug, ct))
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

    private static TeamDto ToTeamDto(TeamEntity team, MemberRole role) =>
        new(team.Id, team.Name, team.Slug, team.Description, team.QuotaBytes, team.ArchivedAt, role);

    private static ChannelDto ToChannelDto(ChannelEntity channel) =>
        new(channel.Id, channel.TeamId, channel.Name, channel.Description, channel.Visibility, channel.ArchivedAt);

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
