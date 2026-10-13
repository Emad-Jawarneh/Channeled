namespace Channeled.Application.Abstractions.Realtime;

public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>Allowed Origin values for cookie-authenticated hub connections.</summary>
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173", "http://127.0.0.1:5173"];

    public int MaxConnectionsPerUser { get; set; } = 10;

    public int MaxGroupsPerConnection { get; set; } = 50;

    public long MaxInboundMessageBytes { get; set; } = 8192;

    public int CoalesceQuietMilliseconds { get; set; } = 250;

    public int CoalesceMaxWaitMilliseconds { get; set; } = 2000;

    public int AuthStateSweepSeconds { get; set; } = 10;
}
