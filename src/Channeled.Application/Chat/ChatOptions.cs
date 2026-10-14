namespace Channeled.Application.Chat;

/// <summary>Text-channel chat settings. Bound via IOptionsMonitor so the rate limit applies without a restart.</summary>
public sealed class ChatOptions
{
    public const string SectionName = "Chat";

    /// <summary>Hard limit on message body length in Unicode code points (mirrors the DB check constraint).</summary>
    public const int MaxBodyLength = 4000;

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>Max structured file attachments per message (deduped server-side).</summary>
    public const int MaxFileRefsPerMessage = 5;

    /// <summary>Per-user budget for POST message (fixed window, 1 minute).</summary>
    public int PostsPerMinutePerUser { get; set; } = 30;

    /// <summary>Soft-deleted (already scrubbed) messages are removed by the purge job after this many days.</summary>
    public int DeletedRetentionDays { get; set; } = 30;

    /// <summary>How often the purge hosted service runs.</summary>
    public int PurgeIntervalHours { get; set; } = 24;
}
