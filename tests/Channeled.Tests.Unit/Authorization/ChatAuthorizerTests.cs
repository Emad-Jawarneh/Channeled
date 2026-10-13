using Channeled.Domain.Authorization;
using Channeled.Domain.Teams;
using FluentAssertions;

namespace Channeled.Tests.Unit.Authorization;

/// <summary>
/// Exhaustive table for the chat actions: every team role x channel visibility x channel-member flag x can_write x
/// author flag x archived flag, with an explicitly written expectation (not a re-implementation of the Authorizer).
/// </summary>
public class ChatAuthorizerTests
{
    private static readonly AuthAction[] ChatActions =
    [
        AuthAction.PostChannelMessage,
        AuthAction.DeleteOwnChannelMessage,
        AuthAction.DeleteAnyChannelMessage,
    ];

    private readonly Authorizer _auth = new();

    public static IEnumerable<object[]> Table()
    {
        foreach (var role in Enum.GetValues<MemberRole>())
            foreach (var visibility in Enum.GetValues<ChannelVisibility>())
                foreach (var isChannelMember in new[] { false, true })
                    foreach (var canWrite in new[] { false, true })
                        foreach (var isAuthor in new[] { false, true })
                            foreach (var archived in new[] { false, true })
                                foreach (var action in ChatActions)
                                {
                                    yield return
                                    [
                                        role, visibility, isChannelMember, canWrite, isAuthor, archived, action,
                Expected(role, visibility, isChannelMember, canWrite, isAuthor, archived, action),
            ];
                                }
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Chat_action_matrix(
        MemberRole role,
        ChannelVisibility visibility,
        bool isChannelMember,
        bool canWrite,
        bool isAuthor,
        bool archived,
        AuthAction action,
        bool expectedAllow)
    {
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = action,
            IsTeamMember = true,
            TeamRole = role,
            ChannelExists = true,
            ChannelVisibility = visibility,
            ChannelArchived = archived,
            IsChannelMember = isChannelMember,
            ChannelCanWrite = canWrite,
            IsMessageAuthor = isAuthor,
        };

        _auth.Authorize(ctx).Allowed.Should().Be(
            expectedAllow,
            because: $"{role}/{visibility} member={isChannelMember} can_write={canWrite} author={isAuthor} archived={archived} {action}");
    }

    [Theory]
    [InlineData(MemberRole.Member, true, false, "channel_write_denied")]
    [InlineData(MemberRole.Member, false, true, "private_channel_hidden")]
    public void Private_channel_post_denial_reasons(MemberRole role, bool isChannelMember, bool canWrite, string reason)
    {
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = AuthAction.PostChannelMessage,
            IsTeamMember = true,
            TeamRole = role,
            ChannelExists = true,
            ChannelVisibility = ChannelVisibility.Restricted,
            IsChannelMember = isChannelMember,
            ChannelCanWrite = canWrite,
        };

        var decision = _auth.Authorize(ctx);
        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(reason);
    }

    [Fact]
    public void Archived_channel_denies_post_with_channel_archived_reason()
    {
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = AuthAction.PostChannelMessage,
            IsTeamMember = true,
            TeamRole = MemberRole.Owner,
            ChannelExists = true,
            ChannelVisibility = ChannelVisibility.Everyone,
            ChannelArchived = true,
        };

        _auth.Authorize(ctx).ReasonCode.Should().Be("channel_archived");
    }

    [Fact]
    public void Archived_team_denies_post()
    {
        var ctx = new AuthorizationContext
        {
            UserId = Guid.NewGuid(),
            Action = AuthAction.PostChannelMessage,
            IsTeamMember = true,
            TeamRole = MemberRole.Owner,
            TeamArchived = true,
            ChannelExists = true,
            ChannelVisibility = ChannelVisibility.Everyone,
        };

        _auth.Authorize(ctx).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Non_team_member_is_denied_every_chat_action()
    {
        foreach (var action in ChatActions)
        {
            var ctx = new AuthorizationContext
            {
                UserId = Guid.NewGuid(),
                Action = action,
                IsTeamMember = false,
                ChannelExists = true,
                ChannelVisibility = ChannelVisibility.Everyone,
                IsMessageAuthor = true,
            };

            _auth.Authorize(ctx).Allowed.Should().BeFalse(because: action.ToString());
        }
    }

    private static bool Expected(
        MemberRole role,
        ChannelVisibility visibility,
        bool isChannelMember,
        bool canWrite,
        bool isAuthor,
        bool archived,
        AuthAction action)
    {
        // Can this role write to the channel at all (ignoring archive state)?
        var canWriteHere = role switch
        {
            MemberRole.Viewer => false,                       // viewers never write, even in private channels
            MemberRole.Member => visibility == ChannelVisibility.Everyone
                                 || (isChannelMember && canWrite),   // private: listed AND can_write
            MemberRole.Admin => true,                         // owners/admins always reach private channels
            MemberRole.Owner => true,
            _ => false,
        };

        return action switch
        {
            AuthAction.PostChannelMessage => canWriteHere && !archived,
            AuthAction.DeleteOwnChannelMessage => canWriteHere && isAuthor,
            AuthAction.DeleteAnyChannelMessage => role is MemberRole.Admin or MemberRole.Owner,
            _ => false,
        };
    }
}
