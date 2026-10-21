using System;
using System.Collections.Generic;
using System.Linq;

namespace Template.Domain.Requisitions;

/// <summary>
/// <c>RequisitionPolicy</c> version 1 (docs/product/business-rules.md, B23-10): at most 100 lines, needed-by on or after
/// the submission local date, expiry grace 30 days after needed-by. The version is pinned on each submission; the
/// versioned policy store arrives with the policy administration of Phase 31, so version 1 is the only one.
/// </summary>
public sealed record RequisitionPolicy(int Version, int MaxLines, int ExpiryGraceDays)
{
    public const string PolicyType = "RequisitionPolicy";

    public static RequisitionPolicy V1 { get; } = new(1, 100, 30);
}

/// <summary>
/// <c>FilePolicy</c> version 1 applied to attachment metadata (FIL-002). Phase 27 stores metadata only, in
/// <c>Pending</c> status; bytes, hashing and content sniffing arrive with the Files module in Phase 30.
/// </summary>
public static class FilePolicy
{
    public const long MaxBytes = 10L * 1024 * 1024;
    public const int MaxAttachmentsPerOwner = 10;
    public const int FileNameMaxLength = 255;
    public const int MediaTypeMaxLength = 100;

    /// <summary>Characters rejected by common file systems; the same set on every platform.</summary>
    private const string ReservedCharacters = "<>:\"|?*";

    public static IReadOnlySet<string> AllowedMediaTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "text/plain",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    /// <summary>
    /// Sanitized display name: the last path segment, control and reserved characters replaced by <c>_</c>, trimmed,
    /// 1–255 characters; <c>null</c> when nothing usable remains.
    /// </summary>
    public static string? SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var name = value.Replace('\\', '/').Split('/').Last();
        var cleaned = new string(name.Select(character => char.IsControl(character) || ReservedCharacters.Contains(character) ? '_' : character).ToArray())
            .Trim().Trim('.');
        return cleaned.Length is 0 or > FileNameMaxLength ? null : cleaned;
    }
}
