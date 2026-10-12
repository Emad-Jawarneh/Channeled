using Microsoft.AspNetCore.Identity;

namespace Channeled.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsPlatformAdmin { get; set; }
    public long PersonalQuotaBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public long PersonalUsageBytes { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public string Status { get; set; } = "active";
}

public sealed class AppRole : IdentityRole<Guid>;
