namespace Channeled.Domain.Authorization;

public enum AuthAction
{
    ViewTeam,
    EditTeamSettings,
    DeleteTeam,
    TransferOwnership,
    InviteMember,
    ChangeMemberRole,
    RemoveMember,
    ViewChannel,
    CreateChannel,
    ArchiveChannel,
    DeleteChannel,
    ManageChannelMembership,
    BrowseFiles,
    DownloadFile,
    UploadFile,
    RenameOwnFile,
    DeleteOwnFile,
    RenameAnyFile,
    DeleteAnyFile,
    Search,
    /// <summary>Browse/download/star personal space. Owner only; platform admin does not bypass.</summary>
    AccessPersonal,
    /// <summary>Upload/rename/move/delete/copy-from personal space. Owner only.</summary>
    WritePersonal,
}
