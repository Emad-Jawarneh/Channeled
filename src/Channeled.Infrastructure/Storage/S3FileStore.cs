using Amazon.S3;
using Amazon.S3.Model;
using Channeled.Application.Abstractions.Storage;

namespace Channeled.Infrastructure.Storage;

public sealed class S3FileStore : IFileStore, IAsyncDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly bool _ownsClient;

    public S3FileStore(FileStoreOptions options)
        : this(CreateClient(options), options.Bucket, ownsClient: true)
    {
    }

    public S3FileStore(IAmazonS3 client, string bucket, bool ownsClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new ArgumentException("Bucket is required.", nameof(bucket));
        }

        _bucket = bucket;
        _ownsClient = ownsClient;
    }

    public async Task PutAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
        };

        await _client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> OpenReadAsync(
        string objectKey,
        long? rangeStart = null,
        long? rangeEndInclusive = null,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var request = new GetObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
        };

        if (rangeStart is not null || rangeEndInclusive is not null)
        {
            var start = rangeStart ?? 0;
            request.ByteRange = rangeEndInclusive is null
                ? new ByteRange(start, long.MaxValue)
                : new ByteRange(start, rangeEndInclusive.Value);
        }

        var response = await _client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ResponseStream;
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        await _client.DeleteObjectAsync(_bucket, objectKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, objectKey, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<string> BeginMultipartUploadAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var response = await _client.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest
            {
                BucketName = _bucket,
                Key = objectKey,
                ContentType = contentType,
            },
            cancellationToken).ConfigureAwait(false);

        return response.UploadId;
    }

    public async Task<string> UploadPartAsync(
        string objectKey,
        string uploadId,
        int partNumber,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var response = await _client.UploadPartAsync(
            new UploadPartRequest
            {
                BucketName = _bucket,
                Key = objectKey,
                UploadId = uploadId,
                PartNumber = partNumber,
                InputStream = content,
            },
            cancellationToken).ConfigureAwait(false);

        return response.ETag;
    }

    public async Task CompleteMultipartUploadAsync(
        string objectKey,
        string uploadId,
        IReadOnlyList<string> partETags,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        var parts = partETags
            .Select((etag, index) => new PartETag(index + 1, etag))
            .ToList();

        await _client.CompleteMultipartUploadAsync(
            new CompleteMultipartUploadRequest
            {
                BucketName = _bucket,
                Key = objectKey,
                UploadId = uploadId,
                PartETags = parts,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task AbortMultipartUploadAsync(
        string objectKey,
        string uploadId,
        CancellationToken cancellationToken = default)
    {
        ValidateKey(objectKey);
        await _client.AbortMultipartUploadAsync(
            new AbortMultipartUploadRequest
            {
                BucketName = _bucket,
                Key = objectKey,
                UploadId = uploadId,
            },
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static IAmazonS3 CreateClient(FileStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new ArgumentException("Endpoint is required for S3 store.", nameof(options));
        }

        var config = new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = options.ForcePathStyle,
            AuthenticationRegion = options.Region,
        };

        return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
    }

    private static void ValidateKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }
    }
}
