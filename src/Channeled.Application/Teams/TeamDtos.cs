using Channeled.Domain.Teams;

namespace Channeled.Application.Teams;

public sealed record CreateTeamRequest(string Name, string? Description);
public sealed record UpdateTeamRequest(string Name, string? Description);
public sealed record TeamDto(Guid Id, string Name, string Slug, string? Description, long QuotaBytes, DateTimeOffset? ArchivedAt, MemberRole? MyRole);
public sealed record TeamMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string Username,
    long? AvatarVersion,
    MemberRole Role,
    DateTimeOffset JoinedAt);
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
public sealed record ChannelMemberDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string Username,
    long? AvatarVersion,
    bool CanWrite,
    bool CanManage);
public sealed record UpsertChannelMemberRequest(Guid UserId, bool CanWrite, bool CanManage);
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
    string Username,
    long? AvatarVersion,
    ChannelJoinRequestStatus Status,
    DateTimeOffset CreatedAt);
public sealed record ApproveChannelJoinRequestRequest(bool CanWrite = true, bool CanManage = false);
public sealed record CreateTeamInvitationRequest(string Email, MemberRole Role);
public sealed record TeamInvitationDto(Guid Id, string Email, MemberRole Role, DateTimeOffset ExpiresAt, string? RawToken);
public sealed record AcceptTeamInvitationRequest(string Token);
