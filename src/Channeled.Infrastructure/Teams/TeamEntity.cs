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

    public List<TeamMemberEntity> Members { get; set; } = [];
    public List<ChannelEntity> Channels { get; set; } = [];
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
    public ChannelVisibility Visibility { get; set; } = ChannelVisibility.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public TeamEntity? Team { get; set; }
    public List<ChannelMemberEntity> Members { get; set; } = [];
}

public sealed class ChannelMemberEntity
{
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    public bool CanWrite { get; set; }
    public bool CanManage { get; set; }

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
