using Channeled.Domain.Teams;

namespace Channeled.Application.Teams;

public sealed record CreateTeamRequest(string Name, string? Description);

/// <summary>Name/description only (Admin+). Join mode / default role go through <see cref="UpdateTeamSettingsRequest"/>.</summary>
public sealed record UpdateTeamRequest(string Name, string? Description);

public sealed record TeamDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    long QuotaBytes,
    DateTimeOffset? ArchivedAt,
    MemberRole? MyRole,
    TeamJoinMode JoinMode,
    MemberRole DefaultJoinRole);

/// <summary>
/// Partial settings update. Null fields are left unchanged.
/// Name/Description: Admin+. JoinMode / DefaultJoinRole: Owner only.
/// <see cref="ConfirmOpenJoinApprovePending"/> must be true when switching RequestToJoin → OpenJoin
/// (all pending requests are approved atomically with <c>DefaultJoinRole</c>).
/// </summary>
public sealed record UpdateTeamSettingsRequest(
    string? Name,
    string? Description,
    TeamJoinMode? JoinMode,
    MemberRole? DefaultJoinRole,
    bool ConfirmOpenJoinApprovePending = false);

public sealed record UpdateTeamSettingsResult(TeamDto Team, int? ApprovedCount);

public sealed record TeamJoinLinkDto(string Token, string Prefix, DateTimeOffset? ExpiresAt, string JoinPath);
public sealed record RotateTeamJoinLinkRequest(DateTimeOffset? ExpiresAt);

public sealed record TeamJoinInfoDto(string TeamName, TeamJoinMode JoinMode, bool IsMember, Guid? PendingRequestId);
public sealed record TeamJoinResultDto(string TeamSlug, MemberRole? Role, Guid? PendingRequestId, bool Joined);
public sealed record TeamJoinRequestDto(
    Guid Id,
    Guid UserId,
    string Email,
    string DisplayName,
    TeamJoinRequestStatus Status,
    DateTimeOffset CreatedAt);

/// <summary>Role must be Viewer or Member; omitted/null falls back to the team's DefaultJoinRole on the server.</summary>
public sealed record ApproveTeamJoinRequestRequest(MemberRole Role);
public sealed record PendingJoinCountDto(int Count);

public sealed record TeamMemberDto(Guid UserId, string Email, string DisplayName, MemberRole Role, DateTimeOffset JoinedAt);
public sealed record ChangeMemberRoleRequest(MemberRole Role);

/// <summary>Kind is required in JSON; missing/null is rejected (do not default to File).</summary>
public sealed record CreateChannelRequest(string Name, string? Description, ChannelVisibility Visibility, ChannelKind? Kind);
public sealed record UpdateChannelRequest(string? Name, string? Description);

/// <summary>
/// File channels with files must choose exactly one disposition:
/// <see cref="DiscardFiles"/> = true, or <see cref="MoveFilesToChannel"/> set to another file channel name.
/// Text channels ignore these fields. Empty file channels may omit both.
/// </summary>
public sealed record DeleteChannelRequest(bool? DiscardFiles = null, string? MoveFilesToChannel = null);

public sealed record ChannelDto(Guid Id, Guid TeamId, string Name, string? Description, ChannelVisibility Visibility, ChannelKind Kind, DateTimeOffset? ArchivedAt);

public sealed record SetChannelVisibilityRequest(ChannelVisibility Visibility);
public sealed record SetChannelVisibilityResult(ChannelDto Channel, int MembersLosingAccess, int ShareLinksRevoked);

public sealed record ChannelMemberDto(Guid UserId, string Email, string DisplayName, bool ReadOnly);
public sealed record UpsertChannelMemberRequest(Guid UserId, bool ReadOnly);
public sealed record BulkUpsertChannelMembersRequest(IReadOnlyList<UpsertChannelMemberRequest> Members);

// ---- Deprecated channel join flow: endpoints return 410 Gone; DTOs kept for service/test compatibility. ----
public sealed record ChannelJoinInfoDto(
    string TeamSlug,
    string ChannelName,
    ChannelVisibility Visibility,
    bool HasAccess,
    Guid? PendingRequestId,
    string JoinPath);
public sealed record ChannelJoinRequestDto(
    Guid Id,
    Guid UserId,
    string Email,
    string DisplayName,
    ChannelJoinRequestStatus Status,
    DateTimeOffset CreatedAt);
public sealed record ApproveChannelJoinRequestRequest(bool CanWrite = true, bool CanManage = false);

// ---- Deprecated email invitations: endpoints return 410 Gone after SPA cutover. ----
public sealed record CreateTeamInvitationRequest(string Email, MemberRole Role);
public sealed record TeamInvitationDto(Guid Id, string Email, MemberRole Role, DateTimeOffset ExpiresAt, string? RawToken);
public sealed record AcceptTeamInvitationRequest(string Token);
