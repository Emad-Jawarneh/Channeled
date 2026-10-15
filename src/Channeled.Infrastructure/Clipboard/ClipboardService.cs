using System.Text.Json;
using Channeled.Application.Abstractions.Realtime;
using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.Clipboard;
using Channeled.Application.Identity;
using Channeled.Domain.Audit;
using Channeled.Domain.Authorization;
using Channeled.Domain.Errors;
using Channeled.Domain.Files;
using Channeled.Domain.Identity;
using Channeled.Domain.Storage;
using Channeled.Domain.Teams;
using Channeled.Infrastructure.Files;
using Channeled.Infrastructure.Persistence;
using Channeled.Infrastructure.PublicLinks;
using Channeled.Infrastructure.Teams;
using Microsoft.EntityFrameworkCore;

namespace Channeled.Infrastructure.Clipboard;

public sealed class ClipboardService(
    ApplicationDbContext db,
    IAuthorizer authorizer,
    IFileStore store,
    IClock clock,
    IChannelNotifier channelNotifier,
    ChannelWriteGuard channelWriteGuard) : IClipboardService
{
    private const string StatusSucceeded = "succeeded";
    private const string StatusFailed = "failed";

    public async Task<AppResult<ClipboardPasteResponse>> PasteAsync(
        Guid userId,
        ClipboardPasteRequest request,
        RequestContext context,
        CancellationToken ct = default)
    {
        if (request.ItemIds is null || request.ItemIds.Count == 0)
        {
            return AppResult<ClipboardPasteResponse>.Fail(ErrorCodes.Validation, "At least one item is required.");
        }

        if (request.ItemIds.Count > ClipboardRules.MaxItemsPerPaste)
        {
            return AppResult<ClipboardPasteResponse>.Fail(
                ErrorCodes.Validation,
                $"Cannot paste more than {ClipboardRules.MaxItemsPerPaste} items at once.");
        }

        var mode = request.Mode?.Trim().ToLowerInvariant();
        if (mode is not "copy" and not "cut")
        {
            return AppResult<ClipboardPasteResponse>.Fail(ErrorCodes.Validation, "Mode must be copy or cut.");
        }

        var source = await ResolveScopeAsync(userId, request.Source, ct);
        if (!source.Succeeded)
        {
            return AppResult<ClipboardPasteResponse>.Fail(source.Code!, source.Message!);
        }

        var target = await ResolveScopeAsync(userId, request.Target, ct);
        if (!target.Succeeded)
        {
            return AppResult<ClipboardPasteResponse>.Fail(target.Code!, target.Message!);
        }

        if (mode == "copy")
        {
            var bytes = await SumCopyBytesAsync(userId, source.Value!, request.ItemIds, ct);
            if (!bytes.Succeeded)
            {
                return AppResult<ClipboardPasteResponse>.Fail(bytes.Code!, bytes.Message!);
            }

            var quota = await EnsureCopyQuotaAsync(userId, source.Value!, target.Value!, bytes.Value, ct);
            if (!quota.Succeeded)
            {
                return AppResult<ClipboardPasteResponse>.Fail(quota.Code!, quota.Message!);
            }
        }

        var results = new List<ClipboardPasteItemResult>();
        foreach (var itemId in request.ItemIds.Distinct())
        {
            var one = mode == "copy"
                ? await CopyItemAsync(userId, source.Value!, target.Value!, itemId, context, ct)
                : await CutItemAsync(userId, source.Value!, target.Value!, itemId, context, ct);
            results.Add(one);
        }

        var succeeded = results.Count(r => r.Status == StatusSucceeded);
        return AppResult<ClipboardPasteResponse>.Ok(new ClipboardPasteResponse(
            succeeded,
            results.Count - succeeded,
            results));
    }

    private async Task<ClipboardPasteItemResult> CopyItemAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        Guid itemId,
        RequestContext context,
        CancellationToken ct)
    {
        var read = AuthorizeSourceRead(userId, source);
        if (read is not null)
        {
            return Fail(itemId, read.Value.Code, read.Value.Message);
        }

        var write = AuthorizeTargetWrite(userId, target, forCopyFromPersonal: source.Kind == ScopeKind.Personal);
        if (write is not null)
        {
            return Fail(itemId, write.Value.Code, write.Value.Message);
        }

        if (source.Kind == ScopeKind.Channel && target.Kind == ScopeKind.Personal)
        {
            if (!Allow(userId, AuthAction.DownloadFile, source.Channel!))
            {
                return Fail(itemId, ErrorCodes.NotFound, "Not found.");
            }
        }

        var targetFolder = await ValidateTargetFolderAsync(source, target, ct);
        if (!targetFolder.Succeeded)
        {
            return Fail(itemId, targetFolder.Code!, targetFolder.Message!);
        }

        var file = await FindSourceFileAsync(source, itemId, ct);
        if (file is not null)
        {
            return await CopyFileAsync(userId, source, target, file, targetFolder.Value, context, ct);
        }

        var folder = await FindSourceFolderAsync(source, itemId, ct);
        if (folder is null)
        {
            return Fail(itemId, ErrorCodes.NotFound, "Item not found.");
        }

        return await CopyFolderTreeAsync(userId, source, target, folder, targetFolder.Value, context, ct);
    }

    private async Task<ClipboardPasteItemResult> CutItemAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        Guid itemId,
        RequestContext context,
        CancellationToken ct)
    {
        if (source.Kind == ScopeKind.Channel && target.Kind == ScopeKind.Personal)
        {
            return Fail(itemId, ErrorCodes.Validation, "Cannot cut team files into personal storage.");
        }

        var read = AuthorizeSourceWriteForCut(userId, source);
        if (read is not null)
        {
            return Fail(itemId, read.Value.Code, read.Value.Message);
        }

        var write = AuthorizeTargetWrite(userId, target, forCopyFromPersonal: false);
        if (write is not null)
        {
            return Fail(itemId, write.Value.Code, write.Value.Message);
        }

        var targetFolder = await ValidateTargetFolderAsync(source, target, ct);
        if (!targetFolder.Succeeded)
        {
            return Fail(itemId, targetFolder.Code!, targetFolder.Message!);
        }

        var file = await FindSourceFileAsync(source, itemId, ct);
        if (file is not null)
        {
            return await CutFileAsync(userId, source, target, file, targetFolder.Value, context, ct);
        }

        var folder = await FindSourceFolderAsync(source, itemId, ct);
        if (folder is null)
        {
            return Fail(itemId, ErrorCodes.NotFound, "Item not found.");
        }

        if (source.Kind != target.Kind)
        {
            return Fail(
                itemId,
                ErrorCodes.Validation,
                "Folders cannot be cut between personal and team storage. Copy them instead.");
        }

        if (source.Kind == ScopeKind.Channel
            && target.Kind == ScopeKind.Channel
            && source.Channel!.Channel.Id != target.Channel!.Channel.Id)
        {
            return Fail(itemId, ErrorCodes.Validation, "Folders cannot be cut across channels.");
        }

        return await CutFolderAsync(userId, source, target, folder, targetFolder.Value, context, ct);
    }

    private async Task<ClipboardPasteItemResult> CopyFileAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        FileEntity sourceFile,
        Guid? targetFolderId,
        RequestContext context,
        CancellationToken ct)
    {
        var siblingNames = await LoadSiblingNamesAsync(target, targetFolderId, ct);
        var newName = CopyNameAllocator.Allocate(sourceFile.Name, siblingNames);

        var newFileId = UuidV7.NewGuid();
        var newKey = ObjectKey.ForFile(newFileId);

        try
        {
            await using (var src = await store.OpenReadAsync(sourceFile.StorageKey, cancellationToken: ct))
            {
                await store.PutAsync(newKey, src, sourceFile.ContentType, ct);
            }
        }
        catch
        {
            return Fail(sourceFile.Id, ErrorCodes.Validation, "Failed to copy file content.");
        }

        var now = clock.UtcNow;
        var copy = new FileEntity
        {
            Id = newFileId,
            Name = newName,
            ContentType = sourceFile.ContentType,
            SizeBytes = sourceFile.SizeBytes,
            Sha256 = sourceFile.Sha256,
            StorageKey = newKey,
            FolderId = targetFolderId,
            UploadedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var lockResult = await LockScopesAsync(source, target, ct);
            if (!lockResult.Succeeded)
            {
                await tx.RollbackAsync(ct);
                await TryDeleteObjectAsync(newKey, ct);
                return Fail(sourceFile.Id, lockResult.Code!, lockResult.Message!);
            }

            ApplyTargetOwnership(copy, target);
            db.Files.Add(copy);
            await ApplyCopyQuotaDeltaAsync(userId, source, target, sourceFile.SizeBytes, ct);
            await WriteFileCopyAuditAsync(userId, source, target, sourceFile.Id, copy.Id, context, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            await TryDeleteObjectAsync(newKey, ct);
            throw;
        }

        NotifyScopeChanged(source, target);
        return Ok(sourceFile.Id, copy.Id);
    }

    private async Task<ClipboardPasteItemResult> CopyFolderTreeAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        FolderEntity rootFolder,
        Guid? targetFolderId,
        RequestContext context,
        CancellationToken ct)
    {
        if (targetFolderId == rootFolder.Id || await IsDescendantFolderAsync(rootFolder.Id, targetFolderId, ct))
        {
            return Fail(rootFolder.Id, ErrorCodes.Validation, "Cannot paste a folder into itself or a descendant.");
        }

        var tree = await CollectFolderTreeAsync(source, rootFolder.Id, ct);
        if (!tree.Succeeded)
        {
            return Fail(rootFolder.Id, tree.Code!, tree.Message!);
        }

        var depthCheck = await EnsurePasteDepthAsync(target, targetFolderId, tree.Value!.MaxDepth, ct);
        if (!depthCheck.Succeeded)
        {
            return Fail(rootFolder.Id, depthCheck.Code!, depthCheck.Message!);
        }

        var siblingNames = await LoadSiblingNamesAsync(target, targetFolderId, ct);
        var rootName = CopyNameAllocator.Allocate(rootFolder.Name, siblingNames);

        var folderMap = new Dictionary<Guid, Guid> { [rootFolder.Id] = UuidV7.NewGuid() };
        foreach (var folder in tree.Value.Folders.Where(f => f.Id != rootFolder.Id))
        {
            folderMap[folder.Id] = UuidV7.NewGuid();
        }

        var stagedFiles = new List<(FileEntity Source, FileEntity Copy, string ObjectKey)>();
        foreach (var file in tree.Value.Files)
        {
            var newFileId = UuidV7.NewGuid();
            var newKey = ObjectKey.ForFile(newFileId);
            try
            {
                await using var src = await store.OpenReadAsync(file.StorageKey, cancellationToken: ct);
                await store.PutAsync(newKey, src, file.ContentType, ct);
            }
            catch
            {
                foreach (var staged in stagedFiles)
                {
                    await TryDeleteObjectAsync(staged.ObjectKey, ct);
                }

                return Fail(rootFolder.Id, ErrorCodes.Validation, "Failed to copy file content.");
            }

            var now = clock.UtcNow;
            var copyFolderId = file.FolderId is Guid fid && folderMap.TryGetValue(fid, out var mappedParent)
                ? mappedParent
                : folderMap[rootFolder.Id];

            var copy = new FileEntity
            {
                Id = newFileId,
                Name = file.Name,
                ContentType = file.ContentType,
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256,
                StorageKey = newKey,
                FolderId = copyFolderId,
                UploadedBy = userId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            stagedFiles.Add((file, copy, newKey));
        }

        var nowRoot = clock.UtcNow;
        var newFolders = new List<FolderEntity>();
        var parentById = tree.Value.Folders.ToDictionary(f => f.Id, f => f.ParentId);
        int DepthFromRoot(Guid folderId)
        {
            var depth = 0;
            var current = folderId;
            while (current != rootFolder.Id)
            {
                depth++;
                if (!parentById.TryGetValue(current, out var parent) || parent is null)
                {
                    break;
                }

                current = parent.Value;
            }

            return depth;
        }

        foreach (var folder in tree.Value.Folders.OrderBy(f => DepthFromRoot(f.Id)))
        {
            Guid? parentId = folder.Id == rootFolder.Id
                ? targetFolderId
                : folder.ParentId is Guid pid && folderMap.TryGetValue(pid, out var mapped)
                    ? mapped
                    : null;

            var entity = new FolderEntity
            {
                Id = folderMap[folder.Id],
                Name = folder.Id == rootFolder.Id ? rootName : folder.Name,
                ParentId = parentId,
                CreatedAt = nowRoot,
                UpdatedAt = nowRoot,
            };
            ApplyTargetOwnership(entity, target);
            newFolders.Add(entity);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var lockResult = await LockScopesAsync(source, target, ct);
            if (!lockResult.Succeeded)
            {
                await tx.RollbackAsync(ct);
                foreach (var staged in stagedFiles)
                {
                    await TryDeleteObjectAsync(staged.ObjectKey, ct);
                }

                return Fail(rootFolder.Id, lockResult.Code!, lockResult.Message!);
            }

            db.Folders.AddRange(newFolders);
            foreach (var (_, copy, _) in stagedFiles)
            {
                ApplyTargetOwnership(copy, target);
                db.Files.Add(copy);
            }

            var totalBytes = tree.Value.Files.Sum(f => f.SizeBytes);
            await ApplyCopyQuotaDeltaAsync(userId, source, target, totalBytes, ct);

            foreach (var (srcFile, copy, _) in stagedFiles)
            {
                await WriteFileCopyAuditAsync(userId, source, target, srcFile.Id, copy.Id, context, ct);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            foreach (var staged in stagedFiles)
            {
                await TryDeleteObjectAsync(staged.ObjectKey, ct);
            }

            throw;
        }

        NotifyScopeChanged(source, target);
        return Ok(rootFolder.Id, folderMap[rootFolder.Id]);
    }

    private async Task<ClipboardPasteItemResult> CutFileAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        FileEntity file,
        Guid? targetFolderId,
        RequestContext context,
        CancellationToken ct)
    {
        if (source.Kind == ScopeKind.Channel)
        {
            if (!AllowFileWrite(userId, source.Channel!, file.UploadedBy))
            {
                return Fail(file.Id, ErrorCodes.NotFound, "Not found.");
            }
        }

        if (source.Kind == ScopeKind.Channel
            && target.Kind == ScopeKind.Channel
            && source.Channel!.Channel.Id != target.Channel!.Channel.Id)
        {
            return await CutFileAcrossChannelsAsync(userId, source, target, file, targetFolderId, context, ct);
        }

        var siblingNames = await LoadSiblingNamesAsync(target, targetFolderId, ct);
        var newName = CopyNameAllocator.Allocate(file.Name, siblingNames);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockResult = await LockScopesAsync(source, target, ct);
        if (!lockResult.Succeeded)
        {
            return Fail(file.Id, lockResult.Code!, lockResult.Message!);
        }

        if (source.Kind == ScopeKind.Channel
            && target.Kind == ScopeKind.Channel
            && source.Channel!.Channel.Id != target.Channel!.Channel.Id)
        {
            var movedFileId = file.Id;
            await ShareLinkRevoker.RevokeAsync(
                db, clock, l => l.FileId == movedFileId, ShareLinkRevoker.ReasonMove, userId, context, ct);
        }

        file.Name = newName;
        file.FolderId = targetFolderId;
        file.UpdatedAt = clock.UtcNow;
        if (source.Kind == ScopeKind.Personal && target.Kind == ScopeKind.Channel)
        {
            file.ChannelId = target.Channel!.Channel.Id;
            file.OwnerUserId = null;
            var team = await db.Teams.FirstAsync(t => t.Id == target.Channel.Team.Id, ct);
            team.UsageBytes += file.SizeBytes;
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            user.PersonalUsageBytes -= file.SizeBytes;
        }
        else if (source.Kind == ScopeKind.Channel && target.Kind == ScopeKind.Personal)
        {
            return Fail(file.Id, ErrorCodes.Validation, "Cannot cut team files into personal storage.");
        }

        await WriteFileMoveAuditAsync(userId, source, target, file.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        NotifyScopeChanged(source, target);
        return Ok(file.Id, file.Id);
    }

    private async Task<ClipboardPasteItemResult> CutFileAcrossChannelsAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        FileEntity file,
        Guid? targetFolderId,
        RequestContext context,
        CancellationToken ct)
    {
        var siblingNames = await LoadSiblingNamesAsync(target, targetFolderId, ct);
        var newName = CopyNameAllocator.Allocate(file.Name, siblingNames);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockResult = await channelWriteGuard.LockOrderedAsync(
            [(source.Channel!.Channel.Id, false), (target.Channel!.Channel.Id, false)],
            ct);
        if (!lockResult.Succeeded)
        {
            return Fail(file.Id, lockResult.Code!, lockResult.Message!);
        }

        var movedFileId = file.Id;
        await ShareLinkRevoker.RevokeAsync(
            db, clock, l => l.FileId == movedFileId, ShareLinkRevoker.ReasonMove, userId, context, ct);

        file.ChannelId = target.Channel!.Channel.Id;
        file.FolderId = targetFolderId;
        file.Name = newName;
        file.UpdatedAt = clock.UtcNow;
        await WriteFileMoveAuditAsync(userId, source, target, file.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        channelNotifier.NotifyFilesChanged(source.Channel!.Channel.Id);
        channelNotifier.NotifyFilesChanged(target.Channel!.Channel.Id);
        return Ok(file.Id, file.Id);
    }

    private async Task<ClipboardPasteItemResult> CutFolderAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        FolderEntity folder,
        Guid? targetFolderId,
        RequestContext context,
        CancellationToken ct)
    {
        if (targetFolderId == folder.Id || await IsDescendantFolderAsync(folder.Id, targetFolderId, ct))
        {
            return Fail(folder.Id, ErrorCodes.Validation, "Cannot paste a folder into itself or a descendant.");
        }

        if (source.Kind == ScopeKind.Channel && !Allow(userId, AuthAction.UploadFile, source.Channel!))
        {
            return Fail(folder.Id, ErrorCodes.NotFound, "Not found.");
        }

        var siblingNames = await LoadSiblingNamesAsync(target, targetFolderId, ct);
        var newName = CopyNameAllocator.Allocate(folder.Name, siblingNames);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockResult = await LockScopesAsync(source, target, ct);
        if (!lockResult.Succeeded)
        {
            return Fail(folder.Id, lockResult.Code!, lockResult.Message!);
        }

        folder.ParentId = targetFolderId;
        folder.Name = newName;
        folder.UpdatedAt = clock.UtcNow;
        await WriteFolderMoveAuditAsync(userId, source, target, folder.Id, context, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        NotifyScopeChanged(source, target);
        return Ok(folder.Id, folder.Id);
    }

    private async Task<AppResult<long>> SumCopyBytesAsync(
        Guid userId,
        ResolvedScope source,
        IReadOnlyList<Guid> itemIds,
        CancellationToken ct)
    {
        long total = 0;
        foreach (var itemId in itemIds.Distinct())
        {
            var file = await FindSourceFileAsync(source, itemId, ct);
            if (file is not null)
            {
                total += file.SizeBytes;
                continue;
            }

            var folder = await FindSourceFolderAsync(source, itemId, ct);
            if (folder is null)
            {
                continue;
            }

            var tree = await CollectFolderTreeAsync(source, folder.Id, ct);
            if (!tree.Succeeded)
            {
                return AppResult<long>.Fail(tree.Code!, tree.Message!);
            }

            total += tree.Value!.Files.Sum(f => f.SizeBytes);
        }

        return AppResult<long>.Ok(total);
    }

    private async Task<AppResult> EnsureCopyQuotaAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        long additionalBytes,
        CancellationToken ct)
    {
        if (additionalBytes <= 0)
        {
            return AppResult.Ok();
        }

        if (target.Kind == ScopeKind.Personal)
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            if (user.PersonalUsageBytes + additionalBytes > user.PersonalQuotaBytes)
            {
                return AppResult.Fail(ErrorCodes.PersonalQuotaExceeded, "Personal quota exceeded.");
            }

            return AppResult.Ok();
        }

        if (source.Kind == ScopeKind.Personal || source.Kind == ScopeKind.Channel && target.Kind == ScopeKind.Channel
            && source.Channel!.Team.Id != target.Channel!.Team.Id)
        {
            var team = target.Channel!.Team;
            if (team.UsageBytes + additionalBytes > team.QuotaBytes)
            {
                return AppResult.Fail(ErrorCodes.TeamQuotaExceeded, "Team quota exceeded.");
            }
        }

        return AppResult.Ok();
    }

    private async Task ApplyCopyQuotaDeltaAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        long bytes,
        CancellationToken ct)
    {
        if (bytes <= 0)
        {
            return;
        }

        if (target.Kind == ScopeKind.Personal)
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            user.PersonalUsageBytes += bytes;
            return;
        }

        if (source.Kind == ScopeKind.Personal
            || (source.Kind == ScopeKind.Channel
                && target.Kind == ScopeKind.Channel
                && source.Channel!.Team.Id != target.Channel!.Team.Id))
        {
            target.Channel!.Team.UsageBytes += bytes;
        }
    }

    private async Task<AppResult<FolderTree>> CollectFolderTreeAsync(
        ResolvedScope source,
        Guid rootFolderId,
        CancellationToken ct)
    {
        var folders = new List<FolderEntity>();
        var files = new List<FileEntity>();
        var frontier = new List<Guid> { rootFolderId };
        var seen = new HashSet<Guid>();
        var maxDepth = 0;

        while (frontier.Count > 0)
        {
            if (folders.Count + files.Count >= ClipboardRules.MaxDescendantNodes)
            {
                return AppResult<FolderTree>.Fail(ErrorCodes.Validation, "Folder tree is too large to copy.");
            }

            var currentId = frontier[0];
            frontier.RemoveAt(0);
            if (!seen.Add(currentId))
            {
                continue;
            }

            var folder = await LoadFolderInScopeAsync(source, currentId, ct);
            if (folder is null)
            {
                return AppResult<FolderTree>.Fail(ErrorCodes.NotFound, "Folder not found.");
            }

            folders.Add(folder);

            var childFolders = await ListChildFoldersAsync(source, currentId, ct);
            var childFiles = await ListChildFilesAsync(source, currentId, ct);
            files.AddRange(childFiles);

            var depth = await FolderDepthFromRootAsync(source, rootFolderId, currentId, ct);
            maxDepth = Math.Max(maxDepth, depth);

            foreach (var child in childFolders)
            {
                if (depth + 1 >= FolderRules.MaxDepth)
                {
                    return AppResult<FolderTree>.Fail(ErrorCodes.FolderDepthExceeded, "Folder depth limit exceeded.");
                }

                frontier.Add(child.Id);
            }
        }

        return AppResult<FolderTree>.Ok(new FolderTree(folders, files, maxDepth + 1));
    }

    private async Task<int> FolderDepthFromRootAsync(
        ResolvedScope source,
        Guid rootId,
        Guid folderId,
        CancellationToken ct)
    {
        var depth = 0;
        Guid? current = folderId;
        while (current is not null && current != rootId)
        {
            depth++;
            var parent = await LoadFolderInScopeAsync(source, current.Value, ct);
            current = parent?.ParentId;
            if (depth > FolderRules.MaxDepth)
            {
                break;
            }
        }

        return depth;
    }

    private async Task<AppResult<Guid?>> ValidateTargetFolderAsync(
        ResolvedScope source,
        ResolvedScope target,
        CancellationToken ct)
    {
        var folderId = target.FolderId;
        if (folderId is null)
        {
            return AppResult<Guid?>.Ok(null);
        }

        if (target.Kind == ScopeKind.Channel)
        {
            var ok = await db.Folders.AnyAsync(
                f => f.Id == folderId && f.ChannelId == target.Channel!.Channel.Id && f.DeletedAt == null,
                ct);
            if (!ok)
            {
                return AppResult<Guid?>.Fail(ErrorCodes.NotFound, "Target folder not found.");
            }
        }
        else
        {
            var ok = await db.Folders.AnyAsync(
                f => f.Id == folderId && f.OwnerUserId == target.UserId && f.DeletedAt == null,
                ct);
            if (!ok)
            {
                return AppResult<Guid?>.Fail(ErrorCodes.NotFound, "Target folder not found.");
            }
        }

        return AppResult<Guid?>.Ok(folderId);
    }

    private async Task<AppResult> EnsurePasteDepthAsync(
        ResolvedScope target,
        Guid? targetFolderId,
        int treeHeight,
        CancellationToken ct)
    {
        var baseDepth = 0;
        Guid? current = targetFolderId;
        while (current is not null)
        {
            baseDepth++;
            if (baseDepth >= FolderRules.MaxDepth)
            {
                return AppResult.Fail(ErrorCodes.FolderDepthExceeded, "Folder depth limit exceeded.");
            }

            if (target.Kind == ScopeKind.Channel)
            {
                current = await db.Folders.AsNoTracking()
                    .Where(f => f.Id == current && f.ChannelId == target.Channel!.Channel.Id)
                    .Select(f => f.ParentId)
                    .FirstOrDefaultAsync(ct);
            }
            else
            {
                current = await db.Folders.AsNoTracking()
                    .Where(f => f.Id == current && f.OwnerUserId == target.UserId)
                    .Select(f => f.ParentId)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (baseDepth + treeHeight > FolderRules.MaxDepth)
        {
            return AppResult.Fail(ErrorCodes.FolderDepthExceeded, "Folder depth limit exceeded.");
        }

        return AppResult.Ok();
    }

    private async Task<AppResult> LockScopesAsync(ResolvedScope source, ResolvedScope target, CancellationToken ct)
    {
        var channelIds = new List<(Guid Id, bool ForUpdate)>();
        if (source.Kind == ScopeKind.Channel)
        {
            channelIds.Add((source.Channel!.Channel.Id, false));
        }

        if (target.Kind == ScopeKind.Channel)
        {
            var id = target.Channel!.Channel.Id;
            if (channelIds.All(c => c.Id != id))
            {
                channelIds.Add((id, false));
            }
        }

        if (channelIds.Count == 0)
        {
            return AppResult.Ok();
        }

        return await channelWriteGuard.LockOrderedAsync(channelIds, ct);
    }

    private async Task<AppResult<ResolvedScope>> ResolveScopeAsync(
        Guid userId,
        ClipboardScopeRequest scope,
        CancellationToken ct)
    {
        var kind = scope.Scope?.Trim().ToLowerInvariant();
        if (kind is not "channel" and not "personal")
        {
            return AppResult<ResolvedScope>.Fail(ErrorCodes.Validation, "Scope must be channel or personal.");
        }

        if (kind == "personal")
        {
            if (!AllowPersonal(userId, AuthAction.AccessPersonal))
            {
                return AppResult<ResolvedScope>.Fail(ErrorCodes.NotFound, "Not found.");
            }

            return AppResult<ResolvedScope>.Ok(new ResolvedScope(
                ScopeKind.Personal,
                userId,
                scope.FolderId,
                null));
        }

        var slug = scope.TeamSlug?.Trim();
        var channelName = scope.ChannelName?.Trim();
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(channelName))
        {
            return AppResult<ResolvedScope>.Fail(ErrorCodes.Validation, "Team slug and channel name are required.");
        }

        var channel = await LoadChannelAsync(userId, slug, channelName, ct);
        if (channel is null)
        {
            return AppResult<ResolvedScope>.Fail(ErrorCodes.NotFound, "Channel not found.");
        }

        return AppResult<ResolvedScope>.Ok(new ResolvedScope(
            ScopeKind.Channel,
            userId,
            scope.FolderId,
            channel));
    }

    private (string Code, string Message)? AuthorizeSourceRead(Guid userId, ResolvedScope source) =>
        source.Kind switch
        {
            ScopeKind.Personal when !AllowPersonal(userId, AuthAction.AccessPersonal) =>
                (ErrorCodes.NotFound, "Not found."),
            ScopeKind.Channel when !Allow(userId, AuthAction.BrowseFiles, source.Channel!) =>
                (ErrorCodes.NotFound, "Not found."),
            _ => null,
        };

    private (string Code, string Message)? AuthorizeSourceWriteForCut(Guid userId, ResolvedScope source) =>
        source.Kind switch
        {
            ScopeKind.Personal when !AllowPersonal(userId, AuthAction.WritePersonal) =>
                (ErrorCodes.NotFound, "Not found."),
            ScopeKind.Channel when !Allow(userId, AuthAction.UploadFile, source.Channel!) =>
                (ErrorCodes.NotFound, "Not found."),
            _ => null,
        };

    private (string Code, string Message)? AuthorizeTargetWrite(
        Guid userId,
        ResolvedScope target,
        bool forCopyFromPersonal) =>
        target.Kind switch
        {
            ScopeKind.Personal when !AllowPersonal(userId, AuthAction.WritePersonal) =>
                (ErrorCodes.NotFound, "Not found."),
            ScopeKind.Channel when !Allow(userId, AuthAction.UploadFile, target.Channel!) =>
                (ErrorCodes.NotFound, "Not found."),
            _ => null,
        };

    private async Task<FileEntity?> FindSourceFileAsync(ResolvedScope source, Guid itemId, CancellationToken ct)
    {
        if (source.Kind == ScopeKind.Channel)
        {
            return await db.Files.FirstOrDefaultAsync(
                f => f.Id == itemId && f.ChannelId == source.Channel!.Channel.Id && f.DeletedAt == null,
                ct);
        }

        return await db.Files.FirstOrDefaultAsync(
            f => f.Id == itemId && f.OwnerUserId == source.UserId && f.DeletedAt == null,
            ct);
    }

    private async Task<FolderEntity?> FindSourceFolderAsync(ResolvedScope source, Guid itemId, CancellationToken ct)
    {
        if (source.Kind == ScopeKind.Channel)
        {
            return await db.Folders.FirstOrDefaultAsync(
                f => f.Id == itemId && f.ChannelId == source.Channel!.Channel.Id && f.DeletedAt == null,
                ct);
        }

        return await db.Folders.FirstOrDefaultAsync(
            f => f.Id == itemId && f.OwnerUserId == source.UserId && f.DeletedAt == null,
            ct);
    }

    private async Task<FolderEntity?> LoadFolderInScopeAsync(ResolvedScope source, Guid folderId, CancellationToken ct)
    {
        if (source.Kind == ScopeKind.Channel)
        {
            return await db.Folders.AsNoTracking().FirstOrDefaultAsync(
                f => f.Id == folderId && f.ChannelId == source.Channel!.Channel.Id && f.DeletedAt == null,
                ct);
        }

        return await db.Folders.AsNoTracking().FirstOrDefaultAsync(
            f => f.Id == folderId && f.OwnerUserId == source.UserId && f.DeletedAt == null,
            ct);
    }

    private async Task<List<FolderEntity>> ListChildFoldersAsync(ResolvedScope source, Guid parentId, CancellationToken ct)
    {
        if (source.Kind == ScopeKind.Channel)
        {
            return await db.Folders.AsNoTracking()
                .Where(f => f.ChannelId == source.Channel!.Channel.Id && f.ParentId == parentId && f.DeletedAt == null)
                .ToListAsync(ct);
        }

        return await db.Folders.AsNoTracking()
            .Where(f => f.OwnerUserId == source.UserId && f.ParentId == parentId && f.DeletedAt == null)
            .ToListAsync(ct);
    }

    private async Task<List<FileEntity>> ListChildFilesAsync(ResolvedScope source, Guid parentId, CancellationToken ct)
    {
        if (source.Kind == ScopeKind.Channel)
        {
            return await db.Files.AsNoTracking()
                .Where(f => f.ChannelId == source.Channel!.Channel.Id && f.FolderId == parentId && f.DeletedAt == null)
                .ToListAsync(ct);
        }

        return await db.Files.AsNoTracking()
            .Where(f => f.OwnerUserId == source.UserId && f.FolderId == parentId && f.DeletedAt == null)
            .ToListAsync(ct);
    }

    private async Task<List<string>> LoadSiblingNamesAsync(ResolvedScope target, Guid? parentFolderId, CancellationToken ct)
    {
        if (target.Kind == ScopeKind.Channel)
        {
            var channelId = target.Channel!.Channel.Id;
            var fileNames = await db.Files.AsNoTracking()
                .Where(f => f.ChannelId == channelId && f.FolderId == parentFolderId && f.DeletedAt == null)
                .Select(f => f.Name)
                .ToListAsync(ct);
            var folderNames = await db.Folders.AsNoTracking()
                .Where(f => f.ChannelId == channelId && f.ParentId == parentFolderId && f.DeletedAt == null)
                .Select(f => f.Name)
                .ToListAsync(ct);
            return fileNames.Concat(folderNames).ToList();
        }

        var userId = target.UserId;
        var personalFiles = await db.Files.AsNoTracking()
            .Where(f => f.OwnerUserId == userId && f.FolderId == parentFolderId && f.DeletedAt == null)
            .Select(f => f.Name)
            .ToListAsync(ct);
        var personalFolders = await db.Folders.AsNoTracking()
            .Where(f => f.OwnerUserId == userId && f.ParentId == parentFolderId && f.DeletedAt == null)
            .Select(f => f.Name)
            .ToListAsync(ct);
        return personalFiles.Concat(personalFolders).ToList();
    }

    private static void ApplyTargetOwnership(FileEntity file, ResolvedScope target)
    {
        if (target.Kind == ScopeKind.Channel)
        {
            file.ChannelId = target.Channel!.Channel.Id;
            file.OwnerUserId = null;
        }
        else
        {
            file.ChannelId = null;
            file.OwnerUserId = target.UserId;
        }
    }

    private static void ApplyTargetOwnership(FolderEntity folder, ResolvedScope target)
    {
        if (target.Kind == ScopeKind.Channel)
        {
            folder.ChannelId = target.Channel!.Channel.Id;
            folder.OwnerUserId = null;
        }
        else
        {
            folder.ChannelId = null;
            folder.OwnerUserId = target.UserId;
        }
    }

    private void NotifyScopeChanged(ResolvedScope source, ResolvedScope target)
    {
        if (source.Kind == ScopeKind.Channel)
        {
            channelNotifier.NotifyFilesChanged(source.Channel!.Channel.Id);
        }

        if (target.Kind == ScopeKind.Channel)
        {
            var targetId = target.Channel!.Channel.Id;
            if (source.Kind != ScopeKind.Channel || source.Channel!.Channel.Id != targetId)
            {
                channelNotifier.NotifyFilesChanged(targetId);
            }
        }
    }

    private async Task WriteFileCopyAuditAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        Guid sourceFileId,
        Guid targetFileId,
        RequestContext context,
        CancellationToken ct)
    {
        Guid? teamId = target.Kind == ScopeKind.Channel
            ? target.Channel!.Team.Id
            : source.Kind == ScopeKind.Channel ? source.Channel!.Team.Id : null;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = userId,
            EventType = AuditEventType.FileCopy,
            TeamId = teamId,
            TargetType = "file",
            TargetId = targetFileId.ToString(),
            DetailsJson = JsonSerializer.Serialize(new
            {
                sourceFileId,
                targetFileId,
                sourceScope = source.Kind == ScopeKind.Channel ? "channel" : "personal",
                targetScope = target.Kind == ScopeKind.Channel ? "channel" : "personal",
            }),
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
    }

    private async Task WriteFileMoveAuditAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        Guid fileId,
        RequestContext context,
        CancellationToken ct)
    {
        Guid? teamId = source.Kind == ScopeKind.Channel
            ? source.Channel!.Team.Id
            : target.Kind == ScopeKind.Channel ? target.Channel!.Team.Id : null;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = userId,
            EventType = AuditEventType.FileMove,
            TeamId = teamId,
            TargetType = "file",
            TargetId = fileId.ToString(),
            DetailsJson = "{}",
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
    }

    private async Task WriteFolderMoveAuditAsync(
        Guid userId,
        ResolvedScope source,
        ResolvedScope target,
        Guid folderId,
        RequestContext context,
        CancellationToken ct)
    {
        Guid? teamId = source.Kind == ScopeKind.Channel ? source.Channel!.Team.Id : null;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = UuidV7.NewGuid(),
            OccurredAt = clock.UtcNow,
            ActorId = userId,
            EventType = AuditEventType.FolderMove,
            TeamId = teamId,
            TargetType = "folder",
            TargetId = folderId.ToString(),
            DetailsJson = "{}",
            Ip = Truncate(context.Ip, 64),
            UserAgent = Truncate(context.UserAgent, 512),
        });
        await Task.CompletedTask;
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

    private bool AllowPersonal(Guid userId, AuthAction action) =>
        authorizer.Authorize(new AuthorizationContext
        {
            UserId = userId,
            Action = action,
            IsPersonalOwner = true,
        }).Allowed;

    private bool Allow(Guid userId, AuthAction action, ChannelAccess channel) =>
        authorizer.Authorize(ToContext(userId, action, channel, isFileOwner: false)).Allowed;

    private bool AllowFileWrite(Guid userId, ChannelAccess channel, Guid uploadedBy)
    {
        if (uploadedBy == userId && authorizer.Authorize(ToContext(userId, AuthAction.RenameOwnFile, channel, true)).Allowed)
        {
            return true;
        }

        return authorizer.Authorize(ToContext(userId, AuthAction.RenameAnyFile, channel, uploadedBy == userId)).Allowed;
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

        return new ChannelAccess(team, member.Role, channel, cm is not null, cm is not null && !cm.ReadOnly, false);
    }

    private static ClipboardPasteItemResult Ok(Guid itemId, Guid newItemId) =>
        new(itemId, StatusSucceeded, newItemId, null, null);

    private static ClipboardPasteItemResult Fail(Guid itemId, string code, string message) =>
        new(itemId, StatusFailed, null, code, message);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private enum ScopeKind
    {
        Channel,
        Personal,
    }

    private sealed record ResolvedScope(
        ScopeKind Kind,
        Guid UserId,
        Guid? FolderId,
        ChannelAccess? Channel);

    private sealed record ChannelAccess(
        TeamEntity Team,
        MemberRole Role,
        ChannelEntity Channel,
        bool IsChannelMember,
        bool CanWrite,
        bool CanManage);

    private sealed record FolderTree(
        IReadOnlyList<FolderEntity> Folders,
        IReadOnlyList<FileEntity> Files,
        int MaxDepth);
}
