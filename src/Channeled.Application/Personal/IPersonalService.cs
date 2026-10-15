using Channeled.Application.Abstractions.Results;
using Channeled.Application.Files;
using Channeled.Application.Identity;

namespace Channeled.Application.Personal;

public interface IPersonalService
{
    Task<AppResult<PersonalStorageDto>> GetStorageAsync(Guid userId, CancellationToken ct = default);

    Task<AppResult<FileItemDto>> CreateFolderAsync(
        Guid userId,
        CreateFolderRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<ItemListResponse>> ListItemsAsync(
        Guid userId,
        ListItemsQuery query,
        CancellationToken ct = default);

    Task<AppResult<ItemListResponse>> ListRecentAsync(
        Guid userId,
        string? cursor,
        int limit,
        CancellationToken ct = default);

    Task<AppResult<ItemListResponse>> ListStarredAsync(
        Guid userId,
        string? cursor,
        int limit,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> GetItemAsync(Guid userId, Guid itemId, CancellationToken ct = default);

    Task<AppResult<FileItemDto>> RenameItemAsync(
        Guid userId,
        Guid itemId,
        RenameItemRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> MoveItemAsync(
        Guid userId,
        Guid itemId,
        MoveItemRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult> SoftDeleteItemAsync(
        Guid userId,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> RestoreItemAsync(
        Guid userId,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default);

    /// <summary>Locates one of the caller's own personal items (live or trashed); 404 for anyone else, admins included.</summary>
    Task<AppResult<ItemLocationDto>> LocateItemAsync(Guid userId, Guid itemId, CancellationToken ct = default);

    /// <summary>Owner-only download check for one personal file; does not open the object stream.</summary>
    Task<AppResult<DownloadTarget>> ResolveDownloadAsync(
        Guid userId,
        Guid fileId,
        CancellationToken ct = default);

    Task<AppResult<StartUploadResponse>> StartUploadAsync(
        Guid userId,
        StartPersonalUploadRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult> StarAsync(Guid userId, Guid fileId, RequestContext context, CancellationToken ct = default);

    Task<AppResult> UnstarAsync(Guid userId, Guid fileId, RequestContext context, CancellationToken ct = default);

    Task<AppResult<FileItemDto>> CopyToChannelAsync(
        Guid userId,
        Guid fileId,
        CopyToChannelRequest request,
        RequestContext context,
        CancellationToken ct = default);
}
