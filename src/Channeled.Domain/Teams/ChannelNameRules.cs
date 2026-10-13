namespace Channeled.Domain.Teams;

/// <summary>Shared create/update rules for channel names (mirrored by the SPA).</summary>
public static class ChannelNameRules
{
    public const int MaxLength = 200;

    /// <summary>Returns an error message, or <c>null</c> when valid. Does not check uniqueness.</summary>
    public static string? Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Channel name is required.";
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxLength)
        {
            return $"Channel name must be at most {MaxLength} characters.";
        }

        if (trimmed.Contains('\0', StringComparison.Ordinal))
        {
            return "Channel name contains invalid characters.";
        }

        return null;
    }

    public static string Normalize(string name) => name.Trim();
}
