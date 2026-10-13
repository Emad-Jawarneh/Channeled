using System.Diagnostics;
using Channeled.Application.PublicLinks;

namespace Channeled.Infrastructure.PublicLinks;

/// <summary>Thrown mid-stream when an anonymous download must be aborted (revoked, trashed, byte cap, ...).</summary>
public sealed class ShareStreamAbortedException(string reason) : IOException(reason)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Read-only wrapper around the object-store stream for anonymous downloads. It:
/// throttles egress via the stream lease, enforces the per-ticket byte cap, and re-validates link/file state
/// every <c>MidStreamRecheckSeconds</c>. Disposing releases the anon stream lease.
/// </summary>
public sealed class GuardedDownloadStream : Stream
{
    private const int MaxChunk = 32 * 1024;

    private readonly Stream _inner;
    private readonly IAnonStreamLease _lease;
    private readonly Guid _ticketId;
    private readonly DateTimeOffset _ticketExpiresAt;
    private readonly long _byteCap;
    private readonly TicketUsageTracker _usage;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<CancellationToken, Task<bool>> _recheck;
    private readonly TimeSpan _recheckInterval;
    private long _lastRecheck;
    private bool _disposed;

    public GuardedDownloadStream(
        Stream inner,
        IAnonStreamLease lease,
        Guid ticketId,
        DateTimeOffset ticketExpiresAt,
        long byteCap,
        TicketUsageTracker usage,
        Func<DateTimeOffset> now,
        Func<CancellationToken, Task<bool>> recheck,
        TimeSpan recheckInterval)
    {
        _inner = inner;
        _lease = lease;
        _ticketId = ticketId;
        _ticketExpiresAt = ticketExpiresAt;
        _byteCap = byteCap;
        _usage = usage;
        _now = now;
        _recheck = recheck;
        _recheckInterval = recheckInterval;
        _lastRecheck = Stopwatch.GetTimestamp();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Stopwatch.GetElapsedTime(_lastRecheck) >= _recheckInterval)
        {
            _lastRecheck = Stopwatch.GetTimestamp();
            if (!await _recheck(cancellationToken).ConfigureAwait(false))
            {
                throw new ShareStreamAbortedException("share_link_no_longer_valid");
            }
        }

        var slice = buffer.Length > MaxChunk ? buffer[..MaxChunk] : buffer;
        var read = await _inner.ReadAsync(slice, cancellationToken).ConfigureAwait(false);
        if (read <= 0)
        {
            return read;
        }

        if (_usage.Add(_ticketId, read, _ticketExpiresAt, _now()) > _byteCap)
        {
            throw new ShareStreamAbortedException("ticket_byte_cap_exceeded");
        }

        await _lease.ThrottleAsync(read, cancellationToken).ConfigureAwait(false);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _inner.Dispose();
            _lease.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _inner.DisposeAsync().ConfigureAwait(false);
            _lease.Dispose();
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
