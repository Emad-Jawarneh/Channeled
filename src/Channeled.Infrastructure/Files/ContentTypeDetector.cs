namespace Channeled.Infrastructure.Files;

public static class ContentTypeDetector
{
    public static string Detect(string fileName, string? clientHint)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var fromExt = ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            _ => null,
        };

        if (fromExt is not null)
        {
            return fromExt;
        }

        if (!string.IsNullOrWhiteSpace(clientHint)
            && clientHint.Length <= 200
            && clientHint.Contains('/', StringComparison.Ordinal))
        {
            return clientHint.Trim();
        }

        return "application/octet-stream";
    }

    public static bool AllowInline(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
}
