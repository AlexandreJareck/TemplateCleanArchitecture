using System.Security.Cryptography;
using Template.Application.Interfaces.Files;

namespace Template.Infrastructure.Files;

/// <summary>
/// Forward-only read wrapper that hashes every byte with SHA-256 and enforces a size limit while the storage client
/// pulls the content, so the digest always describes exactly the bytes that were sent.
/// </summary>
internal sealed class MeasuringReadStream(Stream inner, long maxBytes) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public long BytesRead { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public string Sha256() => Convert.ToHexStringLower(hash.GetHashAndReset());

    public override int Read(byte[] buffer, int offset, int count) => Track(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        return Track(buffer.Span[..read]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            hash.Dispose();
        base.Dispose(disposing);
    }

    private int Track(ReadOnlySpan<byte> read)
    {
        BytesRead += read.Length;
        if (BytesRead > maxBytes)
            throw new FileContentTooLargeException(maxBytes);
        hash.AppendData(read);
        return read.Length;
    }
}
