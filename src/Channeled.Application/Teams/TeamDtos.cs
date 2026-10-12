using Channeled.Domain.Teams;

namespace Channeled.Application.Teams;

public sealed record CreateTeamRequest(string Name, string? Description);
public sealed record UpdateTeamRequest(string Name, string? Description);
public sealed record TeamDto(Guid Id, string Name, string Slug, string? Description, long QuotaBytes, DateTimeOffset? ArchivedAt, MemberRole? MyRole);
public sealed record TeamMemberDto(Guid UserId, string Email, string DisplayName, MemberRole Role, DateTimeOffset JoinedAt);
public sealed record ChangeMemberRoleRequest(MemberRole Role);
public sealed record CreateChannelRequest(string Name, string? Description, ChannelVisibility Visibility);
public sealed record UpdateChannelRequest(string? Name, string? Description);
public sealed record ChannelDto(Guid Id, Guid TeamId, string Name, string? Description, ChannelVisibility Visibility, DateTimeOffset? ArchivedAt);
public sealed record ChannelMemberDto(Guid UserId, string Email, string DisplayName, bool CanWrite, bool CanManage);
public sealed record UpsertChannelMemberRequest(Guid UserId, bool CanWrite, bool CanManage);
public sealed record CreateTeamInvitationRequest(string Email, MemberRole Role);
public sealed record TeamInvitationDto(Guid Id, string Email, MemberRole Role, DateTimeOffset ExpiresAt, string? RawToken);
public sealed record AcceptTeamInvitationRequest(string Token);
