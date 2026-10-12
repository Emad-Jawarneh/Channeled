using Channeled.Application.Abstractions.Results;
using Channeled.Application.Identity;

namespace Channeled.Application.Files;

public interface IFileService
{
    Task<AppResult<FileItemDto>> CreateFolderAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        CreateFolderRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<ItemListResponse>> ListItemsAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        ListItemsQuery query,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> GetItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> RenameItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        RenameItemRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> MoveItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        MoveItemRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult> SoftDeleteItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> RestoreItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<DownloadResult>> OpenDownloadAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid fileId,
        long? rangeStart,
        long? rangeEndInclusive,
        CancellationToken ct = default);

    Task<AppResult<StartUploadResponse>> StartUploadAsync(
        Guid userId,
        StartUploadRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult<UploadPartResponse>> UploadPartAsync(
        Guid userId,
        Guid uploadId,
        int partNumber,
        Stream content,
        CancellationToken ct = default);

    Task<AppResult<IReadOnlyList<UploadPartInfo>>> ListPartsAsync(
        Guid userId,
        Guid uploadId,
        CancellationToken ct = default);

    Task<AppResult<FileItemDto>> CompleteUploadAsync(
        Guid userId,
        Guid uploadId,
        CompleteUploadRequest request,
        RequestContext context,
        CancellationToken ct = default);

    Task<AppResult> AbortUploadAsync(
        Guid userId,
        Guid uploadId,
        RequestContext context,
        CancellationToken ct = default);
}
