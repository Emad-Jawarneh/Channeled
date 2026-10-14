using System.Text.RegularExpressions;

namespace Channeled.Domain.Identity;

public static partial class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 30;

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "support", "api", "www", "mail", "root", "system",
        "channeled", "account", "accounts", "auth", "login", "logout", "signup",
        "register", "me", "settings", "help", "null", "undefined",
    };

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public static bool TryValidate(string? username, out string normalized, out string? error)
    {
        normalized = "";
        error = null;
        if (string.IsNullOrWhiteSpace(username))
        {
            error = "Username is required.";
            return false;
        }

        normalized = Normalize(username);
        if (normalized.Length is < MinLength or > MaxLength)
        {
            error = $"Username must be {MinLength}–{MaxLength} characters.";
            return false;
        }

        if (!UsernamePattern().IsMatch(normalized))
        {
            error = "Username may only use lowercase letters, digits, dots, underscores, and hyphens.";
            return false;
        }

        if (normalized[0] is '.' or '_' or '-' || normalized[^1] is '.' or '_' or '-')
        {
            error = "Username cannot start or end with a separator.";
            return false;
        }

        if (normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains("__", StringComparison.Ordinal)
            || normalized.Contains("--", StringComparison.Ordinal)
            || normalized.Contains("._", StringComparison.Ordinal)
            || normalized.Contains("_.", StringComparison.Ordinal)
            || normalized.Contains(".-", StringComparison.Ordinal)
            || normalized.Contains("-.", StringComparison.Ordinal)
            || normalized.Contains("_-", StringComparison.Ordinal)
            || normalized.Contains("-_", StringComparison.Ordinal))
        {
            error = "Username cannot contain consecutive separators.";
            return false;
        }

        if (Reserved.Contains(normalized))
        {
            error = "That username is reserved.";
            return false;
        }

        return true;
    }

    /// <summary>Slugify for migration/backfill; may still need disambiguation.</summary>
    public static string SuggestFrom(string displayOrLocalPart)
    {
        var s = displayOrLocalPart.Trim().ToLowerInvariant();
        var chars = s.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '.').ToArray();
        var slug = new string(chars);
        while (slug.Contains("..", StringComparison.Ordinal))
        {
            slug = slug.Replace("..", ".", StringComparison.Ordinal);
        }

        slug = slug.Trim('.');
        if (slug.Length < MinLength)
        {
            slug = "user";
        }

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('.', '_', '-');
        }

        if (!TryValidate(slug, out var normalized, out _))
        {
            return "user";
        }

        return normalized;
    }

    [GeneratedRegex("^[a-z0-9._-]+$")]
    private static partial Regex UsernamePattern();
}
