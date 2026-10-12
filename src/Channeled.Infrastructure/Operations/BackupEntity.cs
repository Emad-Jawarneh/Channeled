namespace Channeled.Infrastructure.Operations;

public sealed class BackupEntity
{
    public Guid Id { get; set; }
    public string Directory { get; set; } = "";
    public string Kind { get; set; } = "ondemand";
    public string State { get; set; } = "pending";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public int ObjectCount { get; set; }
    public long TotalBytes { get; set; }
    public string? Error { get; set; }
}
