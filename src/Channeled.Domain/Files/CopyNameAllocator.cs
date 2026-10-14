namespace Channeled.Domain.Files;

/// <summary>Allocates a sibling-unique name using " (copy)" / " (copy N)" suffixes.</summary>
public static class CopyNameAllocator
{
    public static string Allocate(string desiredName, IEnumerable<string> existingSiblingNames)
    {
        var existing = new HashSet<string>(
            existingSiblingNames.Select(SiblingName.Normalize),
            StringComparer.Ordinal);

        if (!existing.Contains(SiblingName.Normalize(desiredName)))
        {
            return TrimToMaxLength(desiredName);
        }

        var (stem, ext) = SplitExtension(desiredName);
        var first = $"{stem} (copy){ext}";
        if (!existing.Contains(SiblingName.Normalize(first)))
        {
            return TrimToMaxLength(first);
        }

        for (var n = 2; n < 10_000; n++)
        {
            var candidate = $"{stem} (copy {n}){ext}";
            if (!existing.Contains(SiblingName.Normalize(candidate)))
            {
                return TrimToMaxLength(candidate);
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique copy name.");
    }

    internal static (string Stem, string Extension) SplitExtension(string name)
    {
        var lastDot = name.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= name.Length - 1)
        {
            return (name, "");
        }

        return (name[..lastDot], name[lastDot..]);
    }

    private static string TrimToMaxLength(string name) =>
        name.Length <= FolderRules.MaxNameLength ? name : name[..FolderRules.MaxNameLength];
}
