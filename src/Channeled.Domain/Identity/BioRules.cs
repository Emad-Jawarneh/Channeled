using System.Globalization;
using System.Text;

namespace Channeled.Domain.Identity;

public static class BioRules
{
    public const int MaxLength = 280;

    public static string Cleanup(string? bio)
    {
        if (string.IsNullOrWhiteSpace(bio))
        {
            return "";
        }

        var nfc = bio.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(nfc.Length);
        foreach (var rune in nfc.EnumerateRunes())
        {
            if (IsStripped(rune))
            {
                continue;
            }

            sb.Append(rune.ToString());
        }

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length > MaxLength)
        {
            cleaned = cleaned[..MaxLength].TrimEnd();
        }

        return cleaned;
    }

    /// <summary>Optional field: empty after cleanup is valid.</summary>
    public static bool TryValidate(string? bio, out string cleaned, out string? error)
    {
        if (bio is not null && bio.Length > 4_000)
        {
            cleaned = "";
            error = $"Bio must be at most {MaxLength} characters.";
            return false;
        }

        cleaned = Cleanup(bio);
        error = null;
        return true;
    }

    private static bool IsStripped(Rune rune)
    {
        var cat = Rune.GetUnicodeCategory(rune);
        if (cat is UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.OtherNotAssigned)
        {
            return true;
        }

        var v = rune.Value;
        return v is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF
            or (>= 0x202A and <= 0x202E)
            or (>= 0x2066 and <= 0x2069);
    }
}
