namespace Channeled.Application.Abstractions.Storage;

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>When true, successful downloads write FileDownload audit events.</summary>
    public bool AuditDownloads { get; set; }
}
