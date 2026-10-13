namespace Channeled.Infrastructure.Chat;

public static class MentionResolver
{
    /// <summary>
    /// Union of explicit mention ids (must be in <paramref name="members"/>) and <c>@DisplayName</c>
    /// matches in <paramref name="body"/> (longest name wins at each <c>@</c>).
    /// </summary>
    public static HashSet<Guid> Resolve(
        string body,
        IReadOnlyList<(Guid Id, string DisplayName)> members,
        IEnumerable<Guid>? explicitIds)
    {
        var memberIds = members.Select(m => m.Id).ToHashSet();
        var result = new HashSet<Guid>();

        if (explicitIds is not null)
        {
            foreach (var id in explicitIds)
            {
                if (memberIds.Contains(id))
                {
                    result.Add(id);
                }
            }
        }

        var ordered = members
            .Where(m => !string.IsNullOrWhiteSpace(m.DisplayName))
            .OrderByDescending(m => m.DisplayName.Length)
            .ToList();

        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '@')
            {
                continue;
            }

            foreach (var member in ordered)
            {
                var name = member.DisplayName;
                var start = i + 1;
                if (start + name.Length > body.Length)
                {
                    continue;
                }

                if (string.Compare(body, start, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    continue;
                }

                var end = start + name.Length;
                if (end < body.Length && IsNameContinue(body[end]))
                {
                    continue;
                }

                result.Add(member.Id);
                break;
            }
        }

        return result;
    }

    private static bool IsNameContinue(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-' or '.';
}
