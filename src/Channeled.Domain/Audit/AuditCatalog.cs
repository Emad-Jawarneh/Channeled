namespace Channeled.Domain.Audit;

/// <summary>
/// Authoritative M6 audit catalog from AGENTS.md.
/// Every value must be emit-able and covered by a test.
/// </summary>
public static class AuditCatalog
{
    public static IReadOnlyList<AuditEventType> All { get; } =
    [
        AuditEventType.LoginSuccess,
        AuditEventType.LoginFailure,
        AuditEventType.Logout,
        AuditEventType.PasswordChange,
        AuditEventType.PasswordReset,
        AuditEventType.SessionRevoke,
        AuditEventType.UserCreate,
        AuditEventType.UserDisable,
        AuditEventType.UserDelete,
        AuditEventType.TeamCreate,
        AuditEventType.TeamDelete,
        AuditEventType.TeamArchive,
        // InviteCreate/InviteAccept/InviteRevoke are retired (team invitation endpoints return 410); the enum values
        // remain so historical audit rows stay readable, but they are no longer emit-able and are not in the catalog.
        AuditEventType.RoleChange,
        AuditEventType.MemberRemove,
        AuditEventType.MemberLeave,
        AuditEventType.TeamJoinLinkRotated,
        AuditEventType.TeamJoinModeChange,
        AuditEventType.TeamJoinRequestCreate,
        AuditEventType.TeamJoinRequestApprove,
        AuditEventType.TeamJoinRequestReject,
        AuditEventType.TeamJoinRequestWithdraw,
        AuditEventType.ChannelVisibilityChange,
        AuditEventType.ChannelCreate,
        AuditEventType.ChannelChange,
        AuditEventType.ChannelArchive,
        AuditEventType.ChannelDelete,
        AuditEventType.ChannelMessageDelete,
        AuditEventType.ChannelMembershipAdd,
        AuditEventType.ChannelMembershipRemove,
        AuditEventType.FileUpload,
        AuditEventType.FileRename,
        AuditEventType.FileMove,
        AuditEventType.FileCopy,
        AuditEventType.FileDelete,
        AuditEventType.FileRestore,
        AuditEventType.FilePurge,
        AuditEventType.FileDownload,
        AuditEventType.FileShareCreate,
        AuditEventType.FileShareRevoke,
        AuditEventType.FolderCreate,
        AuditEventType.FolderRename,
        AuditEventType.FolderMove,
        AuditEventType.FolderDelete,
        AuditEventType.FolderRestore,
        AuditEventType.FileStar,
        AuditEventType.FileUnstar,
        AuditEventType.QuotaChange,
        AuditEventType.BackupRun,
        AuditEventType.BackupRestore,
        AuditEventType.ConfigChange,
        AuditEventType.EmailChange,
        AuditEventType.EmailChangeStart,
        AuditEventType.EmailChangeCancel,
        AuditEventType.UsernameChange,
        AuditEventType.AvatarChange,
        AuditEventType.AdminEmailCorrection,
        AuditEventType.SmtpTest,
        AuditEventType.SmtpConfigChange,
    ];
}
