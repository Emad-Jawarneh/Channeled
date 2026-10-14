namespace Channeled.Infrastructure.Identity;

/// <summary>Keyed-hash of submitted email addresses for forgot-password caps.</summary>
public sealed class AddressRateCounter
{
    public string AddressKeyHash { get; set; } = "";
    public int Count { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
