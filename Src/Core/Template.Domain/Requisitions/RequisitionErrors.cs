using System.Collections.Generic;
using Template.Domain.Common;

namespace Template.Domain.Requisitions;

/// <summary>Stable rejections of the Requisitions module (REQ-*) and of attachment metadata (FIL-002/004).</summary>
public static class RequisitionErrors
{
    public const string NotSubmittableCode = "requisition.not-submittable";
    public const string ImmutableCode = "requisition.immutable";
    public const string FileTooLargeCode = "file.too-large";
    public const string FileTypeNotAllowedCode = "file.type-not-allowed";
    public const string FileLimitReachedCode = "file.limit-reached";
    public const string FileImmutableCode = "file.immutable";

    public static readonly DomainError Immutable = new(
        ImmutableCode, DomainErrorKind.Conflict, "Only a Draft requisition can change; submitted content is immutable.", "REQ-006");

    public static readonly DomainError NeverDiscardable = new(
        ImmutableCode, DomainErrorKind.Conflict, "Only a never-submitted draft can be discarded; submitted history is kept.", "REQ-010");

    public static readonly DomainError FileTooLarge = new(
        FileTooLargeCode, DomainErrorKind.TooLarge, $"An attachment has at most {FilePolicy.MaxBytes} bytes.", "FIL-002", "length");

    public static readonly DomainError FileTypeNotAllowed = new(
        FileTypeNotAllowedCode, DomainErrorKind.UnsupportedMediaType, "The media type is not allowed by the file policy.", "FIL-002", "mediaType");

    public static readonly DomainError FileLimitReached = new(
        FileLimitReachedCode, DomainErrorKind.BusinessRule, $"A requisition has at most {FilePolicy.MaxAttachmentsPerOwner} attachments.", "FIL-002");

    public static readonly DomainError FileImmutable = new(
        FileImmutableCode, DomainErrorKind.Conflict, "Attachments of a submitted revision are append-only.", "FIL-004");

    /// <summary>REQ-004: every failed guard is listed in the <c>fields</c> detail (comma-separated field paths).</summary>
    public static DomainError NotSubmittable(IEnumerable<string> fields) => new(
        NotSubmittableCode, DomainErrorKind.BusinessRule, "The requisition does not meet the submission requirements.", "REQ-004",
        Details: new Dictionary<string, string> { ["fields"] = string.Join(',', fields) });

    public static DomainError Validation(string field, string message, string? ruleId = "REQ-002") =>
        new("validation", DomainErrorKind.Validation, message, ruleId, field);

    public static DomainError NotFound(string what) =>
        new("not-found", DomainErrorKind.NotFound, $"The {what} does not belong to this requisition.");
}
