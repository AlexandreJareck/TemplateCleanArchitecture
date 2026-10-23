#nullable enable
using Template.Domain.Files;

namespace Template.Application.Interfaces.Files;

/// <summary>Length and lowercase hexadecimal SHA-256 of the bytes the storage adapter actually wrote or read.</summary>
public sealed record StoredContent(long Length, string Sha256);

/// <summary>
/// Provider-neutral blob storage (ADR-0004, FIL-001). Bytes are addressed only by the <see cref="StoredFile"/> ID;
/// physical keys, container names, credentials and storage URLs never leave the adapter. Containers are private.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Streams <paramref name="content"/> to storage, computing its SHA-256 while writing. A write never overwrites an
    /// existing object (the same ID written twice fails). More than <paramref name="maxBytes"/> bytes fail with
    /// <see cref="FileContentTooLargeException"/> and leave no object behind.
    /// </summary>
    /// <exception cref="FileStorageUnavailableException">The storage service is unreachable, timed out or rejected the request.</exception>
    Task<StoredContent> WriteAsync(Guid fileId, Stream content, string mediaType, long maxBytes, CancellationToken cancellationToken);

    /// <summary>Opens a read stream, or <c>null</c> when the object does not exist.</summary>
    /// <exception cref="FileStorageUnavailableException">The storage service is unreachable or timed out.</exception>
    Task<Stream?> OpenReadAsync(Guid fileId, CancellationToken cancellationToken);

    /// <summary>Deletes the object; returns <c>false</c> when it did not exist (idempotent).</summary>
    Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid fileId, CancellationToken cancellationToken);
}

public sealed class FileStorageUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class FileContentTooLargeException(long maxBytes) : Exception($"The content exceeds {maxBytes} bytes.")
{
    public long MaxBytes { get; } = maxBytes;
}

public enum MalwareScanVerdict
{
    /// <summary>No scanner is configured; nothing is claimed about the content (FIL-006).</summary>
    NotScanned,
    Clean,
    Infected
}

/// <summary>Malware-scan extension point (FIL-006). The default adapter scans nothing and says so.</summary>
public interface IMalwareScanner
{
    Task<MalwareScanVerdict> ScanAsync(Guid fileId, string mediaType, CancellationToken cancellationToken);
}

/// <summary>Metadata repository of <see cref="StoredFile"/>.</summary>
public interface IStoredFileRepository
{
    void Add(StoredFile file);

    Task<StoredFile?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>FIL-005 selection: pending files older than the pending lifetime and available files past expiry, oldest first.</summary>
    Task<IReadOnlyList<StoredFile>> ListCleanupCandidatesAsync(FileRetentionPolicy policy, DateTime nowUtc, int max, CancellationToken cancellationToken);
}
