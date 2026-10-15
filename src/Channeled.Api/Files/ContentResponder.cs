using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using Channeled.Application.Abstractions.Storage;
using Channeled.Application.Files;
using Channeled.Domain.Errors;
using Microsoft.Net.Http.Headers;

namespace Channeled.Api.Files;

public enum RangeKind
{
    Full,
    Partial,
    Unsatisfiable,
}

/// <summary>What to send for a request: the whole file, one byte range, or 416. <c>IsResume</c> means a range
/// guarded by a matching If-Range (what browsers send when resuming a download).</summary>
public readonly record struct RangeDecision(RangeKind Kind, long Start, long EndInclusive, bool IsResume);

/// <param name="AsAttachment">Content-Disposition attachment vs inline.</param>
/// <param name="CountTowardsLimit">Take a concurrent download slot and apply the per-stream egress cap.</param>
/// <param name="NoStore">Ticket downloads: no-store, no referrer, sandbox CSP; inline content may revalidate (ETag/304).</param>
public sealed record ContentResponseOptions(bool AsAttachment, bool CountTowardsLimit, bool NoStore, Guid UserId);

/// <summary>
/// One place for file responses (channel content, personal content, download tickets): strong ETag from the
/// server-computed SHA-256 (content is immutable per file id), Last-Modified, Accept-Ranges, single-range and
/// suffix-range support, If-Range, If-None-Match, 416, RFC 6266 filenames, slots and per-stream throttling.
/// </summary>
public static class ContentResponder
{
    private const int BufferSize = 64 * 1024;

    public static string ETagFor(DownloadTarget target) =>
        string.IsNullOrWhiteSpace(target.Sha256)
            ? $"\"{target.FileId:N}-{target.SizeBytes}\""
            : $"\"{target.Sha256}\"";

    public static RangeDecision Evaluate(HttpRequest request, DownloadTarget target)
    {
        var size = target.SizeBytes;
        var full = new RangeDecision(RangeKind.Full, 0, Math.Max(0, size - 1), false);
        var rawRange = request.Headers[HeaderNames.Range].ToString();
        if (string.IsNullOrWhiteSpace(rawRange))
        {
            return full;
        }

        // Malformed, non-byte, or multi-range requests are answered with the whole representation (RFC 9110 §14.2).
        if (!RangeHeaderValue.TryParse(rawRange, out var parsed)
            || !string.Equals(parsed.Unit.Value, "bytes", StringComparison.OrdinalIgnoreCase)
            || parsed.Ranges.Count != 1)
        {
            return full;
        }

        var ifRange = request.Headers[HeaderNames.IfRange].ToString();
        var guarded = !string.IsNullOrWhiteSpace(ifRange);
        if (guarded && !IfRangeMatches(ifRange, target))
        {
            return full;
        }

        var range = parsed.Ranges.First();
        long start;
        long end;
        if (range.From is long from)
        {
            start = from;
            end = range.To is long to ? Math.Min(to, size - 1) : size - 1;
        }
        else if (range.To is long suffix)
        {
            if (suffix <= 0)
            {
                return new RangeDecision(RangeKind.Unsatisfiable, 0, 0, false);
            }

            start = Math.Max(0, size - suffix);
            end = size - 1;
        }
        else
        {
            return full;
        }

        if (size == 0 || start >= size || start > end)
        {
            return new RangeDecision(RangeKind.Unsatisfiable, 0, 0, false);
        }

        return new RangeDecision(RangeKind.Partial, start, end, guarded);
    }

    public static async Task<IResult> WriteAsync(
        HttpContext http,
        DownloadTarget target,
        RangeDecision decision,
        ContentResponseOptions options,
        IFileStore store,
        IDownloadSlots slots,
        long perStreamBytesPerSecond,
        CancellationToken ct)
    {
        var response = http.Response;
        var headers = response.Headers;
        var etag = ETagFor(target);
        headers.XContentTypeOptions = "nosniff";
        headers.AcceptRanges = "bytes";
        headers.ETag = etag;
        headers.LastModified = TruncateToSeconds(target.CreatedAt).ToString("R", CultureInfo.InvariantCulture);
        if (options.NoStore)
        {
            headers.CacheControl = "private, no-store";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        }
        else
        {
            headers.CacheControl = "private, no-cache";
            if (decision.Kind == RangeKind.Full && IfNoneMatch(http.Request, etag))
            {
                response.StatusCode = StatusCodes.Status304NotModified;
                return Results.Empty;
            }
        }

        if (decision.Kind == RangeKind.Unsatisfiable)
        {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            headers.ContentRange = $"bytes */{target.SizeBytes}";
            return Results.Empty;
        }

        IDisposable? slot = null;
        if (options.CountTowardsLimit)
        {
            slot = slots.TryAcquire(options.UserId);
            if (slot is null)
            {
                return Results.Json(
                    new
                    {
                        type = $"https://channeled.local/errors/{ErrorCodes.TooManyDownloads}",
                        title = ErrorCodes.TooManyDownloads,
                        status = StatusCodes.Status429TooManyRequests,
                        detail = "Too many downloads are running. Try again when one finishes.",
                        code = ErrorCodes.TooManyDownloads,
                    },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }
        }

        try
        {
            var disposition = new ContentDispositionHeaderValue(options.AsAttachment ? "attachment" : "inline");
            disposition.SetHttpFileName(target.FileName);
            headers.ContentDisposition = disposition.ToString();
            response.ContentType = string.IsNullOrWhiteSpace(target.ContentType) ? "application/octet-stream" : target.ContentType;

            var length = target.SizeBytes == 0 ? 0 : decision.EndInclusive - decision.Start + 1;
            if (decision.Kind == RangeKind.Partial)
            {
                response.StatusCode = StatusCodes.Status206PartialContent;
                headers.ContentRange = $"bytes {decision.Start}-{decision.EndInclusive}/{target.SizeBytes}";
            }

            response.ContentLength = length;
            if (length == 0)
            {
                return Results.Empty;
            }

            await using var content = await store.OpenReadAsync(target.StorageKey, decision.Start, decision.EndInclusive, ct);
            try
            {
                await CopyAsync(content, response.Body, options.CountTowardsLimit ? perStreamBytesPerSecond : 0, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client went away; the slot is released below.
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException && response.HasStarted)
            {
                // Store failure mid-stream: drop the connection so the client sees a failed (not truncated) download.
                http.Abort();
            }

            return Results.Empty;
        }
        finally
        {
            slot?.Dispose();
        }
    }

    private static bool IfRangeMatches(string ifRange, DownloadTarget target)
    {
        var value = ifRange.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            return false; // If-Range requires a strong validator.
        }

        if (value.StartsWith('"'))
        {
            return string.Equals(value, ETagFor(target), StringComparison.Ordinal);
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
               && date.ToUniversalTime() == TruncateToSeconds(target.CreatedAt);
    }

    private static bool IfNoneMatch(HttpRequest request, string etag)
    {
        var raw = request.Headers[HeaderNames.IfNoneMatch].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        foreach (var part in raw.Split(','))
        {
            var candidate = part.Trim();
            if (candidate == "*" || candidate == etag || candidate == "W/" + etag)
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }

    private static async Task CopyAsync(Stream source, Stream destination, long bytesPerSecond, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            // Small chunks when throttled so pacing is smooth; full buffer otherwise.
            var chunk = bytesPerSecond > 0
                ? (int)Math.Clamp(bytesPerSecond / 10, 1024, BufferSize)
                : BufferSize;
            var started = Stopwatch.GetTimestamp();
            long sent = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, chunk), ct)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                sent += read;
                if (bytesPerSecond > 0)
                {
                    var due = TimeSpan.FromSeconds((double)sent / bytesPerSecond);
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    if (due > elapsed)
                    {
                        await Task.Delay(due - elapsed, ct);
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
