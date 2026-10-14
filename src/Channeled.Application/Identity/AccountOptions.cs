namespace Channeled.Application.Identity;

public sealed class AccountOptions
{
    public const string SectionName = "Account";

    /// <summary>Auto = self-service mail when SMTP sender is active and verified; Off = never.</summary>
    public string SelfServiceMail { get; set; } = "Auto";

    /// <summary>Session validation cache TTL in seconds (hard max 5).</summary>
    public int SessionCacheTtlSeconds { get; set; } = 5;

    public int ForgotPasswordPerAddressPerHour { get; set; } = 3;

    public int EmailChangePendingMinutes { get; set; } = 60;

    public int CancelTokenHours { get; set; } = 24;

    public int UsernameCooldownDays { get; set; } = 30;
}

public static class SelfServiceMailModes
{
    public const string Auto = "Auto";
    public const string Off = "Off";
}
