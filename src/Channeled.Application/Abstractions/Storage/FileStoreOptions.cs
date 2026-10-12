namespace Channeled.Application.Abstractions.Storage;

public enum FileStoreKind
{
    FileSystem = 0,
    S3Compatible = 1,
}

public sealed class FileStoreOptions
{
    public const string SectionName = "FileStore";

    public FileStoreKind Kind { get; set; } = FileStoreKind.FileSystem;

    /// <summary>Root directory for the filesystem adapter.</summary>
    public string RootPath { get; set; } = ".data/object-store";

    public string Endpoint { get; set; } = "http://127.0.0.1:9000";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "channeled";
    public string Region { get; set; } = "us-east-1";
    public bool ForcePathStyle { get; set; } = true;
}
