using Channeled.Application.Abstractions.Results;
using Channeled.Domain.Errors;
using SkiaSharp;

namespace Channeled.Infrastructure.Identity;

public static class AvatarImageProcessor
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const long MaxPixels = 16_000_000;
    public const int OutputSize = 256;

    private static readonly SemaphoreSlim DecodeGate = new(2, 2);

    public static async Task<AppResult<(byte[] Jpeg, string ContentType)>> ProcessAsync(
        Stream input,
        long? contentLength,
        CancellationToken ct)
    {
        if (contentLength is > MaxBytes)
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Avatar must be 2 MB or smaller.");
        }

        await using var limited = new SizeLimitedStream(input, MaxBytes);
        using var ms = new MemoryStream();
        try
        {
            await limited.CopyToAsync(ms, ct);
        }
        catch (InvalidOperationException)
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Avatar must be 2 MB or smaller.");
        }

        var bytes = ms.ToArray();
        if (bytes.Length == 0)
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Empty image.");
        }

        if (!TryDetectFormat(bytes, out _))
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Unsupported image format.");
        }

        if (!await DecodeGate.WaitAsync(TimeSpan.FromSeconds(10), ct))
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Avatar processing busy; try again.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            return await Task.Run(() => DecodeAndResize(bytes), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Avatar processing timed out.");
        }
        catch (Exception)
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Could not decode image.");
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    private static AppResult<(byte[] Jpeg, string ContentType)> DecodeAndResize(byte[] bytes)
    {
        try
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null)
            {
                return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Could not decode image.");
            }

            if (codec.FrameCount > 1)
            {
                return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Animated images are not allowed.");
            }

            var info = codec.Info;
            var pixels = (long)info.Width * info.Height;
            if (pixels <= 0 || pixels > MaxPixels)
            {
                return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Image dimensions are too large.");
            }

            using var original = SKBitmap.Decode(data);
            if (original is null)
            {
                return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Could not decode image.");
            }

            var side = Math.Min(original.Width, original.Height);
            var x = (original.Width - side) / 2;
            var y = (original.Height - side) / 2;
            using var cropped = new SKBitmap(side, side);
            if (!original.ExtractSubset(cropped, new SKRectI(x, y, x + side, y + side)))
            {
                return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Could not crop image.");
            }

            using var resized = cropped.Resize(new SKImageInfo(OutputSize, OutputSize), SKSamplingOptions.Default);
            if (resized is null)
            {
                return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Could not resize image.");
            }

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            return AppResult<(byte[], string)>.Ok((encoded.ToArray(), "image/jpeg"));
        }
        catch (Exception)
        {
            return AppResult<(byte[], string)>.Fail(ErrorCodes.Validation, "Could not decode image.");
        }
    }

    public static bool TryDetectFormat(ReadOnlySpan<byte> bytes, out string format)
    {
        format = "";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            format = "jpeg";
            return true;
        }

        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            format = "png";
            return true;
        }

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            format = "webp";
            return true;
        }

        return false;
    }

    private sealed class SizeLimitedStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            _read += n;
            if (_read > maxBytes)
            {
                throw new InvalidOperationException("too large");
            }

            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            _read += n;
            if (_read > maxBytes)
            {
                throw new InvalidOperationException("too large");
            }

            return n;
        }

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
