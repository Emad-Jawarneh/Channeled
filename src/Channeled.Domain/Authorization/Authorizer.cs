using Channeled.Domain.Teams;

namespace Channeled.Domain.Authorization;

/// <summary>
/// Single authorization evaluator for all team/channel/file decisions.
/// Deny by default. Callers map Deny → 404 for resource lookups.
/// </summary>
public sealed class Authorizer : IAuthorizer
{
    public AuthorizationDecision Authorize(AuthorizationContext context)
    {
        if (!context.IsTeamMember || context.TeamRole is null)
        {
            return AuthorizationDecision.Deny("not_team_member");
        }

        if (context.TeamArchived && context.Action is not AuthAction.ViewTeam and not AuthAction.Search)
        {
            // Still allow read-ish of archived teams for members
        }

        var role = context.TeamRole.Value;

        return context.Action switch
        {
            AuthAction.ViewTeam => AllowIf(true),
            AuthAction.Search => AllowIf(true),
            AuthAction.EditTeamSettings => AllowIf(role >= MemberRole.Admin, "role_insufficient"),
            AuthAction.DeleteTeam => AllowIf(role == MemberRole.Owner, "owner_required"),
            AuthAction.TransferOwnership => AllowIf(role == MemberRole.Owner, "owner_required"),
            AuthAction.InviteMember => AllowIf(role >= MemberRole.Admin, "role_insufficient"),
            AuthAction.ChangeMemberRole => AllowIf(role >= MemberRole.Admin, "role_insufficient"),
            AuthAction.RemoveMember => AllowIf(role >= MemberRole.Admin, "role_insufficient"),
            AuthAction.CreateChannel => AllowIf(role >= MemberRole.Admin, "role_insufficient"),
            AuthAction.ArchiveChannel => AllowIf(role >= MemberRole.Admin, "role_insufficient"),
            AuthAction.DeleteChannel => AllowIf(role >= MemberRole.Admin, "role_insufficient"),
            AuthAction.ViewChannel => AuthorizeChannelAccess(context, writeRequired: false),
            AuthAction.BrowseFiles => AuthorizeChannelAccess(context, writeRequired: false),
            AuthAction.DownloadFile => AuthorizeChannelAccess(context, writeRequired: false),
            AuthAction.ManageChannelMembership => AuthorizeManageChannelMembership(context),
            AuthAction.UploadFile => AuthorizeChannelWrite(context, minRole: MemberRole.Member),
            AuthAction.RenameOwnFile => AuthorizeOwnFileWrite(context),
            AuthAction.DeleteOwnFile => AuthorizeOwnFileWrite(context),
            AuthAction.RenameAnyFile => AuthorizeAnyFileWrite(context),
            AuthAction.DeleteAnyFile => AuthorizeAnyFileWrite(context),
            _ => AuthorizationDecision.Deny("unknown_action"),
        };
    }

    private static AuthorizationDecision AuthorizeChannelAccess(AuthorizationContext context, bool writeRequired)
    {
        if (!context.ChannelExists)
        {
            return AuthorizationDecision.Deny("channel_not_found");
        }

        var role = context.TeamRole!.Value;

        if (context.ChannelVisibility == ChannelVisibility.Open)
        {
            if (writeRequired && role == MemberRole.Viewer)
            {
                return AuthorizationDecision.Deny("viewer_cannot_write");
            }

            return AuthorizationDecision.Allow();
        }

        // Private: Owners/Admins always see; others need channel membership
        if (role >= MemberRole.Admin)
        {
            return AuthorizationDecision.Allow();
        }

        if (!context.IsChannelMember)
        {
            return AuthorizationDecision.Deny("private_channel_hidden");
        }

        if (writeRequired)
        {
            if (role == MemberRole.Viewer)
            {
                return AuthorizationDecision.Deny("viewer_cannot_write");
            }

            if (!context.ChannelCanWrite)
            {
                return AuthorizationDecision.Deny("channel_write_denied");
            }
        }

        return AuthorizationDecision.Allow();
    }

    private static AuthorizationDecision AuthorizeManageChannelMembership(AuthorizationContext context)
    {
        if (!context.ChannelExists)
        {
            return AuthorizationDecision.Deny("channel_not_found");
        }

        var role = context.TeamRole!.Value;
        if (role >= MemberRole.Admin)
        {
            return AuthorizationDecision.Allow();
        }

        if (context.ChannelVisibility == ChannelVisibility.Private
            && context.IsChannelMember
            && context.ChannelCanManage
            && role >= MemberRole.Member)
        {
            return AuthorizationDecision.Allow();
        }

        return AuthorizationDecision.Deny("cannot_manage_channel_membership");
    }

    private static AuthorizationDecision AuthorizeChannelWrite(AuthorizationContext context, MemberRole minRole)
    {
        if (context.TeamRole!.Value < minRole)
        {
            return AuthorizationDecision.Deny("role_insufficient");
        }

        if (context.TeamRole == MemberRole.Viewer)
        {
            return AuthorizationDecision.Deny("viewer_cannot_write");
        }

        return AuthorizeChannelAccess(context, writeRequired: true);
    }

    private static AuthorizationDecision AuthorizeOwnFileWrite(AuthorizationContext context)
    {
        if (context.TeamRole!.Value < MemberRole.Member)
        {
            return AuthorizationDecision.Deny("role_insufficient");
        }

        var channel = AuthorizeChannelAccess(context, writeRequired: true);
        if (!channel.Allowed)
        {
            return channel;
        }

        return context.IsFileOwner
            ? AuthorizationDecision.Allow()
            : AuthorizationDecision.Deny("not_file_owner");
    }

    private static AuthorizationDecision AuthorizeAnyFileWrite(AuthorizationContext context)
    {
        if (context.TeamRole!.Value < MemberRole.Admin)
        {
            return AuthorizationDecision.Deny("role_insufficient");
        }

        return AuthorizeChannelAccess(context, writeRequired: true);
    }

    private static AuthorizationDecision AllowIf(bool condition, string denyReason = "denied") =>
        condition ? AuthorizationDecision.Allow() : AuthorizationDecision.Deny(denyReason);
}
