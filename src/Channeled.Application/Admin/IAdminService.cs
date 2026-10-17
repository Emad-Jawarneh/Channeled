using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;

namespace Channeled.Application.Admin;

public interface IAdminService
{
    Task<AppResult<AdminDashboardDto>> GetDashboardAsync(Guid actorId, CancellationToken ct = default);

    Task<AppResult<AdminHealthDto>> GetHealthAsync(Guid actorId, CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<AdminUserDto>>> ListUsersAsync(Guid actorId, CancellationToken ct = default);

    Task<AppResult<AdminUserDto>> GetUserAsync(Guid actorId, Guid userId, CancellationToken ct = default);

    Task<AppResult<AdminUserDto>> CreateUserAsync(
        Guid actorId,
        CreateAdminUserRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<AdminUserDto>> UpdateUserAsync(
        Guid actorId,
        Guid userId,
        UpdateAdminUserRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult> DisableUserAsync(Guid actorId, Guid userId, RequestContext context, CancellationToken ct = default);

    Task<AppResult> EnableUserAsync(Guid actorId, Guid userId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<UserDeletePreviewDto>> GetUserDeletePreviewAsync(Guid actorId, Guid userId, CancellationToken ct = default);

    Task<AppResult> DeleteUserAsync(
        Guid actorId,
        Guid userId,
        DeleteUserRequest request,
        RequestContext context,
        CancellationToken ct = default);

    /// <summary>Advances late-phase user deletes (hosted sweep).</summary>
    Task<int> SweepStuckUserDeletesAsync(CancellationToken ct = default);

    Task<AppResult> ResetUserPasswordAsync(
        Guid actorId,
        Guid userId,
        ResetUserPasswordRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<AdminTeamDto>>> ListTeamsAsync(Guid actorId, CancellationToken ct = default);

    Task<AppResult<AdminTeamDto>> UpdateTeamQuotaAsync(
        Guid actorId,
        Guid teamId,
        UpdateTeamQuotaRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<AuditListResponse>> ListAuditAsync(Guid actorId, AuditListQuery query, CancellationToken ct = default);

    Task<AppResult<string>> ExportAuditAsync(
        Guid actorId,
        AuditListQuery query,
        string format,
        CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<AdminBackupDto>>> ListBackupsAsync(Guid actorId, CancellationToken ct = default);

    Task<AppResult<AdminBackupDto>> RunBackupAsync(Guid actorId, RequestContext context, CancellationToken ct = default);

    Task<AppResult> RecordConfigChangeAsync(
        Guid actorId,
        string key,
        string value,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult> PurgeFileAsync(Guid actorId, Guid fileId, RequestContext context, CancellationToken ct = default);

    Task<AppResult> RecordDownloadAsync(Guid actorId, Guid fileId, RequestContext context, CancellationToken ct = default);

    Task<AppResult> RecordBackupRestoreAsync(Guid actorId, Guid backupId, RequestContext context, CancellationToken ct = default);
}
