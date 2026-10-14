namespace Channeled.Infrastructure.Identity;

public sealed class UsernameReservation
{
    public string NormalizedUsername { get; set; } = "";
    public Guid PreviousOwnerUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Reason { get; set; } = "cooldown";
}
