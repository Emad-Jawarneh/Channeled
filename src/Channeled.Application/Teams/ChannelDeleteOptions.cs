namespace Channeled.Application.Teams;

public sealed class ChannelDeleteOptions
{
    public const string SectionName = "ChannelDelete";

    public int MaxFilesPerTxn { get; set; } = 10_000;

    public int MaxMessagesPerTxn { get; set; } = 10_000;

    public int ObjectDeleteChunkSize { get; set; } = 2_000;

    public string LockTimeout { get; set; } = "5s";

    public string StatementTimeout { get; set; } = "60s";

    public int ObjectDeleteMaxAttempts { get; set; } = 8;
}
