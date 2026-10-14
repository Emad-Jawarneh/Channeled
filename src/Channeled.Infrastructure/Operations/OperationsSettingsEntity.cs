namespace Channeled.Infrastructure.Operations;

/// <summary>Singleton row (Id = 1) for operator SMTP verification state.</summary>
public sealed class OperationsSettingsEntity
{
    public int Id { get; set; } = 1;
    public DateTimeOffset? SmtpVerifiedAt { get; set; }
    public string? SmtpSettingsFingerprint { get; set; }
}
