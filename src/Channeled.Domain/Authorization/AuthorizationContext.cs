using Channeled.Domain.Teams;

namespace Channeled.Domain.Authorization;

/// <summary>
/// Snapshot of membership facts for one Authorize() call.
/// Built by Infrastructure per request (cached only for the request).
/// </summary>
public sealed class AuthorizationContext
{
    public required Guid UserId { get; init; }
    public required AuthAction Action { get; init; }

    public bool IsTeamMember { get; init; }
    public MemberRole? TeamRole { get; init; }
    public bool TeamArchived { get; init; }

    public bool ChannelExists { get; init; }
    public ChannelVisibility? ChannelVisibility { get; init; }
    public bool ChannelArchived { get; init; }
    public bool IsChannelMember { get; init; }
    public bool ChannelCanWrite { get; init; }
    public bool ChannelCanManage { get; init; }

    public bool IsFileOwner { get; init; }

    /// <summary>True when the acting user owns the personal-space resource under evaluation.</summary>
    public bool IsPersonalOwner { get; init; }

    /// <summary>Platform admin flag — must not grant personal-space access.</summary>
    public bool IsPlatformAdmin { get; init; }
}
