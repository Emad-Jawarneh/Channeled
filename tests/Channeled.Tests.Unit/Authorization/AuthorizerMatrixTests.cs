using Channeled.Domain.Authorization;
using Channeled.Domain.Teams;
using FluentAssertions;

namespace Channeled.Tests.Unit.Authorization;

public class AuthorizerMatrixTests
{
    private readonly Authorizer _auth = new();

    public static IEnumerable<object[]> MatrixCases()
    {
        foreach (MemberRole role in Enum.GetValues<MemberRole>())
        {
            foreach (var visibility in new[] { ChannelVisibility.Open, ChannelVisibility.Private })
            {
                foreach (AuthAction action in Enum.GetValues<AuthAction>())
                {
                    yield return new object[] { role, visibility, action, Expected(role, visibility, action) };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void Role_and_visibility_matrix(MemberRole role, ChannelVisibility visibility, AuthAction action, bool expectedAllow)
    {
        var isChannelMember = role is MemberRole.Member or MemberRole.Viewer;
        // Private channel: non-admin needs membership; we model member/viewer as listed with write for Member
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = action,
            IsTeamMember = true,
            TeamRole = role,
            ChannelExists = NeedsChannel(action),
            ChannelVisibility = visibility,
            IsChannelMember = visibility == ChannelVisibility.Open || role >= MemberRole.Admin || isChannelMember,
            ChannelCanWrite = role >= MemberRole.Member,
            ChannelCanManage = role >= MemberRole.Member && visibility == ChannelVisibility.Private,
            IsFileOwner = true,
        };

        // For private + viewer/member without membership, force hidden for view/browse cases
        if (visibility == ChannelVisibility.Private && role < MemberRole.Admin)
        {
            // Explicit hidden variant covered by separate theory; matrix uses "listed" membership for member/viewer
        }

        var decision = _auth.Authorize(ctx);
        decision.Allowed.Should().Be(expectedAllow, because: $"{role} / {visibility} / {action}");
    }

    [Fact]
    public void Private_channel_hidden_from_non_listed_member()
    {
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = AuthAction.ViewChannel,
            IsTeamMember = true,
            TeamRole = MemberRole.Member,
            ChannelExists = true,
            ChannelVisibility = ChannelVisibility.Private,
            IsChannelMember = false,
        };

        _auth.Authorize(ctx).Allowed.Should().BeFalse();
        _auth.Authorize(ctx).ReasonCode.Should().Be("private_channel_hidden");
    }

    [Fact]
    public void Viewer_never_writes_even_with_channel_write_flag()
    {
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = AuthAction.UploadFile,
            IsTeamMember = true,
            TeamRole = MemberRole.Viewer,
            ChannelExists = true,
            ChannelVisibility = ChannelVisibility.Open,
            IsChannelMember = true,
            ChannelCanWrite = true,
        };

        _auth.Authorize(ctx).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Non_member_denied()
    {
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = AuthAction.ViewTeam,
            IsTeamMember = false,
            TeamRole = null,
        };

        _auth.Authorize(ctx).Allowed.Should().BeFalse();
    }

    private static bool NeedsChannel(AuthAction action) => action is
        AuthAction.ViewChannel or
        AuthAction.BrowseFiles or
        AuthAction.DownloadFile or
        AuthAction.UploadFile or
        AuthAction.RenameOwnFile or
        AuthAction.DeleteOwnFile or
        AuthAction.RenameAnyFile or
        AuthAction.DeleteAnyFile or
        AuthAction.ManageChannelMembership or
        AuthAction.ArchiveChannel or
        AuthAction.DeleteChannel;

    private static bool Expected(MemberRole role, ChannelVisibility visibility, AuthAction action)
    {
        // Matrix assumes channel membership for Member/Viewer on private channels when NeedsChannel
        var channelVisible = visibility == ChannelVisibility.Open
                             || role >= MemberRole.Admin
                             || role is MemberRole.Member or MemberRole.Viewer;

        return action switch
        {
            AuthAction.ViewTeam => true,
            AuthAction.Search => true,
            AuthAction.EditTeamSettings => role >= MemberRole.Admin,
            AuthAction.DeleteTeam => role == MemberRole.Owner,
            AuthAction.TransferOwnership => role == MemberRole.Owner,
            AuthAction.InviteMember => role >= MemberRole.Admin,
            AuthAction.ChangeMemberRole => role >= MemberRole.Admin,
            AuthAction.RemoveMember => role >= MemberRole.Admin,
            AuthAction.CreateChannel => role >= MemberRole.Admin,
            AuthAction.ArchiveChannel => role >= MemberRole.Admin,
            AuthAction.DeleteChannel => role >= MemberRole.Admin,
            AuthAction.ViewChannel => channelVisible,
            AuthAction.BrowseFiles => channelVisible,
            AuthAction.DownloadFile => channelVisible,
            AuthAction.ManageChannelMembership =>
                role >= MemberRole.Admin
                || (visibility == ChannelVisibility.Private && role >= MemberRole.Member),
            AuthAction.UploadFile => role >= MemberRole.Member && channelVisible,
            AuthAction.RenameOwnFile => role >= MemberRole.Member && channelVisible,
            AuthAction.DeleteOwnFile => role >= MemberRole.Member && channelVisible,
            AuthAction.RenameAnyFile => role >= MemberRole.Admin && channelVisible,
            AuthAction.DeleteAnyFile => role >= MemberRole.Admin && channelVisible,
            _ => false,
        };
    }
}
