using System.Collections.Concurrent;
using Template.Application.Interfaces.Files;

namespace Template.Infrastructure.Files;

/// <summary>
/// Process-local <see cref="IFileStorage"/> for the Test environment and adapter tests: the same create-only, hashing
/// and size-limit semantics as the blob adapter, with no network.
/// </summary>
public sealed class InMemoryFileStorage : IFileStorage, IFileStorageProbe
{
    private readonly ConcurrentDictionary<Guid, byte[]> objects = new();

    public int Count => objects.Count;

    public async Task<StoredContent> WriteAsync(Guid fileId, Stream content, string mediaType, long maxBytes, CancellationToken cancellationToken)
    {
        await using var measured = new MeasuringReadStream(content, maxBytes);
        using var buffer = new MemoryStream();
        await measured.CopyToAsync(buffer, cancellationToken);
        if (!objects.TryAdd(fileId, buffer.ToArray()))
            throw new InvalidOperationException("A stored file is never overwritten.");
        return new StoredContent(measured.BytesRead, measured.Sha256());
    }

    public Task<Stream?> OpenReadAsync(Guid fileId, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(objects.TryGetValue(fileId, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult(objects.TryRemove(fileId, out _));

    public Task<bool> ExistsAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult(objects.ContainsKey(fileId));

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>
/// Storage used when <c>FileStorageSettings:Enabled</c> is false outside Test: every operation reports unavailability,
/// so report attempts retry and downloads answer 503 while procurement state is unaffected (NFR availability table).
/// </summary>
public sealed class UnavailableFileStorage : IFileStorage, IFileStorageProbe
{
    public Task<StoredContent> WriteAsync(Guid fileId, Stream content, string mediaType, long maxBytes, CancellationToken cancellationToken) =>
        throw Unavailable();

    public Task<Stream?> OpenReadAsync(Guid fileId, CancellationToken cancellationToken) => throw Unavailable();

    public Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken) => throw Unavailable();

    public Task<bool> ExistsAsync(Guid fileId, CancellationToken cancellationToken) => throw Unavailable();

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    private static FileStorageUnavailableException Unavailable() => new("File storage is disabled.");
}

/// <summary>FIL-006 extension point: no scanner is configured, so nothing is claimed about the content.</summary>
public sealed class NoOpMalwareScanner : IMalwareScanner
{
    public Task<MalwareScanVerdict> ScanAsync(Guid fileId, string mediaType, CancellationToken cancellationToken) =>
        Task.FromResult(MalwareScanVerdict.NotScanned);
}
