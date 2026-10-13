using Channeled.Domain.Teams;

namespace Channeled.Infrastructure.Teams;

public sealed class TeamEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public long QuotaBytes { get; set; } = 100L * 1024 * 1024 * 1024;
    public long UsageBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public TeamJoinMode JoinMode { get; set; } = TeamJoinMode.RequestToJoin;
    /// <summary>Role granted to joiners; only Viewer (0) or Member (1) allowed.</summary>
    public MemberRole DefaultJoinRole { get; set; } = MemberRole.Viewer;
    public string? JoinTokenCiphertext { get; set; }
    public string? JoinTokenHash { get; set; }
    public string? JoinTokenPrefix { get; set; }
    public DateTimeOffset? JoinTokenExpiresAt { get; set; }

    public List<TeamMemberEntity> Members { get; set; } = [];
    public List<ChannelEntity> Channels { get; set; } = [];
    public List<TeamJoinRequestEntity> JoinRequests { get; set; } = [];
}

public sealed class TeamJoinRequestEntity
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public TeamJoinRequestStatus Status { get; set; } = TeamJoinRequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? CooldownUntil { get; set; }

    public TeamEntity? Team { get; set; }
}

public sealed class TeamMemberEntity
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public MemberRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }

    public TeamEntity? Team { get; set; }
}

public sealed class ChannelEntity
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ChannelKind Kind { get; set; } = ChannelKind.File;
    public ChannelVisibility Visibility { get; set; } = ChannelVisibility.Everyone;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public TeamEntity? Team { get; set; }
    public List<ChannelMemberEntity> Members { get; set; } = [];
    public List<ChannelJoinRequestEntity> JoinRequests { get; set; } = [];
}

public sealed class ChannelMemberEntity
{
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>When true the grant is view-only; default false means the member may write (subject to team role).</summary>
    public bool ReadOnly { get; set; }

    public ChannelEntity? Channel { get; set; }
}

public sealed class ChannelJoinRequestEntity
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    public ChannelJoinRequestStatus Status { get; set; } = ChannelJoinRequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }

    public ChannelEntity? Channel { get; set; }
}

public sealed class TeamInvitationEntity
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Email { get; set; } = "";
    public MemberRole Role { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid InvitedByUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public TeamEntity? Team { get; set; }
}
