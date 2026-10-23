#nullable enable
using System.Security.Cryptography;
using Template.Application.Interfaces.Files;

namespace Template.Application.Services.Files;

/// <summary>
/// Server-side content checks of the Files module (FIL-002): magic-number sniffing must match the declared media type,
/// and stored bytes are verified by re-reading them and recomputing their SHA-256.
/// </summary>
public static class FileContent
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    /// <summary>Whether the first bytes of a seekable stream match <paramref name="mediaType"/>; the position is restored.</summary>
    public static async Task<bool> MatchesAsync(Stream content, string mediaType, CancellationToken cancellationToken)
    {
        var expected = mediaType switch
        {
            "application/pdf" => PdfSignature,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                or "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ZipSignature,
            "image/png" => PngSignature,
            "image/jpeg" => JpegSignature,
            _ => null
        };
        if (expected is null)
            return false;

        var position = content.Position;
        var buffer = new byte[expected.Length];
        var read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        content.Position = position;
        return read == expected.Length && buffer.AsSpan().SequenceEqual(expected);
    }

    /// <summary>Re-reads the stored object and recomputes its length and SHA-256; <c>null</c> when it is missing.</summary>
    public static async Task<StoredContent?> MeasureAsync(IFileStorage storage, Guid fileId, CancellationToken cancellationToken)
    {
        await using var stream = await storage.OpenReadAsync(fileId, cancellationToken);
        if (stream is null)
            return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            length += read;
        }

        return new StoredContent(length, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }
}
