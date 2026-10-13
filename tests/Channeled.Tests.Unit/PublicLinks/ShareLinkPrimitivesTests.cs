using Channeled.Application.Abstractions.Results;
using Channeled.Application.Abstractions.Time;
using Channeled.Application.PublicLinks;
using Channeled.Infrastructure.PublicLinks;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Channeled.Tests.Unit.PublicLinks;

public class ShareLinkPrimitivesTests
{
    private sealed class Opts(PublicFileLinksOptions value) : IOptionsMonitor<PublicFileLinksOptions>
    {
        public PublicFileLinksOptions CurrentValue => value;
        public PublicFileLinksOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PublicFileLinksOptions, string?> listener) => null;
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class FakeLease : IAnonStreamLease
    {
        public int Disposed;
        public ValueTask ThrottleAsync(int bytes, CancellationToken ct) => ValueTask.CompletedTask;
        public void Dispose() => Disposed++;
    }

    private static PublicFileLinksOptions Options(string key = "key-one", string prev = "") =>
        new() { Enabled = true, HmacKey = key, PreviousHmacKey = prev };

    // ---- signer

    [Fact]
    public void Ticket_round_trips_and_is_fixed_ttl()
    {
        var signer = new HmacTicketSigner(new Opts(Options()));
        var now = DateTimeOffset.UtcNow;
        var t = new ShareTicket(Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(2));
        var s = signer.Sign(t);

        signer.TryVerify(s, now.AddMinutes(119), out var parsed).Should().BeTrue();
        parsed!.LinkId.Should().Be(t.LinkId);
        parsed.TicketId.Should().Be(t.TicketId);
        signer.TryVerify(s, now.AddHours(2).AddSeconds(1), out _).Should().BeFalse();
    }

    [Fact]
    public void Tampered_or_foreign_tickets_are_rejected()
    {
        var signer = new HmacTicketSigner(new Opts(Options()));
        var now = DateTimeOffset.UtcNow;
        var s = signer.Sign(new ShareTicket(Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(2)));

        var parts = s.Split('.');
        signer.TryVerify(parts[0] + "." + parts[1][..^2] + "AA", now, out _).Should().BeFalse();
        signer.TryVerify(parts[1] + "." + parts[0], now, out _).Should().BeFalse();
        signer.TryVerify("", now, out _).Should().BeFalse();
        signer.TryVerify("a.b", now, out _).Should().BeFalse();
        new HmacTicketSigner(new Opts(Options("other"))).TryVerify(s, now, out _).Should().BeFalse();
    }

    [Fact]
    public void Dual_key_rotation_accepts_previous_key_but_signs_with_current()
    {
        var now = DateTimeOffset.UtcNow;
        var oldTicket = new HmacTicketSigner(new Opts(Options("old-key")))
            .Sign(new ShareTicket(Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(1)));

        var rotated = new HmacTicketSigner(new Opts(Options("new-key", prev: "old-key")));
        rotated.TryVerify(oldTicket, now, out _).Should().BeTrue();

        var fresh = rotated.Sign(new ShareTicket(Guid.NewGuid(), Guid.NewGuid(), now, now.AddHours(1)));
        new HmacTicketSigner(new Opts(Options("new-key"))).TryVerify(fresh, now, out _).Should().BeTrue();
        new HmacTicketSigner(new Opts(Options("old-key"))).TryVerify(fresh, now, out _).Should().BeFalse();

        // once the previous key is dropped, old tickets die
        new HmacTicketSigner(new Opts(Options("new-key"))).TryVerify(oldTicket, now, out _).Should().BeFalse();
    }

    // ---- claim idempotency

    [Fact]
    public async Task Concurrent_same_key_runs_factory_once_and_failures_are_not_cached()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new ClaimIdempotencyStore(new Opts(Options()), clock);
        var calls = 0;
        var gate = new TaskCompletionSource();

        async Task<AppResult<ShareClaimDto>> Factory()
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return AppResult<ShareClaimDto>.Ok(new ShareClaimDto("t", clock.UtcNow, "/p", "f", 1));
        }

        var tasks = Enumerable.Range(0, 20).Select(_ => store.GetOrAddAsync("hash", "k", Factory)).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);
        calls.Should().Be(1);
        results.Select(r => r.Value!.Ticket).Distinct().Should().ContainSingle();

        // different token hash with same key is independent
        await store.GetOrAddAsync("hash2", "k", Factory);
        calls.Should().Be(2);

        // failure is not retained
        var failCalls = 0;
        Task<AppResult<ShareClaimDto>> Fail()
        {
            failCalls++;
            return Task.FromResult(AppResult<ShareClaimDto>.Fail("not_found", "x"));
        }

        await store.GetOrAddAsync("h3", "k", Fail);
        await store.GetOrAddAsync("h3", "k", Fail);
        failCalls.Should().Be(2);
    }

    [Fact]
    public async Task Idempotency_window_expires()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new ClaimIdempotencyStore(new Opts(Options()), clock);
        var calls = 0;
        Task<AppResult<ShareClaimDto>> Factory()
        {
            calls++;
            return Task.FromResult(AppResult<ShareClaimDto>.Ok(new ShareClaimDto("t" + calls, clock.UtcNow, "/p", "f", 1)));
        }

        await store.GetOrAddAsync("h", "k", Factory);
        clock.UtcNow = clock.UtcNow.AddSeconds(59);
        await store.GetOrAddAsync("h", "k", Factory);
        calls.Should().Be(1);
        clock.UtcNow = clock.UtcNow.AddSeconds(2);
        await store.GetOrAddAsync("h", "k", Factory);
        calls.Should().Be(2);
    }

    // ---- limiter

    [Fact]
    public void Limiter_enforces_global_and_per_ip_caps_and_releases()
    {
        var limiter = new AnonDownloadLimiter(new Opts(new PublicFileLinksOptions
        {
            Enabled = true,
            MaxGlobalAnonStreams = 3,
            MaxAnonStreamsPerIp = 2,
        }));

        var a1 = limiter.TryAcquire("1.1.1.1");
        var a2 = limiter.TryAcquire("1.1.1.1");
        limiter.TryAcquire("1.1.1.1").Should().BeNull("per-IP cap");
        var b1 = limiter.TryAcquire("2.2.2.2");
        limiter.TryAcquire("3.3.3.3").Should().BeNull("global cap");

        a1!.Dispose();
        a1.Dispose(); // idempotent
        var c = limiter.TryAcquire("3.3.3.3");
        c.Should().NotBeNull();
        limiter.ActiveStreams.Should().Be(3);

        a2!.Dispose();
        b1!.Dispose();
        c!.Dispose();
        limiter.ActiveStreams.Should().Be(0);
    }

    [Fact]
    public async Task Limiter_throttles_egress_per_stream()
    {
        var limiter = new AnonDownloadLimiter(new Opts(new PublicFileLinksOptions
        {
            Enabled = true,
            PerStreamBytesPerSecond = 100_000,
            GlobalAnonEgressBytesPerSecond = 0,
        }));
        using var lease = limiter.TryAcquire("1.1.1.1")!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 6; i++)
        {
            await lease.ThrottleAsync(10_000, CancellationToken.None); // 60k bytes @100k/s => ~0.5s for 5 waits
        }

        sw.Elapsed.TotalMilliseconds.Should().BeGreaterThan(350);
    }

    // ---- guarded stream (mid-stream recheck / byte cap / lease release)

    [Fact]
    public async Task Guarded_stream_aborts_when_recheck_fails_and_releases_lease()
    {
        var lease = new FakeLease();
        var rechecks = 0;
        var stream = new GuardedDownloadStream(
            new MemoryStream(new byte[100_000]),
            lease,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(1),
            byteCap: 1_000_000,
            new TicketUsageTracker(),
            () => DateTimeOffset.UtcNow,
            _ => Task.FromResult(++rechecks < 2), // second check fails
            TimeSpan.Zero);

        var buf = new byte[1024];
        (await stream.ReadAsync(buf)).Should().BeGreaterThan(0);

        var act = async () => await stream.ReadAsync(buf);
        await act.Should().ThrowAsync<ShareStreamAbortedException>();

        await stream.DisposeAsync();
        lease.Disposed.Should().Be(1);
    }

    [Fact]
    public async Task Guarded_stream_enforces_ticket_byte_cap_across_streams()
    {
        var tracker = new TicketUsageTracker();
        var ticketId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddHours(1);

        GuardedDownloadStream Open() => new(
            new MemoryStream(new byte[3000]),
            new FakeLease(),
            ticketId,
            expires,
            byteCap: 5000,
            tracker,
            () => DateTimeOffset.UtcNow,
            _ => Task.FromResult(true),
            TimeSpan.FromHours(1));

        await using (var first = Open())
        {
            using var sink = new MemoryStream();
            await first.CopyToAsync(sink);
            sink.Length.Should().Be(3000);
        }

        await using var second = Open();
        var act = async () => await second.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<ShareStreamAbortedException>().WithMessage("*byte_cap*");
    }
}
