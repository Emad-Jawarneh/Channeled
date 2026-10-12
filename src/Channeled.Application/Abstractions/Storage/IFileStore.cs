namespace Channeled.Application.Abstractions.Storage;

/// <summary>
/// Object storage port. All byte I/O streams; never buffer whole files.
/// </summary>
public interface IFileStore
{
    Task PutAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string objectKey,
        long? rangeStart = null,
        long? rangeEndInclusive = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default);

    Task<string> BeginMultipartUploadAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<string> UploadPartAsync(
        string objectKey,
        string uploadId,
        int partNumber,
        Stream content,
        CancellationToken cancellationToken = default);

    Task CompleteMultipartUploadAsync(
        string objectKey,
        string uploadId,
        IReadOnlyList<string> partETags,
        CancellationToken cancellationToken = default);

    Task AbortMultipartUploadAsync(
        string objectKey,
        string uploadId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists object keys (excludes multipart scratch and metadata sidecars).</summary>
    Task<IReadOnlyList<string>> ListObjectKeysAsync(CancellationToken cancellationToken = default);
}
