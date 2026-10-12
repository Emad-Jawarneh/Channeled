using System.Diagnostics;
using System.Security.Cryptography;
using Channeled.Application.Abstractions.Storage;
using Channeled.Domain.Storage;
using Channeled.Infrastructure.Storage;
using FluentAssertions;

namespace Channeled.Tests.Integration.Storage;

public class MultipartResumeAndLargeFileTests
{
    private static long GateBytes =>
        long.TryParse(Environment.GetEnvironmentVariable("CHANNELED_GATE_UPLOAD_BYTES"), out var n) && n > 0
            ? n
            : 2L * 1024 * 1024 * 1024;

    [Fact]
    public async Task Multipart_survives_process_restart_and_verifies_sha256()
    {
        var root = Path.Combine(Path.GetTempPath(), "channeled-mp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var key = ObjectKey.ForFile(Guid.NewGuid());
            const int partSize = 64 * 1024;
            var payload = new byte[partSize * 3 + 100];
            RandomNumberGenerator.Fill(payload);
            var expectedSha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

            string uploadId;
            var etags = new List<string>();
            {
                IFileStore store1 = new FilesystemFileStore(new FileStoreOptions { RootPath = root });
                uploadId = await store1.BeginMultipartUploadAsync(key, "application/octet-stream");
                await using var part1 = new MemoryStream(payload, 0, partSize);
                etags.Add(await store1.UploadPartAsync(key, uploadId, 1, part1));
                await using var part2 = new MemoryStream(payload, partSize, partSize);
                etags.Add(await store1.UploadPartAsync(key, uploadId, 2, part2));
            }

            // "Kill" process: new store instance, resume remaining parts from disk metadata.
            {
                IFileStore store2 = new FilesystemFileStore(new FileStoreOptions { RootPath = root });
                await using var part3 = new MemoryStream(payload, partSize * 2, payload.Length - partSize * 2);
                etags.Add(await store2.UploadPartAsync(key, uploadId, 3, part3));
                await store2.CompleteMultipartUploadAsync(key, uploadId, etags);
            }

            IFileStore store3 = new FilesystemFileStore(new FileStoreOptions { RootPath = root });
            await using var read = await store3.OpenReadAsync(key);
            using var sha = SHA256.Create();
            var actual = Convert.ToHexString(await sha.ComputeHashAsync(read)).ToLowerInvariant();
            actual.Should().Be(expectedSha);

            await using var ranged = await store3.OpenReadAsync(key, 10, 19);
            var buf = new byte[10];
            (await ranged.ReadAsync(buf)).Should().Be(10);
            buf.Should().Equal(payload.AsSpan(10, 10).ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Two_gigabyte_multipart_upload_download_uses_bounded_memory()
    {
        var totalBytes = GateBytes;
        var root = Path.Combine(Path.GetTempPath(), "channeled-2g-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            IFileStore store = new FilesystemFileStore(new FileStoreOptions { RootPath = root });
            var key = ObjectKey.ForFile(Guid.NewGuid());
            const int partSize = 8 * 1024 * 1024;
            var uploadId = await store.BeginMultipartUploadAsync(key, "application/octet-stream");

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            var baseline = Process.GetCurrentProcess().WorkingSet64;
            long peak = baseline;

            var etags = new List<string>();
            var partNumber = 1;
            long remaining = totalBytes;
            using var uploadSha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            while (remaining > 0)
            {
                var size = (int)Math.Min(partSize, remaining);
                await using var part = new PatternStream(size, seed: partNumber);
                await using var tee = new HashingReadStream(part, uploadSha);
                etags.Add(await store.UploadPartAsync(key, uploadId, partNumber, tee));
                remaining -= size;
                partNumber++;
                peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64);
            }

            await store.CompleteMultipartUploadAsync(key, uploadId, etags);
            var expectedSha = Convert.ToHexString(uploadSha.GetHashAndReset()).ToLowerInvariant();

            peak = Math.Max(peak, Process.GetCurrentProcess().WorkingSet64);
            var delta = peak - baseline;
            // Allow headroom for GC/runtime; must stay far below file size (constant-memory streaming).
            delta.Should().BeLessThan(256L * 1024 * 1024, $"working set grew by {delta} bytes");

            using var downloadSha = SHA256.Create();
            await using var read = await store.OpenReadAsync(key);
            var actualSha = Convert.ToHexString(await downloadSha.ComputeHashAsync(read)).ToLowerInvariant();
            actualSha.Should().Be(expectedSha);

            // Range download of last 16 bytes
            var rangeStart = totalBytes - 16;
            await using var ranged = await store.OpenReadAsync(key, rangeStart, totalBytes - 1);
            var tail = new byte[16];
            var readCount = 0;
            while (readCount < 16)
            {
                var n = await ranged.ReadAsync(tail.AsMemory(readCount, 16 - readCount));
                n.Should().BeGreaterThan(0);
                readCount += n;
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                    // Best effort cleanup of large temp tree.
                }
            }
        }
    }

    /// <summary>Deterministic stream of `length` bytes without buffering the whole payload.</summary>
    private sealed class PatternStream : Stream
    {
        private readonly int _length;
        private readonly int _seed;
        private int _position;

        public PatternStream(int length, int seed)
        {
            _length = length;
            _seed = seed;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length)
            {
                return 0;
            }

            var toWrite = Math.Min(count, _length - _position);
            for (var i = 0; i < toWrite; i++)
            {
                buffer[offset + i] = (byte)((_seed * 131 + _position + i) & 0xFF);
            }

            _position += toWrite;
            return toWrite;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class HashingReadStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if (read > 0)
            {
                hash.AppendData(buffer, offset, read);
            }

            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                hash.AppendData(buffer, offset, read);
            }

            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                hash.AppendData(buffer.Span[..read]);
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
