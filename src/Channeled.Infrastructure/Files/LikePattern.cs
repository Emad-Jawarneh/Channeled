namespace Channeled.Infrastructure.Files;

/// <summary>
/// Builds LIKE/ILIKE patterns that match the user's text literally. Always pass <see cref="EscapeCharacter"/> to
/// <c>EF.Functions.Like/ILike</c>: without it the Npgsql provider emits <c>ESCAPE ''</c> and the escapes are ignored.
/// </summary>
public static class LikePattern
{
    public const string EscapeCharacter = "\\";

    public static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary><c>%text%</c> with <c>\ % _</c> escaped.</summary>
    public static string Contains(string text) => "%" + Escape(text) + "%";

    /// <summary><c>text%</c> with <c>\ % _</c> escaped.</summary>
    public static string StartsWith(string text) => Escape(text) + "%";
}
