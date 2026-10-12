using System.Text;
using Channeled.Application.Abstractions.Storage;
using Channeled.Domain.Storage;
using Channeled.Infrastructure.Storage;
using FluentAssertions;

namespace Channeled.Tests.Integration.Storage;

public class FileStoreRoundTripTests
{
    [Fact]
    public async Task Filesystem_store_round_trips_object_on_real_disk()
    {
        var root = Path.Combine(Path.GetTempPath(), "channeled-fs-int-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            IFileStore store = new FilesystemFileStore(new FileStoreOptions { RootPath = root });
            var key = ObjectKey.ForFile(Guid.NewGuid());
            var payload = Encoding.UTF8.GetBytes("integration-round-trip-" + Guid.NewGuid());

            await using (var input = new MemoryStream(payload))
            {
                await store.PutAsync(key, input, "application/octet-stream");
            }

            (await store.ExistsAsync(key)).Should().BeTrue();

            await using var output = await store.OpenReadAsync(key);
            using var buffer = new MemoryStream();
            await output.CopyToAsync(buffer);
            buffer.ToArray().Should().Equal(payload);

            await store.DeleteAsync(key);
            (await store.ExistsAsync(key)).Should().BeFalse();
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
    public async Task S3_store_round_trips_when_endpoint_configured()
    {
        var endpoint = Environment.GetEnvironmentVariable("CHANNELED_S3_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // MinIO not required for default CI; enable with CHANNELED_S3_* env vars.
            return;
        }

        var options = new FileStoreOptions
        {
            Kind = FileStoreKind.S3Compatible,
            Endpoint = endpoint,
            AccessKey = Environment.GetEnvironmentVariable("CHANNELED_S3_ACCESS_KEY") ?? "minioadmin",
            SecretKey = Environment.GetEnvironmentVariable("CHANNELED_S3_SECRET_KEY") ?? "minioadmin",
            Bucket = Environment.GetEnvironmentVariable("CHANNELED_S3_BUCKET") ?? "channeled",
            ForcePathStyle = true,
        };

        await using var store = new S3FileStore(options);
        var key = ObjectKey.ForFile(Guid.NewGuid());
        var payload = Encoding.UTF8.GetBytes("s3-round-trip");

        await using (var input = new MemoryStream(payload))
        {
            await store.PutAsync(key, input, "application/octet-stream");
        }

        await using var output = await store.OpenReadAsync(key);
        using var buffer = new MemoryStream();
        await output.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(payload);

        await store.DeleteAsync(key);
    }
}
