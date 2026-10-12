namespace Channeled.Domain.Audit;

public sealed class AuditEvent
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid? ActorId { get; init; }
    public AuditEventType EventType { get; init; }
    public Guid? TeamId { get; init; }
    public string TargetType { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string DetailsJson { get; init; } = "{}";
    public string Ip { get; init; } = "";
    public string UserAgent { get; init; } = "";
}
