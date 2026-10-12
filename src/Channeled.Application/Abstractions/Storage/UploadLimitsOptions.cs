namespace Channeled.Application.Abstractions.Storage;

public sealed class UploadLimitsOptions
{
    public const string SectionName = "UploadLimits";

    public long MaxFileBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public int PartSizeBytes { get; set; } = 8 * 1024 * 1024;
    public int UploadSessionExpiryHours { get; set; } = 24;
    public int TrashRetentionDays { get; set; } = 30;
}
