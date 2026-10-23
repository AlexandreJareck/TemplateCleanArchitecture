using System;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Files;

/// <summary>Lifecycle of a stored file (domain-model.md §6).</summary>
public enum StoredFileStatus
{
    /// <summary>Metadata recorded, bytes not yet confirmed in storage.</summary>
    Pending,

    /// <summary>Bytes stored and their length and SHA-256 verified.</summary>
    Available,

    /// <summary>Bytes removed (or never confirmed); metadata kept for audit.</summary>
    Deleted
}

public enum FileClassification
{
    Internal,
    Confidential
}

/// <summary>
/// Retention and size policy of the Files module (FIL-005, NFR retention table). Version 1 is the only version.
/// </summary>
public sealed record FileRetentionPolicy(int Version, TimeSpan PendingUploadLifetime, TimeSpan ReportOutputRetention, long MaxReportOutputBytes)
{
    public static FileRetentionPolicy V1 { get; } = new(1, TimeSpan.FromHours(24), TimeSpan.FromDays(7), 50L * 1024 * 1024);
}

/// <summary>
/// Metadata of a file whose bytes live only in blob storage behind the Application storage port (FIL-001). The
/// Domain never sees bytes or storage keys: the storage adapter addresses the bytes by <see cref="VersionedAggregateRoot.Id"/>.
/// The name is sanitized, the SHA-256 is computed by the server while the bytes are written (FIL-002).
/// </summary>
public sealed class StoredFile : VersionedAggregateRoot
{
    public const int OwnerTypeMaxLength = 60;
    public const int NameMaxLength = 255;
    public const int MediaTypeMaxLength = 100;
    public const int Sha256Length = 64;
    public const int DeletionReasonMaxLength = 40;

    private const string ReservedCharacters = "<>:\"|?*/\\";

    private StoredFile()
    {
        OwnerType = string.Empty;
        OriginalName = string.Empty;
        MediaType = string.Empty;
    }

    public string OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public string OriginalName { get; private set; }
    public string MediaType { get; private set; }
    public long? Length { get; private set; }
    public string? Sha256 { get; private set; }
    public FileClassification Classification { get; private set; }
    public StoredFileStatus Status { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AvailableAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public string? DeletionReason { get; private set; }

    public static (StoredFile? File, DomainError? Error) CreatePending(
        Guid id, string? ownerType, Guid ownerId, string? originalName, string? mediaType, FileClassification classification,
        Guid? createdBy, DateTime nowUtc, DateTime? expiresAtUtc)
    {
        if (id == Guid.Empty || ownerId == Guid.Empty)
            throw new ArgumentException("File and owner identifiers are required.");
        EnsureUtc(nowUtc);
        if (string.IsNullOrWhiteSpace(ownerType) || ownerType.Length > OwnerTypeMaxLength)
            return (null, FileErrors.Validation("ownerType", $"The owner type must have 1 to {OwnerTypeMaxLength} characters."));
        var name = SanitizeName(originalName);
        if (name is null)
            return (null, FileErrors.Validation("fileName", $"The file name must have 1 to {NameMaxLength} usable characters."));
        if (string.IsNullOrWhiteSpace(mediaType) || mediaType.Length > MediaTypeMaxLength)
            return (null, FileErrors.Validation("mediaType", $"The media type must have 1 to {MediaTypeMaxLength} characters."));
        if (expiresAtUtc is { } expires && (expires.Kind != DateTimeKind.Utc || expires <= nowUtc))
            return (null, FileErrors.Validation("expiresAt", "The expiry must be a future UTC instant."));

        return (new StoredFile
        {
            Id = id,
            OwnerType = ownerType,
            OwnerId = ownerId,
            OriginalName = name,
            MediaType = mediaType,
            Classification = classification,
            Status = StoredFileStatus.Pending,
            CreatedBy = createdBy == Guid.Empty ? null : createdBy,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc
        }, null);
    }

    /// <summary>
    /// Confirms the stored bytes with the length and lowercase hexadecimal SHA-256 measured by the server. Confirming
    /// the same digest again is a no-op; a different digest or a deleted file is rejected.
    /// </summary>
    public DomainError? MarkAvailable(long length, string? sha256, long maxBytes, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (Status == StoredFileStatus.Available)
            return Length == length && Sha256 == sha256 ? null : FileErrors.DigestMismatch;
        if (Status != StoredFileStatus.Pending)
            return DomainError.InvalidTransition(Status.ToString(), "Only a pending file can become available.", "FIL-001");
        if (length <= 0)
            return FileErrors.Validation("length", "A stored file cannot be empty.");
        if (length > maxBytes)
            return FileErrors.TooLarge(maxBytes);
        if (!IsSha256(sha256))
            return FileErrors.Validation("sha256", "The digest must be 64 lowercase hexadecimal characters.");

        Length = length;
        Sha256 = sha256;
        Status = StoredFileStatus.Available;
        AvailableAtUtc = nowUtc;
        IncrementVersion();
        return null;
    }

    /// <summary>Marks the bytes as removed; repeating it is a no-op (FIL-005).</summary>
    public void MarkDeleted(string reason, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (Status == StoredFileStatus.Deleted)
            return;
        Status = StoredFileStatus.Deleted;
        DeletedAtUtc = nowUtc;
        DeletionReason = reason.Length <= DeletionReasonMaxLength ? reason : reason[..DeletionReasonMaxLength];
        IncrementVersion();
    }

    public bool IsDownloadable(DateTime nowUtc) =>
        Status == StoredFileStatus.Available && (ExpiresAtUtc is null || ExpiresAtUtc > nowUtc);

    /// <summary>
    /// FIL-005 selection: a pending upload older than the pending lifetime (an orphan of an interrupted write) or an
    /// available file whose expiry passed.
    /// </summary>
    public bool IsCleanupCandidate(FileRetentionPolicy policy, DateTime nowUtc) => Status switch
    {
        StoredFileStatus.Pending => CreatedAtUtc <= nowUtc - policy.PendingUploadLifetime,
        StoredFileStatus.Available => ExpiresAtUtc is { } expires && expires <= nowUtc,
        _ => false
    };

    /// <summary>
    /// Display name: the last path segment, control, reserved and path characters replaced by <c>_</c>, trimmed, at
    /// most 255 characters; <c>null</c> when nothing usable remains (FIL-002).
    /// </summary>
    public static string? SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var name = value.Replace('\\', '/').Split('/').Last();
        var cleaned = new string(name.Select(character =>
                char.IsControl(character) || char.IsSurrogate(character) || ReservedCharacters.Contains(character) ? '_' : character).ToArray())
            .Trim().Trim('.').Trim();
        if (cleaned.Length == 0 || cleaned.All(character => character == '_'))
            return null;
        return cleaned.Length > NameMaxLength ? cleaned[..NameMaxLength] : cleaned;
    }

    public static bool IsSha256(string? value) =>
        value is { Length: Sha256Length } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Instants must be UTC.", nameof(value));
    }
}

/// <summary>Stable rejections of the Files module (FIL-*).</summary>
public static class FileErrors
{
    public const string TooLargeCode = "file.too-large";
    public const string DigestMismatchCode = "file.digest-mismatch";

    public static readonly DomainError DigestMismatch = new(
        DigestMismatchCode, DomainErrorKind.Conflict, "The file is already available with a different digest.", "FIL-002");

    public static DomainError TooLarge(long maxBytes) => new(
        TooLargeCode, DomainErrorKind.TooLarge, $"A stored file has at most {maxBytes} bytes.", "FIL-002", "length");

    public static DomainError Validation(string field, string message) =>
        new("validation", DomainErrorKind.Validation, message, "FIL-002", field);
}
