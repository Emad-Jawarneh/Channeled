using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;

namespace Channeled.Application.Teams;

public interface ITeamService
{
    Task<AppResult<TeamDto>> CreateTeamAsync(Guid userId, CreateTeamRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult<IReadOnlyList<TeamDto>>> ListMyTeamsAsync(Guid userId, CancellationToken ct = default);
    Task<AppResult<TeamDto>> GetTeamAsync(Guid userId, string slug, CancellationToken ct = default);
    Task<AppResult<TeamDto>> UpdateTeamAsync(Guid userId, string slug, UpdateTeamRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> ArchiveTeamAsync(Guid userId, string slug, RequestContext context, CancellationToken ct = default);
    Task<AppResult> DeleteTeamAsync(Guid userId, string slug, RequestContext context, CancellationToken ct = default);
    Task<AppResult> TransferOwnershipAsync(Guid userId, string slug, Guid newOwnerUserId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<TeamMemberDto>>> ListMembersAsync(Guid userId, string slug, CancellationToken ct = default);
    Task<AppResult> ChangeMemberRoleAsync(Guid userId, string slug, Guid memberUserId, ChangeMemberRoleRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> RemoveMemberAsync(Guid userId, string slug, Guid memberUserId, RequestContext context, CancellationToken ct = default);
    /// <summary>Any non-Owner member may leave; the Owner must transfer ownership first.</summary>
    Task<AppResult> LeaveTeamAsync(Guid userId, string slug, RequestContext context, CancellationToken ct = default);

    // ---- Settings / join mode (Owner for join mode + default role; Admin+ for name/description) ----
    Task<AppResult<UpdateTeamSettingsResult>> UpdateTeamSettingsAsync(Guid userId, string slug, UpdateTeamSettingsRequest request, RequestContext context, CancellationToken ct = default);

    // ---- Join link (Admin+) ----
    Task<AppResult<TeamJoinLinkDto>> GetJoinLinkAsync(Guid userId, string slug, CancellationToken ct = default);
    /// <summary>Returns the current link, creating one if the team has none (idempotent).</summary>
    Task<AppResult<TeamJoinLinkDto>> EnsureJoinLinkAsync(Guid userId, string slug, RequestContext context, CancellationToken ct = default);
    Task<AppResult<TeamJoinLinkDto>> RotateJoinLinkAsync(Guid userId, string slug, RotateTeamJoinLinkRequest request, RequestContext context, CancellationToken ct = default);

    // ---- Join by token (no team slug) ----
    /// <summary><paramref name="userId"/> null = anonymous caller (only team name + join mode are returned).</summary>
    Task<AppResult<TeamJoinInfoDto>> GetJoinInfoAsync(Guid? userId, string token, CancellationToken ct = default);
    Task<AppResult<TeamJoinResultDto>> JoinAsync(Guid userId, string token, RequestContext context, CancellationToken ct = default);

    // ---- Join requests ----
    Task<AppResult<IReadOnlyList<TeamJoinRequestDto>>> ListJoinRequestsAsync(Guid userId, string slug, CancellationToken ct = default);
    Task<AppResult> ApproveJoinRequestAsync(Guid userId, string slug, Guid requestId, ApproveTeamJoinRequestRequest? request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> RejectJoinRequestAsync(Guid userId, string slug, Guid requestId, RequestContext context, CancellationToken ct = default);
    Task<AppResult> WithdrawJoinRequestAsync(Guid userId, Guid requestId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<TeamInvitationDto>> InviteAsync(Guid userId, string slug, CreateTeamInvitationRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult<TeamDto>> AcceptInvitationAsync(Guid userId, AcceptTeamInvitationRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> RevokeInvitationAsync(Guid userId, string slug, Guid invitationId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<ChannelDto>> CreateChannelAsync(Guid userId, string slug, CreateChannelRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult<IReadOnlyList<ChannelDto>>> ListChannelsAsync(Guid userId, string slug, CancellationToken ct = default);
    Task<AppResult<ChannelDto>> GetChannelAsync(Guid userId, string slug, string channelName, CancellationToken ct = default);
    Task<AppResult<ChannelDto>> UpdateChannelAsync(Guid userId, string slug, string channelName, UpdateChannelRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> ArchiveChannelAsync(Guid userId, string slug, string channelName, RequestContext context, CancellationToken ct = default);
    Task<AppResult> DeleteChannelAsync(
        Guid userId,
        string slug,
        string channelName,
        DeleteChannelRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<ChannelMemberDto>>> ListChannelMembersAsync(Guid userId, string slug, string channelName, CancellationToken ct = default);
    Task<AppResult> UpsertChannelMemberAsync(Guid userId, string slug, string channelName, UpsertChannelMemberRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> BulkUpsertChannelMembersAsync(Guid userId, string slug, string channelName, BulkUpsertChannelMembersRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult<SetChannelVisibilityResult>> SetChannelVisibilityAsync(Guid userId, string slug, string channelName, SetChannelVisibilityRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> RemoveChannelMemberAsync(Guid userId, string slug, string channelName, Guid memberUserId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<ChannelJoinInfoDto>> GetChannelJoinInfoAsync(Guid userId, string slug, string channelName, CancellationToken ct = default);
    Task<AppResult<ChannelJoinRequestDto>> RequestChannelJoinAsync(Guid userId, string slug, string channelName, RequestContext context, CancellationToken ct = default);
    Task<AppResult<IReadOnlyList<ChannelJoinRequestDto>>> ListChannelJoinRequestsAsync(Guid userId, string slug, string channelName, CancellationToken ct = default);
    Task<AppResult> ApproveChannelJoinRequestAsync(Guid userId, string slug, string channelName, Guid requestId, ApproveChannelJoinRequestRequest request, RequestContext context, CancellationToken ct = default);
    Task<AppResult> RejectChannelJoinRequestAsync(Guid userId, string slug, string channelName, Guid requestId, RequestContext context, CancellationToken ct = default);
}
