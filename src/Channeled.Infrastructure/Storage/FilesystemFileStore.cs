using System.Collections.Concurrent;
using System.Text.Json;
using Channeled.Application.Abstractions.Storage;

namespace Channeled.Infrastructure.Storage;

public sealed class FilesystemFileStore : IFileStore
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, MultipartState> _uploads = new();

    public FilesystemFileStore(FileStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new ArgumentException("RootPath is required for filesystem store.", nameof(options));
        }

        _root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, ".multipart"));
    }

    public async Task PutAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var path = ResolvePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        await WriteSidecarAsync(path, contentType, cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream> OpenReadAsync(
        string objectKey,
        long? rangeStart = null,
        long? rangeEndInclusive = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var path = ResolvePath(objectKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Object '{objectKey}' was not found.", path);
        }

        var length = new FileInfo(path).Length;
        var start = rangeStart ?? 0;
        var end = rangeEndInclusive ?? (length - 1);

        if (start < 0 || end < start || start >= length)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeStart), "Invalid byte range.");
        }

        end = Math.Min(end, length - 1);
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (start > 0)
        {
            stream.Seek(start, SeekOrigin.Begin);
        }

        var count = end - start + 1;
        return Task.FromResult<Stream>(new BoundedReadStream(stream, count));
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var path = ResolvePath(objectKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var sidecar = SidecarPath(path);
        if (File.Exists(sidecar))
        {
            File.Delete(sidecar);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        return Task.FromResult(File.Exists(ResolvePath(objectKey)));
    }

    public Task<string> BeginMultipartUploadAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var uploadId = Guid.NewGuid().ToString("N");
        var dir = MultipartDir(uploadId);
        Directory.CreateDirectory(dir);
        var state = new MultipartState(objectKey, contentType, dir);
        _uploads[uploadId] = state;
        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(state));
        return Task.FromResult(uploadId);
    }

    public async Task<string> UploadPartAsync(
        string objectKey,
        string uploadId,
        int partNumber,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        if (partNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(partNumber));
        }

        var state = GetUpload(uploadId);
        if (!string.Equals(state.ObjectKey, objectKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Upload id does not match object key.");
        }

        var partPath = Path.Combine(state.Directory, $"part-{partNumber:D5}");
        await using (var file = new FileStream(
                         partPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var etag = $"{partNumber}-{new FileInfo(partPath).Length}";
        state.Parts[partNumber] = etag;
        return etag;
    }

    public async Task CompleteMultipartUploadAsync(
        string objectKey,
        string uploadId,
        IReadOnlyList<string> partETags,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var state = GetUpload(uploadId);
        if (!string.Equals(state.ObjectKey, objectKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Upload id does not match object key.");
        }

        var path = ResolvePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var output = new FileStream(
                         path,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            foreach (var partNumber in state.Parts.Keys.OrderBy(n => n))
            {
                var partPath = Path.Combine(state.Directory, $"part-{partNumber:D5}");
                await using var part = new FileStream(
                    partPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                await part.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }

        await WriteSidecarAsync(path, state.ContentType, cancellationToken).ConfigureAwait(false);
        CleanupUpload(uploadId, state);
    }

    public Task AbortMultipartUploadAsync(
        string objectKey,
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        if (_uploads.TryRemove(uploadId, out var state))
        {
            if (Directory.Exists(state.Directory))
            {
                Directory.Delete(state.Directory, recursive: true);
            }
        }
        else
        {
            var dir = MultipartDir(uploadId);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private MultipartState GetUpload(string uploadId)
    {
        if (_uploads.TryGetValue(uploadId, out var state))
        {
            return state;
        }

        var dir = MultipartDir(uploadId);
        var meta = Path.Combine(dir, "meta.json");
        if (!File.Exists(meta))
        {
            throw new InvalidOperationException($"Unknown multipart upload '{uploadId}'.");
        }

        state = JsonSerializer.Deserialize<MultipartState>(File.ReadAllText(meta))
                ?? throw new InvalidOperationException($"Corrupt multipart metadata for '{uploadId}'.");
        _uploads[uploadId] = state;
        return state;
    }

    private void CleanupUpload(string uploadId, MultipartState state)
    {
        _uploads.TryRemove(uploadId, out _);
        if (Directory.Exists(state.Directory))
        {
            Directory.Delete(state.Directory, recursive: true);
        }
    }

    private string ResolvePath(string objectKey)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Object key escapes store root.");
        }

        return combined;
    }

    private string MultipartDir(string uploadId) => Path.Combine(_root, ".multipart", uploadId);

    private static string SidecarPath(string objectPath) => objectPath + ".meta.json";

    private static async Task WriteSidecarAsync(string objectPath, string contentType, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { contentType });
        await File.WriteAllTextAsync(SidecarPath(objectPath), json, ct).ConfigureAwait(false);
    }

    private static void ValidateKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }

        if (objectKey.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(objectKey))
        {
            throw new ArgumentException("Object key is invalid.", nameof(objectKey));
        }
    }

    private sealed class MultipartState
    {
        public MultipartState()
        {
            ObjectKey = "";
            ContentType = "application/octet-stream";
            Directory = "";
            Parts = new SortedDictionary<int, string>();
        }

        public MultipartState(string objectKey, string contentType, string directory)
        {
            ObjectKey = objectKey;
            ContentType = contentType;
            Directory = directory;
            Parts = new SortedDictionary<int, string>();
        }

        public string ObjectKey { get; set; }
        public string ContentType { get; set; }
        public string Directory { get; set; }
        public SortedDictionary<int, string> Parts { get; set; }
    }

    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public BoundedReadStream(Stream inner, long count)
        {
            _inner = inner;
            _remaining = count;
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

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, _remaining);
            var read = _inner.Read(buffer, offset, toRead);
            _remaining -= read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var toRead = (int)Math.Min(count, _remaining);
            var read = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken).ConfigureAwait(false);
            _remaining -= read;
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                return ValueTask.FromResult(0);
            }

            if (buffer.Length > _remaining)
            {
                buffer = buffer[..(int)_remaining];
            }

            return ReadAndTrackAsync(buffer, cancellationToken);
        }

        private async ValueTask<int> ReadAndTrackAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _remaining -= read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
