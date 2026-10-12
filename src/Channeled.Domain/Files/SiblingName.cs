namespace Channeled.Domain.Files;

/// <summary>Case-insensitive sibling name uniqueness among non-deleted items.</summary>
public static class SiblingName
{
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    public static bool EqualsIgnoreCase(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}
