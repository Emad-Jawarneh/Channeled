using System.Text;
using Channeled.Application.Abstractions.Storage;
using Channeled.Domain.Storage;
using Channeled.Infrastructure.Storage;
using FluentAssertions;

namespace Channeled.Tests.Unit.Storage;

public class FilesystemFileStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FilesystemFileStore _store;

    public FilesystemFileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "channeled-fs-unit-" + Guid.NewGuid().ToString("N"));
        _store = new FilesystemFileStore(new FileStoreOptions { RootPath = _root });
    }

    [Fact]
    public async Task Put_then_read_round_trips_bytes()
    {
        var key = ObjectKey.ForFile(Guid.NewGuid());
        var payload = Encoding.UTF8.GetBytes("hello-channeled");

        await using (var input = new MemoryStream(payload))
        {
            await _store.PutAsync(key, input, "text/plain");
        }

        await using var output = await _store.OpenReadAsync(key);
        using var reader = new MemoryStream();
        await output.CopyToAsync(reader);

        reader.ToArray().Should().Equal(payload);
        (await _store.ExistsAsync(key)).Should().BeTrue();
    }

    [Fact]
    public async Task Multipart_upload_assembles_parts_in_order()
    {
        var key = ObjectKey.ForFile(Guid.NewGuid());
        var uploadId = await _store.BeginMultipartUploadAsync(key, "application/octet-stream");

        var etags = new List<string>();
        foreach (var (part, number) in new[] { ("aaa", 1), ("bbb", 2), ("ccc", 3) })
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(part));
            etags.Add(await _store.UploadPartAsync(key, uploadId, number, stream));
        }

        await _store.CompleteMultipartUploadAsync(key, uploadId, etags);

        await using var output = await _store.OpenReadAsync(key);
        using var reader = new StreamReader(output, Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Be("aaabbbccc");
    }

    [Fact]
    public async Task OpenRead_supports_byte_range()
    {
        var key = ObjectKey.ForFile(Guid.NewGuid());
        await using (var input = new MemoryStream(Encoding.UTF8.GetBytes("0123456789")))
        {
            await _store.PutAsync(key, input, "text/plain");
        }

        await using var ranged = await _store.OpenReadAsync(key, rangeStart: 2, rangeEndInclusive: 5);
        using var reader = new StreamReader(ranged, Encoding.UTF8);
        (await reader.ReadToEndAsync()).Should().Be("2345");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
