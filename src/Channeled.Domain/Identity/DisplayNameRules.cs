using System.Globalization;
using System.Text;

namespace Channeled.Domain.Identity;

public static class DisplayNameRules
{
    public const int MaxLength = 80;

    public static string Cleanup(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "";
        }

        var nfc = displayName.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(nfc.Length);
        foreach (var rune in nfc.EnumerateRunes())
        {
            if (IsStripped(rune))
            {
                continue;
            }

            sb.Append(rune.ToString());
        }

        var trimmed = sb.ToString().Trim();
        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed[..MaxLength].TrimEnd();
        }

        return trimmed;
    }

    public static bool TryValidate(string? displayName, out string cleaned, out string? error)
    {
        cleaned = Cleanup(displayName);
        if (string.IsNullOrEmpty(cleaned))
        {
            error = "Display name is required.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Migration fallback when cleanup yields empty or invalid.</summary>
    public static string SanitizeForMigration(string? displayName, string fallbackSeed)
    {
        var cleaned = Cleanup(displayName);
        if (!string.IsNullOrEmpty(cleaned))
        {
            return cleaned;
        }

        var seed = Cleanup(fallbackSeed);
        if (string.IsNullOrEmpty(seed))
        {
            seed = "User";
        }

        if (seed.Length > MaxLength)
        {
            seed = seed[..MaxLength];
        }

        return seed;
    }

    private static bool IsStripped(Rune rune)
    {
        var cat = Rune.GetUnicodeCategory(rune);
        if (cat is UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.OtherNotAssigned)
        {
            return true;
        }

        // Explicit zero-width / bidi controls
        var v = rune.Value;
        return v is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF
            or (>= 0x202A and <= 0x202E)
            or (>= 0x2066 and <= 0x2069);
    }
}
