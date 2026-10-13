namespace Channeled.Application.PublicLinks;

/// <summary>
/// Options for M2 public/anonymous file share links. Bound via IOptionsMonitor so
/// <see cref="Enabled"/> (the kill switch) takes effect without a restart.
/// </summary>
public sealed class PublicFileLinksOptions
{
    public const string SectionName = "PublicFileLinks";

    /// <summary>Feature kill switch. Default OFF.</summary>
    public bool Enabled { get; set; }

    /// <summary>Current HMAC key used to sign download tickets.</summary>
    public string HmacKey { get; set; } = "";

    /// <summary>Previous HMAC key; still accepted for verification during rotation.</summary>
    public string PreviousHmacKey { get; set; } = "";

    public int DefaultExpiryDays { get; set; } = 7;
    public int MaxExpiryDays { get; set; } = 30;
    public int DefaultMaxDownloads { get; set; } = 3;
    public long MaxFileBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public int MaxActiveLinksPerTeam { get; set; } = 50;

    /// <summary>Fixed ticket lifetime. Tickets outlive link expiry; they never slide.</summary>
    public int TicketTtlHours { get; set; } = 2;

    /// <summary>A ticket may serve at most file size x this multiplier bytes in total.</summary>
    public int TicketByteCapMultiplier { get; set; } = 2;

    public long PerStreamBytesPerSecond { get; set; } = 25L * 1024 * 1024;
    public long GlobalAnonEgressBytesPerSecond { get; set; } = 100L * 1024 * 1024;
    public int MaxGlobalAnonStreams { get; set; } = 32;
    public int MaxAnonStreamsPerIp { get; set; } = 4;

    /// <summary>Window in which a repeated claim with the same idempotency key returns the same ticket.</summary>
    public int ClaimIdempotencySeconds { get; set; } = 60;

    /// <summary>Interval at which an in-flight anonymous stream re-validates link/file state.</summary>
    public int MidStreamRecheckSeconds { get; set; } = 5;

    /// <summary>Per-IP request budget for the anonymous /api/v1/public endpoints (fixed window, 1 minute).</summary>
    public int AnonRequestsPerMinutePerIp { get; set; } = 120;

    public bool IsOperational => Enabled && !string.IsNullOrWhiteSpace(HmacKey);
}
