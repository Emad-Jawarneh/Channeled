using System.Globalization;
using System.Text;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Files;
using Channeled.Application.Identity;
using Channeled.Application.Personal;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Files;
using Channeled.Domain.Identity;
using Channeled.Domain.Storage;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Channeled.Infrastructure.Personal;

public sealed class PersonalService(
    ApplicationDbContext db,
    IAuthorizer authorizer,
    IFileStore store,
    IClock clock,
    IOptions<UploadLimitsOptions> limitsOptions) : IPersonalService
{
    private readonly UploadLimitsOptions _limits = limitsOptions.Value;

    public async Task<AppResult<PersonalStorageDto>> GetStorageAsync(Guid userId, CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.AccessPersonal))
        {
            return AppResult<PersonalStorageDto>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return AppResult<PersonalStorageDto>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        return AppResult<PersonalStorageDto>.Ok(new PersonalStorageDto(user.PersonalQuotaBytes, user.PersonalUsageBytes));
    }

    public async Task<AppResult<FileItemDto>> CreateFolderAsync(
        Guid userId,
        CreateFolderRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return NotFound();
        }

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > FolderRules.MaxNameLength)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Folder name is required.");
        }

        var depth = await EnsureFolderDepthAsync(userId, request.ParentFolderId, ct);
        if (depth is not null)
        {
            return AppResult<FileItemDto>.Fail(depth.Value.Code, depth.Value.Message);
        }

        if (await HasSiblingConflictAsync(userId, request.ParentFolderId, name, null, null, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        var now = clock.UtcNow;
        var folder = new FolderEntity
        {
            Id = UuidV7.NewGuid(),
            OwnerUserId = userId,
            ParentId = request.ParentFolderId,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Folders.Add(folder);
        await WriteAuditAsync(userId, AuditEventType.FolderCreate, "folder", folder.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult<ItemListResponse>> ListItemsAsync(
        Guid userId,
        ListItemsQuery query,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.AccessPersonal))
        {
            return AppResult<ItemListResponse>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var folders = await db.Folders.AsNoTracking()
            .Where(f => f.OwnerUserId == userId)
            .Where(f => query.TrashOnly ? f.DeletedAt != null : f.DeletedAt == null)
            .Where(f => query.TrashOnly || f.ParentId == query.FolderId)
            .ToListAsync(ct);

        var filesQuery = db.Files.AsNoTracking()
            .Where(f => f.OwnerUserId == userId)
            .Where(f => query.TrashOnly ? f.DeletedAt != null : f.DeletedAt == null)
            .Where(f => query.TrashOnly || f.FolderId == query.FolderId);

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = query.Type.Trim().ToLowerInvariant();
            filesQuery = filesQuery.Where(f => f.ContentType.ToLower().StartsWith(type));
        }

        var files = await filesQuery.ToListAsync(ct);
        var starred = await db.FileStars.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.FileId)
            .ToListAsync(ct);
        var starredSet = starred.ToHashSet();

        IEnumerable<FileItemDto> items = folders.Select(ToFolderDto)
            .Concat(files.Select(f => ToFileDto(f, starredSet.Contains(f.Id))));

        items = SortItems(items, query.Sort);
        items = ApplyCursor(items, query.Cursor);

        var page = items.Take(limit + 1).ToList();
        string? next = null;
        if (page.Count > limit)
        {
            page = page.Take(limit).ToList();
            next = EncodeCursor(page[^1].UpdatedAt, page[^1].Id);
        }

        return AppResult<ItemListResponse>.Ok(new ItemListResponse(page, next));
    }

    public async Task<AppResult<ItemListResponse>> ListRecentAsync(
        Guid userId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.AccessPersonal))
        {
            return AppResult<ItemListResponse>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
        var files = await db.Files.AsNoTracking()
            .Where(f => f.OwnerUserId == userId && f.DeletedAt == null)
            .OrderByDescending(f => f.UpdatedAt)
            .ThenByDescending(f => f.Id)
            .Take(500)
            .ToListAsync(ct);

        var starred = await StarredIdsAsync(userId, ct);
        IEnumerable<FileItemDto> items = files.Select(f => ToFileDto(f, starred.Contains(f.Id)));
        items = ApplyCursor(items, cursor);
        var page = items.Take(limit + 1).ToList();
        string? next = null;
        if (page.Count > limit)
        {
            page = page.Take(limit).ToList();
            next = EncodeCursor(page[^1].UpdatedAt, page[^1].Id);
        }

        return AppResult<ItemListResponse>.Ok(new ItemListResponse(page, next));
    }

    public async Task<AppResult<ItemListResponse>> ListStarredAsync(
        Guid userId,
        string? cursor,
        int limit,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.AccessPersonal))
        {
            return AppResult<ItemListResponse>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
        var rows = await db.FileStars.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Join(db.Files.AsNoTracking().Where(f => f.OwnerUserId == userId && f.DeletedAt == null),
                s => s.FileId,
                f => f.Id,
                (s, f) => new { Star = s, File = f })
            .OrderByDescending(x => x.Star.StarredAt)
            .Take(500)
            .ToListAsync(ct);

        IEnumerable<FileItemDto> items = rows.Select(x => ToFileDto(x.File, starred: true));
        items = ApplyCursor(items, cursor);
        var page = items.Take(limit + 1).ToList();
        string? next = null;
        if (page.Count > limit)
        {
            page = page.Take(limit).ToList();
            next = EncodeCursor(page[^1].UpdatedAt, page[^1].Id);
        }

        return AppResult<ItemListResponse>.Ok(new ItemListResponse(page, next));
    }

    public async Task<AppResult<FileItemDto>> GetItemAsync(Guid userId, Guid itemId, CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.AccessPersonal))
        {
            return NotFound();
        }

        var file = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId, ct);
        if (file is not null)
        {
            var starred = await db.FileStars.AsNoTracking().AnyAsync(s => s.UserId == userId && s.FileId == file.Id, ct);
            return AppResult<FileItemDto>.Ok(ToFileDto(file, starred));
        }

        var folder = await db.Folders.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId, ct);
        if (folder is not null)
        {
            return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
        }

        return NotFound();
    }

    public async Task<AppResult<FileItemDto>> RenameItemAsync(
        Guid userId,
        Guid itemId,
        RenameItemRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return NotFound();
        }

        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > FolderRules.MaxNameLength)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Name is required.");
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (file is not null)
        {
            if (await HasSiblingConflictAsync(userId, file.FolderId, name, file.Id, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }

            file.Name = name;
            file.UpdatedAt = clock.UtcNow;
            await WriteAuditAsync(userId, AuditEventType.FileRename, "file", file.Id.ToString(), null, context, ct);
            await db.SaveChangesAsync(ct);
            return AppResult<FileItemDto>.Ok(ToFileDto(file));
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (folder is null)
        {
            return NotFound();
        }

        if (await HasSiblingConflictAsync(userId, folder.ParentId, name, null, folder.Id, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        folder.Name = name;
        folder.UpdatedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.FolderRename, "folder", folder.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult<FileItemDto>> MoveItemAsync(
        Guid userId,
        Guid itemId,
        MoveItemRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return NotFound();
        }

        var depth = await EnsureFolderDepthAsync(userId, request.TargetFolderId, ct);
        if (depth is not null)
        {
            return AppResult<FileItemDto>.Fail(depth.Value.Code, depth.Value.Message);
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (file is not null)
        {
            if (await HasSiblingConflictAsync(userId, request.TargetFolderId, file.Name, file.Id, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }

            file.FolderId = request.TargetFolderId;
            file.UpdatedAt = clock.UtcNow;
            await WriteAuditAsync(userId, AuditEventType.FileMove, "file", file.Id.ToString(), null, context, ct);
            await db.SaveChangesAsync(ct);
            return AppResult<FileItemDto>.Ok(ToFileDto(file));
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (folder is null)
        {
            return NotFound();
        }

        if (request.TargetFolderId == folder.Id || await IsDescendantFolderAsync(folder.Id, request.TargetFolderId, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.Validation, "Cannot move a folder into itself.");
        }

        if (await HasSiblingConflictAsync(userId, request.TargetFolderId, folder.Name, null, folder.Id, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        folder.ParentId = request.TargetFolderId;
        folder.UpdatedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.FolderMove, "folder", folder.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult> SoftDeleteItemAsync(
        Guid userId,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var now = clock.UtcNow;
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (file is not null)
        {
            file.DeletedAt = now;
            file.UpdatedAt = now;
            await WriteAuditAsync(userId, AuditEventType.FileDelete, "file", file.Id.ToString(), null, context, ct);
            await db.SaveChangesAsync(ct);
            return AppResult.Ok();
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (folder is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Not found.");
        }

        folder.DeletedAt = now;
        folder.UpdatedAt = now;
        await WriteAuditAsync(userId, AuditEventType.FolderDelete, "folder", folder.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<FileItemDto>> RestoreItemAsync(
        Guid userId,
        Guid itemId,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return NotFound();
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt != null, ct);
        if (file is not null)
        {
            if (await HasSiblingConflictAsync(userId, file.FolderId, file.Name, file.Id, null, ct))
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
            }

            file.DeletedAt = null;
            file.UpdatedAt = clock.UtcNow;
            await WriteAuditAsync(userId, AuditEventType.FileRestore, "file", file.Id.ToString(), null, context, ct);
            await db.SaveChangesAsync(ct);
            return AppResult<FileItemDto>.Ok(ToFileDto(file));
        }

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == itemId && f.OwnerUserId == userId && f.DeletedAt != null, ct);
        if (folder is null)
        {
            return NotFound();
        }

        if (await HasSiblingConflictAsync(userId, folder.ParentId, folder.Name, null, folder.Id, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        folder.DeletedAt = null;
        folder.UpdatedAt = clock.UtcNow;
        await WriteAuditAsync(userId, AuditEventType.FolderRestore, "folder", folder.Id.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult<FileItemDto>.Ok(ToFolderDto(folder));
    }

    public async Task<AppResult<DownloadResult>> OpenDownloadAsync(
        Guid userId,
        Guid fileId,
        long? rangeStart,
        long? rangeEndInclusive,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.AccessPersonal))
        {
            return AppResult<DownloadResult>.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var file = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (file is null)
        {
            return AppResult<DownloadResult>.Fail(ErrorCodes.NotFound, "Not found.");
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

        return AppResult<DownloadResult>.Ok(new DownloadResult(
            content,
            file.Name,
            file.ContentType,
            file.SizeBytes,
            start,
            Math.Max(end, 0),
            rangeStart is not null || rangeEndInclusive is not null));
    }

    public async Task<AppResult<StartUploadResponse>> StartUploadAsync(
        Guid userId,
        StartPersonalUploadRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.NotFound, "Not found.");
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

        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (user.PersonalUsageBytes + request.DeclaredSizeBytes > user.PersonalQuotaBytes)
        {
            return AppResult<StartUploadResponse>.Fail(ErrorCodes.PersonalQuotaExceeded, "Personal quota exceeded.");
        }

        if (request.FolderId is Guid folderId)
        {
            var folderOk = await db.Folders.AnyAsync(
                f => f.Id == folderId && f.OwnerUserId == userId && f.DeletedAt == null,
                ct);
            if (!folderOk)
            {
                return AppResult<StartUploadResponse>.Fail(ErrorCodes.NotFound, "Folder not found.");
            }
        }

        if (await HasSiblingConflictAsync(userId, request.FolderId, fileName, null, null, ct))
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
            TeamId = null,
            ChannelId = null,
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

    public async Task<AppResult> StarAsync(Guid userId, Guid fileId, RequestContext context, CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var file = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (file is null)
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var exists = await db.FileStars.AnyAsync(s => s.UserId == userId && s.FileId == fileId, ct);
        if (!exists)
        {
            db.FileStars.Add(new FileStarEntity
            {
                UserId = userId,
                FileId = fileId,
                StarredAt = clock.UtcNow,
            });
            await WriteAuditAsync(userId, AuditEventType.FileStar, "file", fileId.ToString(), null, context, ct);
            await db.SaveChangesAsync(ct);
        }

        return AppResult.Ok();
    }

    public async Task<AppResult> UnstarAsync(Guid userId, Guid fileId, RequestContext context, CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return AppResult.Fail(ErrorCodes.NotFound, "Not found.");
        }

        var row = await db.FileStars.FirstOrDefaultAsync(s => s.UserId == userId && s.FileId == fileId, ct);
        if (row is null)
        {
            // Still require ownership of the file for consistent 404.
            var owned = await db.Files.AsNoTracking().AnyAsync(f => f.Id == fileId && f.OwnerUserId == userId, ct);
            return owned ? AppResult.Ok() : AppResult.Fail(ErrorCodes.NotFound, "Not found.");
        }

        db.FileStars.Remove(row);
        await WriteAuditAsync(userId, AuditEventType.FileUnstar, "file", fileId.ToString(), null, context, ct);
        await db.SaveChangesAsync(ct);
        return AppResult.Ok();
    }

    public async Task<AppResult<FileItemDto>> CopyToChannelAsync(
        Guid userId,
        Guid fileId,
        CopyToChannelRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (!AllowPersonal(userId, AuthAction.WritePersonal))
        {
            return NotFound();
        }

        var source = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.OwnerUserId == userId && f.DeletedAt == null, ct);
        if (source is null)
        {
            return NotFound();
        }

        var channel = await LoadChannelAsync(userId, request.TeamSlug, request.ChannelName, ct);
        if (channel is null)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Channel not found.");
        }

        var writeDecision = authorizer.Authorize(new AuthorizationContext
        {
            UserId = userId,
            Action = AuthAction.UploadFile,
            IsTeamMember = true,
            TeamRole = channel.Role,
            TeamArchived = channel.Team.ArchivedAt is not null,
            ChannelExists = true,
            ChannelVisibility = channel.Channel.Visibility,
            ChannelArchived = channel.Channel.ArchivedAt is not null,
            IsChannelMember = channel.IsChannelMember,
            ChannelCanWrite = channel.CanWrite,
            ChannelCanManage = channel.CanManage,
        });
        if (!writeDecision.Allowed)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Channel not found.");
        }

        if (channel.Team.UsageBytes + source.SizeBytes > channel.Team.QuotaBytes)
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.TeamQuotaExceeded, "Team quota exceeded.");
        }

        if (request.TargetFolderId is Guid folderId)
        {
            var folderOk = await db.Folders.AnyAsync(
                f => f.Id == folderId && f.ChannelId == channel.Channel.Id && f.DeletedAt == null,
                ct);
            if (!folderOk)
            {
                return AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Folder not found.");
            }
        }

        var newName = string.IsNullOrWhiteSpace(request.NewName) ? source.Name : request.NewName.Trim();
        if (await ChannelSiblingConflictAsync(channel.Channel.Id, request.TargetFolderId, newName, ct))
        {
            return AppResult<FileItemDto>.Fail(ErrorCodes.NameConflict, "An item with that name already exists.");
        }

        var newFileId = UuidV7.NewGuid();
        var newKey = ObjectKey.ForFile(newFileId);

        await using (var src = await store.OpenReadAsync(source.StorageKey, cancellationToken: ct))
        {
            await store.PutAsync(newKey, src, source.ContentType, ct);
        }

        var now = clock.UtcNow;
        var copy = new FileEntity
        {
            Id = newFileId,
            FolderId = request.TargetFolderId,
            ChannelId = channel.Channel.Id,
            OwnerUserId = null,
            Name = newName,
            ContentType = source.ContentType,
            SizeBytes = source.SizeBytes,
            Sha256 = source.Sha256,
            StorageKey = newKey,
            UploadedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.Files.Add(copy);
            channel.Team.UsageBytes += source.SizeBytes;
            db.AuditEvents.Add(new AuditEvent
            {
                Id = UuidV7.NewGuid(),
                OccurredAt = now,
                ActorId = userId,
                EventType = AuditEventType.FileCopy,
                TeamId = channel.Team.Id,
                TargetType = "file",
                TargetId = copy.Id.ToString(),
                DetailsJson = $"{{\"sourceFileId\":\"{source.Id}\",\"targetFileId\":\"{copy.Id}\",\"sourceScope\":\"personal\",\"targetChannelId\":\"{channel.Channel.Id}\"}}",
                Ip = Truncate(context.Ip, 64),
                UserAgent = Truncate(context.UserAgent, 512),
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            try
            {
                await store.DeleteAsync(newKey, ct);
            }
            catch
            {
                // compensate best-effort
            }

            throw;
        }

        return AppResult<FileItemDto>.Ok(ToFileDto(copy));
    }

    private bool AllowPersonal(Guid userId, AuthAction action) =>
        authorizer.Authorize(new AuthorizationContext
        {
            UserId = userId,
            Action = action,
            IsPersonalOwner = true,
            IsPlatformAdmin = false,
        }).Allowed;

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
        Guid ownerUserId,
        Guid? parentFolderId,
        string name,
        Guid? excludeFileId,
        Guid? excludeFolderId,
        CancellationToken ct)
    {
        var normalized = SiblingName.Normalize(name);
        if (await db.Files.AnyAsync(
                f => f.OwnerUserId == ownerUserId
                     && f.FolderId == parentFolderId
                     && f.DeletedAt == null
                     && f.Id != excludeFileId
                     && f.Name.ToLower() == normalized,
                ct))
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

    private async Task<bool> ChannelSiblingConflictAsync(Guid channelId, Guid? folderId, string name, CancellationToken ct)
    {
        var normalized = SiblingName.Normalize(name);
        if (await db.Files.AnyAsync(
                f => f.ChannelId == channelId && f.FolderId == folderId && f.DeletedAt == null && f.Name.ToLower() == normalized,
                ct))
        {
            return true;
        }

        return await db.Folders.AnyAsync(
            f => f.ChannelId == channelId && f.ParentId == folderId && f.DeletedAt == null && f.Name.ToLower() == normalized,
            ct);
    }

    private async Task<(string Code, string Message)?> EnsureFolderDepthAsync(Guid ownerUserId, Guid? folderId, CancellationToken ct)
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
                .Where(f => f.Id == current && f.OwnerUserId == ownerUserId && f.DeletedAt == null)
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

    private async Task<HashSet<Guid>> StarredIdsAsync(Guid userId, CancellationToken ct)
    {
        var ids = await db.FileStars.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.FileId).ToListAsync(ct);
        return ids.ToHashSet();
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

    private static IEnumerable<FileItemDto> SortItems(IEnumerable<FileItemDto> items, string? sort) =>
        (sort?.ToLowerInvariant()) switch
        {
            "name" or "name_asc" => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "name_desc" => items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "size" or "size_asc" => items.OrderBy(i => i.SizeBytes ?? -1),
            "size_desc" => items.OrderByDescending(i => i.SizeBytes ?? -1),
            "created" or "created_asc" => items.OrderBy(i => i.CreatedAt),
            _ => items.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Id),
        };

    private static IEnumerable<FileItemDto> ApplyCursor(IEnumerable<FileItemDto> items, string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || !TryDecodeCursor(cursor, out var cursorTime, out var cursorId))
        {
            return items;
        }

        return items.Where(i =>
            i.UpdatedAt < cursorTime
            || (i.UpdatedAt == cursorTime && i.Id.CompareTo(cursorId) < 0));
    }

    private static FileItemDto ToFileDto(FileEntity f, bool starred = false) =>
        new(f.Id, ItemKind.File, f.Name, f.FolderId, f.ChannelId, f.ContentType, f.SizeBytes, f.Sha256, f.UploadedBy, f.CreatedAt, f.UpdatedAt, f.DeletedAt, f.OwnerUserId, starred);

    private static FileItemDto ToFolderDto(FolderEntity f) =>
        new(f.Id, ItemKind.Folder, f.Name, f.ParentId, f.ChannelId, null, null, null, null, f.CreatedAt, f.UpdatedAt, f.DeletedAt, f.OwnerUserId);

    private static AppResult<FileItemDto> NotFound() =>
        AppResult<FileItemDto>.Fail(ErrorCodes.NotFound, "Not found.");

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
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(parts[1], "N", out id))
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

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record ChannelAccess(
        TeamEntity Team,
        MemberRole Role,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);
}
