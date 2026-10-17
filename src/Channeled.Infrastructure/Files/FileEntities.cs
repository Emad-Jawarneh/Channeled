using Channeled.Domain.Files;

namespace Channeled.Infrastructure.Files;

public sealed class FolderEntity
{
    public Guid Id { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBatchId { get; set; }

    /// <summary>Stored generated <c>COALESCE(ChannelId, OwnerUserId)</c>: one listing index set serves both scopes.</summary>
    public Guid ContainerId { get; private set; }

    public FolderEntity? Parent { get; set; }
    public TrashBatchEntity? DeletedBatch { get; set; }
}

public sealed class FileEntity
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public Guid UploadedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBatchId { get; set; }

    /// <summary>Stored generated <c>COALESCE(ChannelId, OwnerUserId)</c>: one listing index set serves both scopes.</summary>
    public Guid ContainerId { get; private set; }

    public FolderEntity? Folder { get; set; }
    public TrashBatchEntity? DeletedBatch { get; set; }
}

/// <summary>Retention clock for a trash cascade; purge queries this small table.</summary>
public sealed class TrashBatchEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
    public string Scope { get; set; } = "channel"; // channel | personal
    public Guid? ChannelId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int ItemCount { get; set; }
    /// <summary>none | purging — allows idempotent crash recovery.</summary>
    public string State { get; set; } = "none";
}

public sealed class FileStarEntity
{
    public Guid UserId { get; set; }
    public Guid FileId { get; set; }
    public DateTimeOffset StarredAt { get; set; }
}

public sealed class UploadSessionEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? TargetFolderId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long DeclaredSizeBytes { get; set; }
    public string MultipartId { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public UploadSessionState State { get; set; } = UploadSessionState.Pending;
    public Guid FileId { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? CompletedFileId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<UploadPartEntity> Parts { get; set; } = [];
}

public sealed class UploadPartEntity
{
    public Guid UploadSessionId { get; set; }
    public int PartNumber { get; set; }
    public string ETag { get; set; } = "";
    public long SizeBytes { get; set; }

    public UploadSessionEntity? Session { get; set; }
}
