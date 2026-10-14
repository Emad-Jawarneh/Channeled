namespace Channeled.Infrastructure.Identity;

public sealed class PendingEmailChange
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string NewEmail { get; set; } = "";
    public string NewEmailNormalized { get; set; } = "";
    public string ConfirmTokenHash { get; set; } = "";
    public string CancelTokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ConfirmExpiresAt { get; set; }
    public DateTimeOffset CancelExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}
