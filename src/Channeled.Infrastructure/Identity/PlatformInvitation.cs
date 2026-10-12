namespace Channeled.Infrastructure.Identity;

public sealed class PlatformInvitation
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public Guid? InvitedByUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
