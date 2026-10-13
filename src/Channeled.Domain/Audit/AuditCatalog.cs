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
        AuditEventType.InviteCreate,
        AuditEventType.InviteAccept,
        AuditEventType.InviteRevoke,
        AuditEventType.RoleChange,
        AuditEventType.MemberRemove,
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
    ];
}
