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
}
