using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Files;
using Channeled.Domain.Identity;
using Channeled.Domain.Storage;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.PublicLinks;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Files;

public sealed class FileService(
    ApplicationDbContext db,
    IAuthorizer authorizer,
    IFileStore store,
    IClock clock,
    IOptions<UploadLimitsOptions> limitsOptions,
    IChannelNotifier channelNotifier,
    ChannelWriteGuard channelWriteGuard) : IFileService
{
    private readonly UploadLimitsOptions _limits = limitsOptions.Value;

    public async Task<AppResult<FileItemDto>> CreateFolderAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        CreateFolderRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (!Allow(userId, AuthAction.UploadFile, channel))
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (channel.Channel.Kind == ChannelKind.Text)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Text channels do not store files.");
        }

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > FolderRules.MaxNameLength)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Folder name is required.");
        }

        var depthResult = await EnsureFolderDepthAsync(channel.Channel.Id, request.ParentFolderId, ct);
        if (depthResult is not null)
        {
            return AppResult<FileItemDto>.Fail(depthResult.Value.Code, depthResult.Value.Message);
        }

        if (await HasSiblingConflictAsync(channel.Channel.Id, request.ParentFolderId, name, excludeFileId: null, excludeFolderId: null, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        var now = clock.UtcNow;
        var folder = new FolderEntity
        {
            Id = UuidV7.NewGuid(),
            ChannelId = channel.Channel.Id,
            ParentId = request.ParentFolderId,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var guard = await channelWriteGuard.EnsureSharedAsync(channel.Channel.Id, ct);
        if (!guard.Succeeded)
        {
            return AppResult<FileItemDto>.Fail(guard.Code!, guard.Message!);
        }

        db.Folders.Add(folder);
        await WriteAuditAsync(userId, AuditEventType.FolderCreate, "folder", folder.Id.ToString(), channel.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        channelNotifier.NotifyFilesChanged(channel.Channel.Id);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult<ItemListResponse>> ListItemsAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        ListItemsQuery query,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<ItemListResponse>();
        }

        if (!Allow(userId, AuthAction.BrowseFiles, channel))
        {
            return NotFoundChannel<ItemListResponse>();
        }

        if (channel.Channel.Kind == ChannelKind.Text)
        {
            return AppResult<ItemListResponse>.Fail(ErrorCodes.Validation, "Text channels do not store files.");
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var trashOnly = query.TrashOnly;
        var folderId = query.FolderId;

        var folders = await db.Folders.AsNoTracking()
            .Where(f => f.ChannelId == channel.Channel.Id)
            .Where(f => trashOnly ? f.DeletedAt != null : f.DeletedAt == null)
            .Where(f => trashOnly || f.ParentId == folderId)
            .ToListAsync(ct);

        var filesQuery = db.Files.AsNoTracking()
            .Where(f => f.ChannelId == channel.Channel.Id)
            .Where(f => trashOnly ? f.DeletedAt != null : f.DeletedAt == null)
            .Where(f => trashOnly || f.FolderId == folderId);

        if (query.UploaderId is Guid uploader)
        {
            filesQuery = filesQuery.Where(f => f.UploadedBy == uploader);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim().ToLowerInvariant();
            filesQuery = filesQuery.Where(f => f.ContentType.ToLower().StartsWith(type));
        }

        var files = await filesQuery.ToListAsync(ct);

        IEnumerable<FileItemDto> items = folders.Select(ToFolderDto).Concat(files.Select(ToFileDto));

        items = (query.Sort?.ToLowerInvariant()) switch
        {
            "name" or "name_asc" => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "name_desc" => items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "size" or "size_asc" => items.OrderBy(i => i.SizeBytes ?? -1),
            "size_desc" => items.OrderByDescending(i => i.SizeBytes ?? -1),
            "created" or "created_asc" => items.OrderBy(i => i.CreatedAt),
            "created_desc" => items.OrderByDescending(i => i.CreatedAt),
            "updated" or "updated_asc" => items.OrderBy(i => i.UpdatedAt).ThenBy(i => i.Id),
            "updated_desc" => items.OrderByDescending(i => i.UpdatedAt).ThenByDescending(i => i.Id),
            _ => items.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Id),
        };

        if (!string.IsNullOrWhiteSpace(query.Cursor) && TryDecodeCursor(query.Cursor, out var cursorTime, out var cursorId))
        {
            items = items.Where(i =>
                i.UpdatedAt < cursorTime
                || (i.UpdatedAt == cursorTime && i.Id.CompareTo(cursorId) < 0));
        }

        var page = items.Take(limit + 1).ToList();
        string? next = null;
        if (page.Count > limit)
        {
            page = page.Take(limit).ToList();
            var last = page[^1];
            next = EncodeCursor(last.UpdatedAt, last.Id);
        }

        return AppResult<ItemListResponse>.Ok(new ItemListResponse(page, next));
    }

    public async Task<AppResult<FileItemDto>> GetItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (!Allow(userId, AuthAction.BrowseFiles, channel))
        {
            return NotFoundChannel<FileItemDto>();
        }

        var file = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id, ct);
        if (file is not null)
        {
            return AppResult<FileItemDto>.Ok(ToFileDto(file));
        }

        var folder = await db.Folders.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id, ct);
        if (folder is not null)
        {
            return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
        }

        return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Item not found.");
    }

    public async Task<AppResult<FileItemDto>> RenameItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        RenameItemRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<FileItemDto>();
        }

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > FolderRules.MaxNameLength)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Name is required.");
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null, ct);
        if (file is not null)
        {
            if (!AllowFileWrite(userId, channel, file.UploadedBy))
            {
                return NotFoundChannel<FileItemDto>();
            }

            if (await HasSiblingConflictAsync(channel.Channel.Id, file.FolderId, name, file.Id, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }

            file.Name = name;
            file.UpdatedAt = clock.UtcNow;
            await WriteAuditAsync(userId, AuditEventType.FileRename, "file", file.Id.ToString(), channel.Team.Id, context, ct);
            await db.SaveChangesAsync(ct);
            channelNotifier.NotifyFilesChanged(channel.Channel.Id);
            return AppResult<FileItemDto>.Ok(ToFileDto(file));
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null, ct);
        if (folder is null)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Item not found.");
        }

        if (!Allow(userId, AuthAction.UploadFile, channel))
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (await HasSiblingConflictAsync(channel.Channel.Id, folder.ParentId, name, null, folder.Id, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        folder.Name = name;
        folder.UpdatedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.FolderRename, "folder", folder.Id.ToString(), channel.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        channelNotifier.NotifyFilesChanged(channel.Channel.Id);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult<FileItemDto>> MoveItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        MoveItemRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (!string.IsNullOrWhiteSpace(request.TargetChannelName)
            && !string.Equals(request.TargetChannelName.Trim(), channel.Channel.Name, StringComparison.OrdinalIgnoreCase))
        {
            return await MoveFileAcrossChannelsAsync(
                userId,
                channel,
                itemId,
                request with { TargetChannelName = request.TargetChannelName.Trim() },
                context,
                ct);
        }

        var depthResult = await EnsureFolderDepthAsync(channel.Channel.Id, request.TargetFolderId, ct);
        if (depthResult is not null)
        {
            return AppResult<FileItemDto>.Fail(depthResult.Value.Code, depthResult.Value.Message);
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null, ct);
        if (file is not null)
        {
            if (!AllowFileWrite(userId, channel, file.UploadedBy))
            {
                return NotFoundChannel<FileItemDto>();
            }

            if (await HasSiblingConflictAsync(channel.Channel.Id, request.TargetFolderId, file.Name, file.Id, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }

            await using var fileTx = await db.Database.BeginTransactionAsync(ct);
            var fileGuard = await channelWriteGuard.EnsureSharedAsync(channel.Channel.Id, ct);
            if (!fileGuard.Succeeded)
            {
                return AppResult<FileItemDto>.Fail(fileGuard.Code!, fileGuard.Message!);
            }

            file.FolderId = request.TargetFolderId;
            file.UpdatedAt = clock.UtcNow;
            await WriteAuditAsync(userId, AuditEventType.FileMove, "file", file.Id.ToString(), channel.Team.Id, context, ct);
            await db.SaveChangesAsync(ct);
            await fileTx.CommitAsync(ct);
            channelNotifier.NotifyFilesChanged(channel.Channel.Id);
            return AppResult<FileItemDto>.Ok(ToFileDto(file));
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null, ct);
        if (folder is null)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Item not found.");
        }

        if (!Allow(userId, AuthAction.UploadFile, channel))
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (request.TargetFolderId == folder.Id || await IsDescendantFolderAsync(folder.Id, request.TargetFolderId, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Cannot move a folder into itself.");
        }

        if (await HasSiblingConflictAsync(channel.Channel.Id, request.TargetFolderId, folder.Name, null, folder.Id, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        await using var folderTx = await db.Database.BeginTransactionAsync(ct);
        var folderGuard = await channelWriteGuard.EnsureSharedAsync(channel.Channel.Id, ct);
        if (!folderGuard.Succeeded)
        {
            return AppResult<FileItemDto>.Fail(folderGuard.Code!, folderGuard.Message!);
        }

        folder.ParentId = request.TargetFolderId;
        folder.UpdatedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.FolderMove, "folder", folder.Id.ToString(), channel.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await folderTx.CommitAsync(ct);
        channelNotifier.NotifyFilesChanged(channel.Channel.Id);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult> SoftDeleteItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel();
        }

        var now = clock.UtcNow;
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null, ct);
        if (file is not null)
        {
            if (!AllowFileWrite(userId, channel, file.UploadedBy, delete: true))
            {
                return NotFoundChannel();
            }

            file.DeletedAt = now;
            file.UpdatedAt = now;
            var trashedFileId = file.Id;
            await ShareLinkRevoker.RevokeAsync(
                db, clock, l => l.FileId == trashedFileId, ShareLinkRevoker.ReasonTrash, userId, context, ct);
            await WriteAuditAsync(userId, AuditEventType.FileDelete, "file", file.Id.ToString(), channel.Team.Id, context, ct);
            await db.SaveChangesAsync(ct);
            channelNotifier.NotifyFilesChanged(channel.Channel.Id);
            return AppResult.Ok();
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null, ct);
        if (folder is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Item not found.");
        }

        if (!Allow(userId, AuthAction.UploadFile, channel))
        {
            return NotFoundChannel();
        }

        folder.DeletedAt = now;
        folder.UpdatedAt = now;

        // Public links to any file nested under the trashed folder must die with it (same SaveChanges).
        var nestedFolderIds = await CollectDescendantFolderIdsAsync(folder.Id, ct);
        var nestedFileIds = await db.Files.AsNoTracking()
            .Where(f => f.ChannelId == channel.Channel.Id && f.FolderId != null && nestedFolderIds.Contains(f.FolderId.Value))
            .Select(f => f.Id)
            .ToListAsync(ct);
        if (nestedFileIds.Count > 0)
        {
            await ShareLinkRevoker.RevokeAsync(
                db,
                clock,
                l => l.FileId != null && nestedFileIds.Contains(l.FileId.Value),
                ShareLinkRevoker.ReasonFolderTrash,
                userId,
                context,
                ct);
        }

        await WriteAuditAsync(userId, AuditEventType.FolderDelete, "folder", folder.Id.ToString(), channel.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        channelNotifier.NotifyFilesChanged(channel.Channel.Id);
        return AppResult.Ok();
    }

    public async Task<AppResult<FileItemDto>> RestoreItemAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<FileItemDto>();
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt != null, ct);
        if (file is not null)
        {
            if (!AllowFileWrite(userId, channel, file.UploadedBy, delete: true))
            {
                return NotFoundChannel<FileItemDto>();
            }

            if (await HasSiblingConflictAsync(channel.Channel.Id, file.FolderId, file.Name, file.Id, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }

            await using var fileTx = await db.Database.BeginTransactionAsync(ct);
            var fileGuard = await channelWriteGuard.EnsureSharedAsync(channel.Channel.Id, ct);
            if (!fileGuard.Succeeded)
            {
                return AppResult<FileItemDto>.Fail(fileGuard.Code!, fileGuard.Message!);
            }

            file.DeletedAt = null;
            file.UpdatedAt = clock.UtcNow;
            await WriteAuditAsync(userId, AuditEventType.FileRestore, "file", file.Id.ToString(), channel.Team.Id, context, ct);
            await db.SaveChangesAsync(ct);
            await fileTx.CommitAsync(ct);
            channelNotifier.NotifyFilesChanged(channel.Channel.Id);
            return AppResult<FileItemDto>.Ok(ToFileDto(file));
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.ChannelId == channel.Channel.Id && f.DeletedAt != null, ct);
        if (folder is null)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Item not found.");
        }

        if (!Allow(userId, AuthAction.UploadFile, channel))
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (await HasSiblingConflictAsync(channel.Channel.Id, folder.ParentId, folder.Name, null, folder.Id, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        await using var folderTx = await db.Database.BeginTransactionAsync(ct);
        var folderGuard = await channelWriteGuard.EnsureSharedAsync(channel.Channel.Id, ct);
        if (!folderGuard.Succeeded)
        {
            return AppResult<FileItemDto>.Fail(folderGuard.Code!, folderGuard.Message!);
        }

        folder.DeletedAt = null;
        folder.UpdatedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.FolderRestore, "folder", folder.Id.ToString(), channel.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await folderTx.CommitAsync(ct);
        channelNotifier.NotifyFilesChanged(channel.Channel.Id);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult<DownloadResult>> OpenDownloadAsync(
        Guid userId,
        string teamSlug,
        string channelName,
        Guid fileId,
        long? rangeStart,
        long? rangeEndInclusive,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, teamSlug, channelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<DownloadResult>();
        }

        if (!Allow(userId, AuthAction.DownloadFile, channel))
        {
            return NotFoundChannel<DownloadResult>();
        }

        var file = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null, ct);
        if (file is null)
        {
            return AppResult<DownloadResult>.Fail(ErrorCodes.NotFound, "File not found.");
        }

        var start = rangeStart ?? 0;
        var end = rangeEndInclusive ?? (file.SizeBytes - 1);
        if (file.SizeBytes == 0)
        {
            start = 0;
            end = -1;
        }
        else
        {
            if (start < 0 || start >= file.SizeBytes || end < start)
            {
                return AppResult<DownloadResult>.Fail(ErrorCodes.Validation, "Invalid byte range.");
            }

            end = Math.Min(end, file.SizeBytes - 1);
        }

        Stream content = file.SizeBytes == 0
            ? new MemoryStream([])
            : await store.OpenReadAsync(file.StorageKey, start, end, ct);

        var partial = rangeStart is not null || rangeEndInclusive is not null;
        return AppResult<DownloadResult>.Ok(new DownloadResult(
            content,
            file.Name,
            file.ContentType,
            file.SizeBytes,
            start,
            Math.Max(end, 0),
            partial));
    }

    public async Task<AppResult<StartUploadResponse>> StartUploadAsync(
        Guid userId,
        StartUploadRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var channel = await LoadChannelAsync(userId, request.TeamSlug, request.ChannelName, ct);
        if (channel is null)
        {
            return NotFoundChannel<StartUploadResponse>();
        }

        if (!Allow(userId, AuthAction.UploadFile, channel))
        {
            return NotFoundChannel<StartUploadResponse>();
        }

        if (channel.Channel.Kind == ChannelKind.Text)
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.Validation, "Text channels do not store files.");
        }

        var fileName = request.FileName?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > FolderRules.MaxNameLength)
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.Validation, "File name is required.");
        }

        if (request.DeclaredSizeBytes < 0)
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.Validation, "Declared size is invalid.");
        }

        if (request.DeclaredSizeBytes > _limits.MaxFileBytes)
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.FileTooLarge, "File exceeds maximum size.");
        }

        if (channel.Team.UsageBytes + request.DeclaredSizeBytes > channel.Team.QuotaBytes)
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.TeamQuotaExceeded, "Team quota exceeded.");
        }

        if (request.FolderId is Guid folderId)
        {
            var folderOk = await db.Folders.AnyAsync(
                f => f.Id == folderId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null,
                ct);
            if (!folderOk)
            {
                return AppResult<StartUploadResponse>.Fail(ErrorCodes.NotFound, "Folder not found.");
            }
        }

        if (await HasSiblingConflictAsync(channel.Channel.Id, request.FolderId, fileName, null, null, ct))
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        var fileId = UuidV7.NewGuid();
        var objectKey = ObjectKey.ForFile(fileId);
        var contentType = ContentTypeDetector.Detect(fileName, request.ContentType);
        var multipartId = await store.BeginMultipartUploadAsync(objectKey, contentType, ct);
        var now = clock.UtcNow;
        var session = new UploadSessionEntity
        {
            Id = UuidV7.NewGuid(),
            UserId = userId,
            TeamId = channel.Team.Id,
            ChannelId = channel.Channel.Id,
            TargetFolderId = request.FolderId,
            FileName = fileName,
            ContentType = contentType,
            DeclaredSizeBytes = request.DeclaredSizeBytes,
            MultipartId = multipartId,
            ObjectKey = objectKey,
            State = UploadSessionState.Pending,
            FileId = fileId,
            ExpiresAt = now.AddHours(_limits.UploadSessionExpiryHours),
            CreatedAt = now,
        };
        db.UploadSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return AppResult<StartUploadResponse>.Ok(new StartUploadResponse(
            session.Id,
            multipartId,
            _limits.PartSizeBytes,
            session.ExpiresAt));
    }

    public async Task<AppResult<UploadPartResponse>> UploadPartAsync(
        Guid userId,
        Guid uploadId,
        int partNumber,
        Stream content,
        CancellationToken ct = default)
    {
        var session = await db.UploadSessions.Include(s => s.Parts)
            .FirstOrDefaultAsync(s => s.Id == uploadId && s.UserId == userId, ct);
        if (session is null)
        {
            return AppResult<UploadPartResponse>.Fail(ErrorCodes.NotFound, "Upload not found.");
        }

        if (session.State != UploadSessionState.Pending)
        {
            return AppResult<UploadPartResponse>.Fail(ErrorCodes.UploadInvalidState, "Upload is not pending.");
        }

        if (session.ExpiresAt < clock.UtcNow)
        {
            return AppResult<UploadPartResponse>.Fail(ErrorCodes.UploadExpired, "Upload session expired.");
        }

        if (partNumber < 1)
        {
            return AppResult<UploadPartResponse>.Fail(ErrorCodes.Validation, "Part number must be >= 1.");
        }

        // Count bytes while streaming to store (bounded buffer inside store).
        await using var counting = new CountingStream(content);
        var etag = await store.UploadPartAsync(session.ObjectKey, session.MultipartId, partNumber, counting, ct);

        var existing = session.Parts.FirstOrDefault(p => p.PartNumber == partNumber);
        if (existing is null)
        {
            existing = new UploadPartEntity
            {
                UploadSessionId = session.Id,
                PartNumber = partNumber,
            };
            db.UploadParts.Add(existing);
            session.Parts.Add(existing);
        }

        existing.ETag = etag;
        existing.SizeBytes = counting.BytesRead;
        await db.SaveChangesAsync(ct);

        return AppResult<UploadPartResponse>.Ok(new UploadPartResponse(partNumber, etag, counting.BytesRead));
    }

    public async Task<AppResult<IReadOnlyList<UploadPartInfo>>> ListPartsAsync(
        Guid userId,
        Guid uploadId,
        CancellationToken ct = default)
    {
        var session = await db.UploadSessions.AsNoTracking()
            .Include(s => s.Parts)
            .FirstOrDefaultAsync(s => s.Id == uploadId && s.UserId == userId, ct);
        if (session is null)
        {
            return AppResult<IReadOnlyList<UploadPartInfo>>.Fail(ErrorCodes.NotFound, "Upload not found.");
        }

        IReadOnlyList<UploadPartInfo> parts = session.Parts
            .OrderBy(p => p.PartNumber)
            .Select(p => new UploadPartInfo(p.PartNumber, p.ETag, p.SizeBytes))
            .ToList();
        return AppResult<IReadOnlyList<UploadPartInfo>>.Ok(parts);
    }

    public async Task<AppResult<FileItemDto>> CompleteUploadAsync(
        Guid userId,
        Guid uploadId,
        CompleteUploadRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        var session = await db.UploadSessions.Include(s => s.Parts)
            .FirstOrDefaultAsync(s => s.Id == uploadId && s.UserId == userId, ct);
        if (session is null)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Upload not found.");
        }

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var prior = await db.UploadSessions.AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.UserId == userId
                         && s.IdempotencyKey == request.IdempotencyKey
                         && s.State == UploadSessionState.Completed
                         && s.CompletedFileId != null,
                    ct);
            if (prior?.CompletedFileId is Guid completedId)
            {
                var existingFile = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == completedId, ct);
                if (existingFile is not null)
                {
                    return AppResult<FileItemDto>.Ok(ToFileDto(existingFile));
                }
            }

            if (session.State == UploadSessionState.Completed && session.CompletedFileId is Guid sameCompleted)
            {
                var existingFile = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == sameCompleted, ct);
                if (existingFile is not null)
                {
                    return AppResult<FileItemDto>.Ok(ToFileDto(existingFile));
                }
            }
        }
        else if (session.State == UploadSessionState.Completed && session.CompletedFileId is Guid doneId)
        {
            var existingFile = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == doneId, ct);
            if (existingFile is not null)
            {
                return AppResult<FileItemDto>.Ok(ToFileDto(existingFile));
            }
        }

        if (session.State != UploadSessionState.Pending)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.UploadInvalidState, "Upload is not pending.");
        }

        if (session.ExpiresAt < clock.UtcNow)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.UploadExpired, "Upload session expired.");
        }

        if (session.Parts.Count == 0 && session.DeclaredSizeBytes > 0)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "No parts uploaded.");
        }

        var ordered = session.Parts.OrderBy(p => p.PartNumber).ToList();
        var totalSize = ordered.Sum(p => p.SizeBytes);
        if (totalSize != session.DeclaredSizeBytes)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Uploaded size does not match declared size.");
        }

        var isPersonal = session.ChannelId is null;
        if (isPersonal)
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            if (user.PersonalUsageBytes + totalSize > user.PersonalQuotaBytes)
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.PersonalQuotaExceeded, "Personal quota exceeded.");
            }

            if (await HasPersonalSiblingConflictAsync(userId, session.TargetFolderId, session.FileName, null, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }
        }
        else
        {
            var team = await db.Teams.FirstAsync(t => t.Id == session.TeamId, ct);
            if (team.UsageBytes + totalSize > team.QuotaBytes)
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.TeamQuotaExceeded, "Team quota exceeded.");
            }

            if (await HasSiblingConflictAsync(session.ChannelId!.Value, session.TargetFolderId, session.FileName, null, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }
        }

        var etags = ordered.Select(p => p.ETag).ToList();
        await store.CompleteMultipartUploadAsync(session.ObjectKey, session.MultipartId, etags, ct);

        string shaHex;
        try
        {
            shaHex = await ComputeSha256HexAsync(session.ObjectKey, ct);
        }
        catch
        {
            await TryDeleteObjectAsync(session.ObjectKey, ct);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256Hex)
            && !string.Equals(NormalizeHex(request.ExpectedSha256Hex), shaHex, StringComparison.OrdinalIgnoreCase))
        {
            await TryDeleteObjectAsync(session.ObjectKey, ct);
            return AppResult<FileItemDto>.Fail(ErrorCodes.ChecksumMismatch, "SHA-256 mismatch.");
        }

        var now = clock.UtcNow;
        var file = new FileEntity
        {
            Id = session.FileId,
            FolderId = session.TargetFolderId,
            ChannelId = session.ChannelId,
            OwnerUserId = isPersonal ? userId : null,
            Name = session.FileName,
            ContentType = session.ContentType,
            SizeBytes = totalSize,
            Sha256 = shaHex,
            StorageKey = session.ObjectKey,
            UploadedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (session.ChannelId is Guid channelId)
            {
                var guard = await channelWriteGuard.EnsureSharedAsync(channelId, ct);
                if (!guard.Succeeded)
                {
                    await tx.RollbackAsync(ct);
                    return AppResult<FileItemDto>.Fail(guard.Code!, guard.Message!);
                }
            }

            db.Files.Add(file);
            if (isPersonal)
            {
                var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
                user.PersonalUsageBytes += totalSize;
            }
            else
            {
                var team = await db.Teams.FirstAsync(t => t.Id == session.TeamId, ct);
                team.UsageBytes += totalSize;
            }

            session.State = UploadSessionState.Completed;
            session.CompletedFileId = file.Id;
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                session.IdempotencyKey = request.IdempotencyKey.Trim();
            }

            await WriteAuditAsync(userId, AuditEventType.FileUpload, "file", file.Id.ToString(), session.TeamId, context, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            await TryDeleteObjectAsync(session.ObjectKey, ct);
            throw;
        }

        if (session.ChannelId is Guid uploadChannelId)
        {
            channelNotifier.NotifyFilesChanged(uploadChannelId);
        }

        return AppResult<FileItemDto>.Ok(ToFileDto(file));
    }

    public async Task<AppResult> AbortUploadAsync(
        Guid userId,
        Guid uploadId,
        RequestContext context,
        CancellationToken ct = default)
    {
        var session = await db.UploadSessions.FirstOrDefaultAsync(s => s.Id == uploadId && s.UserId == userId, ct);
        if (session is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Upload not found.");
        }

        if (session.State == UploadSessionState.Completed)
        {
            return AppResult.Fail(ErrorCodes.UploadInvalidState, "Upload already completed.");
        }

        if (session.State == UploadSessionState.Pending)
        {
            await store.AbortMultipartUploadAsync(session.ObjectKey, session.MultipartId, ct);
        }

        session.State = UploadSessionState.Aborted;
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    private async Task<string> ComputeSha256HexAsync(string objectKey, CancellationToken ct)
    {
        await using var stream = await store.OpenReadAsync(objectKey, cancellationToken: ct);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task TryDeleteObjectAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await store.DeleteAsync(objectKey, ct);
        }
        catch
        {
            // Best-effort compensation.
        }
    }

    private bool Allow(Guid userId, AuthAction action, ChannelAccess channel) =>
        authorizer.Authorize(ToContext(userId, action, channel, isFileOwner: false)).Allowed;

    private bool AllowFileWrite(Guid userId, ChannelAccess channel, Guid uploadedBy, bool delete = false)
    {
        var isOwner = uploadedBy == userId;
        var own = delete ? AuthAction.DeleteOwnFile : AuthAction.RenameOwnFile;
        var any = delete ? AuthAction.DeleteAnyFile : AuthAction.RenameAnyFile;
        if (isOwner && authorizer.Authorize(ToContext(userId, own, channel, isFileOwner: true)).Allowed)
        {
            return true;
        }

        return authorizer.Authorize(ToContext(userId, any, channel, isFileOwner: isOwner)).Allowed;
    }

    private static AuthorizationContext ToContext(Guid userId, AuthAction action, ChannelAccess channel, bool isFileOwner) =>
        new()
        {
            UserId = userId,
            Action = action,
            IsTeamMember = true,
            TeamRole = channel.Role,
            TeamArchived = channel.Team.ArchivedAt is not null,
            ChannelExists = true,
            ChannelVisibility = channel.Channel.Visibility,
            ChannelArchived = channel.Channel.ArchivedAt is not null,
            IsChannelMember = channel.IsChannelMember,
            ChannelCanWrite = channel.CanWrite,
            ChannelCanManage = channel.CanManage,
            IsFileOwner = isFileOwner,
        };

    private async Task<ChannelAccess?> LoadChannelAsync(Guid userId, string slug, string channelName, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (team is null)
        {
            return null;
        }

        var member = await db.TeamMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TeamId == team.Id && m.UserId == userId, ct);
        if (member is null)
        {
            return null;
        }

        var channel = await db.Channels.FirstOrDefaultAsync(
            c => c.TeamId == team.Id && c.Name.ToLower() == channelName.ToLower(),
            ct);
        if (channel is null)
        {
            return null;
        }

        var cm = await db.ChannelMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channel.Id && m.UserId == userId, ct);

        return new ChannelAccess(team, member.Role, channel, cm is not null, cm?.CanWrite ?? false, cm?.CanManage ?? false);
    }

    private async Task<bool> HasSiblingConflictAsync(
        Guid channelId,
        Guid? parentFolderId,
        string name,
        Guid? excludeFileId,
        Guid? excludeFolderId,
        CancellationToken ct)
    {
        var normalized = SiblingName.Normalize(name);
        var fileConflict = await db.Files.AnyAsync(
            f => f.ChannelId == channelId
                 && f.FolderId == parentFolderId
                 && f.DeletedAt == null
                 && f.Id != excludeFileId
                 && f.Name.ToLower() == normalized,
            ct);
        if (fileConflict)
        {
            return true;
        }

        return await db.Folders.AnyAsync(
            f => f.ChannelId == channelId
                 && f.ParentId == parentFolderId
                 && f.DeletedAt == null
                 && f.Id != excludeFolderId
                 && f.Name.ToLower() == normalized,
            ct);
    }

    private async Task<bool> HasPersonalSiblingConflictAsync(
        Guid ownerUserId,
        Guid? parentFolderId,
        string name,
        Guid? excludeFileId,
        Guid? excludeFolderId,
        CancellationToken ct)
    {
        var normalized = SiblingName.Normalize(name);
        var fileConflict = await db.Files.AnyAsync(
            f => f.OwnerUserId == ownerUserId
                 && f.FolderId == parentFolderId
                 && f.DeletedAt == null
                 && f.Id != excludeFileId
                 && f.Name.ToLower() == normalized,
            ct);
        if (fileConflict)
        {
            return true;
        }

        return await db.Folders.AnyAsync(
            f => f.OwnerUserId == ownerUserId
                 && f.ParentId == parentFolderId
                 && f.DeletedAt == null
                 && f.Id != excludeFolderId
                 && f.Name.ToLower() == normalized,
            ct);
    }

    private async Task<(string Code, string Message)?> EnsureFolderDepthAsync(Guid channelId, Guid? folderId, CancellationToken ct)
    {
        if (folderId is null)
        {
            return null;
        }

        var depth = 0;
        Guid? current = folderId;
        while (current is not null)
        {
            depth++;
            if (depth >= FolderRules.MaxDepth)
            {
                return (ErrorCodes.FolderDepthExceeded, "Folder depth limit exceeded.");
            }

            var parent = await db.Folders.AsNoTracking()
                .Where(f => f.Id == current && f.ChannelId == channelId && f.DeletedAt == null)
                .Select(f => new { f.ParentId })
                .FirstOrDefaultAsync(ct);
            if (parent is null)
            {
                return (ErrorCodes.NotFound, "Folder not found.");
            }

            current = parent.ParentId;
        }

        return null;
    }

    /// <summary>The folder itself plus all nested folders (any depth), including already-trashed ones.</summary>
    private async Task<List<Guid>> CollectDescendantFolderIdsAsync(Guid rootFolderId, CancellationToken ct)
    {
        var all = new List<Guid> { rootFolderId };
        var frontier = new List<Guid> { rootFolderId };
        var guard = 0;
        while (frontier.Count > 0 && guard++ < 256)
        {
            var current = frontier;
            var children = await db.Folders.AsNoTracking()
                .Where(f => f.ParentId != null && current.Contains(f.ParentId.Value))
                .Select(f => f.Id)
                .ToListAsync(ct);
            frontier = children.Where(id => !all.Contains(id)).ToList();
            all.AddRange(frontier);
        }

        return all;
    }

    private async Task<AppResult<FileItemDto>> MoveFileAcrossChannelsAsync(
        Guid userId,
        ChannelAccess source,
        Guid itemId,
        MoveItemRequest request,
        RequestContext context,
        CancellationToken ct)
    {
        var file = await db.Files.FirstOrDefaultAsync(
            f => f.Id == itemId && f.ChannelId == source.Channel.Id && f.DeletedAt == null, ct);
        if (file is null)
        {
            var isFolder = await db.Folders.AnyAsync(
                f => f.Id == itemId && f.ChannelId == source.Channel.Id && f.DeletedAt == null, ct);
            return isFolder
                ? AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Only files can be moved to another channel.")
                : AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Item not found.");
        }

        if (!AllowFileWrite(userId, source, file.UploadedBy))
        {
            return NotFoundChannel<FileItemDto>();
        }

        var target = await LoadChannelAsync(userId, source.Team.Slug, request.TargetChannelName!, ct);
        if (target is null || !Allow(userId, AuthAction.UploadFile, target))
        {
            return NotFoundChannel<FileItemDto>();
        }

        if (target.Channel.Kind == ChannelKind.Text)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Text channels do not store files.");
        }

        if (target.Channel.ArchivedAt is not null)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Target channel is archived.");
        }

        var depthResult = await EnsureFolderDepthAsync(target.Channel.Id, request.TargetFolderId, ct);
        if (depthResult is not null)
        {
            return AppResult<FileItemDto>.Fail(depthResult.Value.Code, depthResult.Value.Message);
        }

        if (await HasSiblingConflictAsync(target.Channel.Id, request.TargetFolderId, file.Name, file.Id, null, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ordered = await channelWriteGuard.LockOrderedAsync(
            [(source.Channel.Id, false), (target.Channel.Id, false)],
            ct);
        if (!ordered.Succeeded)
        {
            return AppResult<FileItemDto>.Fail(ordered.Code!, ordered.Message!);
        }

        // Public links never follow a file into another channel: revoke in the SAME SaveChanges (one txn)
        // as the move. The FK's ON UPDATE CASCADE then carries channel_id; original_channel_id is kept.
        var movedFileId = file.Id;
        await ShareLinkRevoker.RevokeAsync(
            db, clock, l => l.FileId == movedFileId, ShareLinkRevoker.ReasonMove, userId, context, ct);

        file.ChannelId = target.Channel.Id;
        file.FolderId = request.TargetFolderId;
        file.UpdatedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.FileMove, "file", file.Id.ToString(), source.Team.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        channelNotifier.NotifyFilesChanged(source.Channel.Id);
        channelNotifier.NotifyFilesChanged(target.Channel.Id);
        return AppResult<FileItemDto>.Ok(ToFileDto(file));
    }

    private async Task<bool> IsDescendantFolderAsync(Guid ancestorId, Guid? candidateId, CancellationToken ct)
    {
        Guid? current = candidateId;
        while (current is not null)
        {
            if (current == ancestorId)
            {
                return true;
            }

            current = await db.Folders.AsNoTracking()
                .Where(f => f.Id == current)
                .Select(f => f.ParentId)
                .FirstOrDefaultAsync(ct);
        }

        return false;
    }

    private async Task WriteAuditAsync(
        Guid? actorId,
        AuditEventType type,
        string targetType,
        string targetId,
        Guid? teamId,
        RequestContext context,
        CancellationToken ct)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = actorId,
            EventType = type,
            TeamId = teamId,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = "{}",
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
    }

    private static FileItemDto ToFileDto(FileEntity f) =>
        new(f.Id, ItemKind.File, f.Name, f.FolderId, f.ChannelId, f.ContentType, f.SizeBytes, f.Sha256, f.UploadedBy, f.CreatedAt, f.UpdatedAt, f.DeletedAt, f.OwnerUserId);

    private static FileItemDto ToFolderDto(FolderEntity f) =>
        new(f.Id, ItemKind.Folder, f.Name, f.ParentId, f.ChannelId, null, null, null, null, f.CreatedAt, f.UpdatedAt, f.DeletedAt, f.OwnerUserId);

    private static AppResult NotFoundChannel() =>
        AppResult.Fail(ErrorCodes.NotFound, "Channel not found.");

    private static AppResult<T> NotFoundChannel<T>() =>
        AppResult<T>.Fail(ErrorCodes.NotFound, "Channel not found.");

    private static string EncodeCursor(DateTimeOffset updatedAt, Guid id)
    {
        var raw = $"{updatedAt.UtcTicks}:{id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset updatedAt, out Guid id)
    {
        updatedAt = default;
        id = default;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(':', 2);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                return false;
            }

            if (!Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            updatedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeHex(string hex) => hex.Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record ChannelAccess(
        TeamEntity Team,
        MemberRole Role,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
