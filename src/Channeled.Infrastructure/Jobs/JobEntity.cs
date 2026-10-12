namespace Channeled.Infrastructure.Jobs;

public sealed class JobEntity
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string State { get; set; } = "pending";
    public string? DetailsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}
