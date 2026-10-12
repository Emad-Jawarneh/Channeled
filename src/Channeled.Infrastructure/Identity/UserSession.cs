namespace Channeled.Infrastructure.Identity;

public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string Ip { get; set; } = "";
    public string UserAgent { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }

    public AppUser? User { get; set; }
}
