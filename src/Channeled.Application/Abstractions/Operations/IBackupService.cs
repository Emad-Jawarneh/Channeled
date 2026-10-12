namespace Channeled.Application.Abstractions.Operations;

public sealed record BackupManifestEntry(string ObjectKey, long SizeBytes, string Sha256Hex);

public sealed record BackupPackageInfo(
    Guid Id,
    string Directory,
    string Kind,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? VerifiedAt,
    int ObjectCount,
    long TotalBytes,
    string? Error);

public sealed record StoreReconcileReport(
    int DatabaseKeys,
    int StoreKeys,
    IReadOnlyList<string> MissingInStore,
    IReadOnlyList<string> OrphansInStore,
    IReadOnlyList<string> ChecksumMismatches)
{
    public bool IsClean =>
        MissingInStore.Count == 0 && OrphansInStore.Count == 0 && ChecksumMismatches.Count == 0;
}

public interface IBackupService
{
    Task<BackupPackageInfo> CreateAsync(string kind = "ondemand", CancellationToken cancellationToken = default);

    Task<BackupPackageInfo> RestoreAsync(Guid backupId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupPackageInfo>> ListAsync(CancellationToken cancellationToken = default);

    Task<BackupPackageInfo?> GetAsync(Guid backupId, CancellationToken cancellationToken = default);
}

public interface IStoreReconciliationService
{
    Task<StoreReconcileReport> ReconcileAsync(CancellationToken cancellationToken = default);
}

public interface IMaintenanceMode
{
    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public interface IOperatorAlertService
{
    Task AlertAsync(string subject, string body, CancellationToken cancellationToken = default);
}

public interface ISystemHealthService
{
    Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed record SystemHealthReport(
    string Status,
    bool Api,
    bool Database,
    bool Store,
    bool Disk,
    long DiskFreeBytes,
    int PendingJobs,
    string StoreKind,
    string? Detail);
