namespace Channeled.Application.PublicLinks;

/// <summary>
/// Limits anonymous downloads only (global + per-IP concurrent streams, global + per-stream egress).
/// Authenticated downloads never go through this limiter.
/// </summary>
public interface IAnonDownloadLimiter
{
    /// <summary>Returns a lease, or null when the global or per-IP stream cap is reached.</summary>
    IAnonStreamLease? TryAcquire(string clientIp);
}

public interface IAnonStreamLease : IDisposable
{
    /// <summary>Delays so that <paramref name="bytes"/> just sent conform to the egress limits.</summary>
    ValueTask ThrottleAsync(int bytes, CancellationToken ct);
}
