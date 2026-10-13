using Channeled.Domain.Audit;

namespace Channeled.Application.Admin;

public sealed record AdminDashboardDto(
    int UserCount,
    int ActiveUserCount,
    int TeamCount,
    int ChannelCount,
    int FileCount,
    long TotalFileBytes,
    int AuditEventsLast24h);

public sealed record AdminHealthDto(
    string Status,
    bool Database,
    bool Store,
    bool Disk,
    long DiskFreeBytes,
    string StoreKind,
    int PendingJobs,
    int DeadJobs,
    string? Detail);

public sealed record AdminUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Status,
    bool IsPlatformAdmin,
    long PersonalQuotaBytes,
    long PersonalUsageBytes,
    DateTimeOffset? LastLoginAt);

public sealed record CreateAdminUserRequest(
    string Email,
    string DisplayName,
    string Password,
    bool IsPlatformAdmin,
    long? PersonalQuotaBytes);

public sealed record UpdateAdminUserRequest(
    string? DisplayName,
    bool? IsPlatformAdmin,
    long? PersonalQuotaBytes);

public sealed record AdminTeamDto(
    Guid Id,
    string Name,
    string Slug,
    long QuotaBytes,
    long UsageBytes,
    int MemberCount,
    DateTimeOffset? ArchivedAt);

public sealed record UpdateTeamQuotaRequest(long QuotaBytes);

public sealed record AuditListQuery(
    AuditEventType? EventType,
    Guid? ActorId,
    Guid? TeamId,
    string? TargetType,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int Limit);

public sealed record AuditEventDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    AuditEventType EventType,
    Guid? TeamId,
    string TargetType,
    string TargetId,
    string DetailsJson,
    string Ip,
    string UserAgent);

public sealed record AuditListResponse(IReadOnlyList<AuditEventDto> Items, string? NextCursor);

public sealed record AdminBackupDto(
    Guid Id,
    string Name,
    string Kind,
    DateTimeOffset CreatedAt,
    string State,
    DateTimeOffset? VerifiedAt,
    int ObjectCount,
    long TotalBytes);

public sealed record ResetUserPasswordRequest(string NewPassword);
