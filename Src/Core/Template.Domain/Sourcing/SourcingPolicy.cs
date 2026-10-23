using System;
using System.Collections.Generic;
using Template.Domain.Common;

namespace Template.Domain.Sourcing;

/// <summary>Request-for-quotation lifecycle (docs/product/workflows.md §2).</summary>
public enum RfqStatus
{
    Draft,
    Published,
    Closed,
    AwardPending,
    Awarded,
    Cancelled
}

/// <summary>Competitive or direct sourcing (B23-3); a direct RFQ has one invitation and a waiver (SRC-006).</summary>
public enum SourcingMethod
{
    Competitive,
    Direct
}

/// <summary>How the buyer received the quote (D23-3).</summary>
public enum QuoteSource
{
    Email,
    Phone,
    Document,
    Other
}

/// <summary>Quote revision states (SRC-009): <c>Current → Superseded</c> (revise) and <c>Current → Withdrawn</c>.</summary>
public enum QuoteRevisionStatus
{
    Current,
    Superseded,
    Withdrawn
}

/// <summary>A comparison is <c>Incomplete</c> while a needed exchange rate is missing (SRC-013, CALC-004).</summary>
public enum ComparisonStatus
{
    Complete,
    Incomplete
}

/// <summary>Exchange-rate provenance (domain-model.md §2 <c>ExchangeRateSnapshot</c>).</summary>
public enum ExchangeRateSource
{
    BcbPtax,
    Manual
}

/// <summary>Why the snapshot was taken: the SRC-005 threshold estimate at creation or the comparison at close.</summary>
public enum ExchangeRatePurpose
{
    Estimate,
    Comparison
}

/// <summary>Sourcing waiver types of the waiver catalog (docs/product/business-rules.md).</summary>
public enum WaiverType
{
    LowValueDirectPurchase,
    DirectPurchase,
    InsufficientQuotations,
    NonLeadingSelection,
    ManualExchangeRate
}

/// <summary>Award lifecycle; Phase 29 creates <c>PendingApproval</c>, the approval module (Phase 31) decides it.</summary>
public enum AwardStatus
{
    PendingApproval,
    Approved,
    Rejected,
    Invalidated,
    Withdrawn,
    Cancelled
}

/// <summary>
/// <c>SourcingPolicy</c> version 1 (docs/product/business-rules.md, B23-10): competition threshold 5,000.00 BRL
/// (estimated total in base currency, exclusive), at least 3 invitations and 3 valid quotes above it (otherwise 1),
/// response window 24 h–30 days, at most +30 days per deadline extension. The RFQ pins the version at creation.
/// </summary>
public sealed record SourcingPolicy(
    int Version,
    decimal CompetitionThreshold,
    int MinInvitationsAboveThreshold,
    int MinInvitationsOtherwise,
    int MinValidQuotesAboveThreshold,
    TimeSpan MinResponseWindow,
    TimeSpan MaxResponseWindow,
    TimeSpan MaxExtension)
{
    public const string PolicyType = "SourcingPolicy";

    public static SourcingPolicy V1 { get; } = new(1, 5_000.00m, 3, 1, 3, TimeSpan.FromHours(24), TimeSpan.FromDays(30), TimeSpan.FromDays(30));

    /// <summary>SRC-005: exclusive threshold; an unknown estimate (no rate) is treated as exceeded (conservative).</summary>
    public bool IsAboveThreshold(decimal? estimateInBaseCurrency) =>
        estimateInBaseCurrency is not { } estimate || estimate > CompetitionThreshold;

    public int RequiredInvitations(bool aboveThreshold) => aboveThreshold ? MinInvitationsAboveThreshold : MinInvitationsOtherwise;

    /// <summary>SRC-017 applies to competitive RFQs above the threshold only; otherwise one valid quote suffices.</summary>
    public int RequiredValidQuotes(SourcingMethod method, bool aboveThreshold) =>
        method == SourcingMethod.Competitive && aboveThreshold ? MinValidQuotesAboveThreshold : 1;
}

/// <summary>
/// A waiver the actor asks for. The use case has already checked the dedicated permission; the aggregate checks that
/// the type is the one the rule needs and that the reason has 10–500 characters (D29-3), then records a
/// <see cref="RfqWaiver"/>. There is no boolean bypass (B23-9).
/// </summary>
public sealed record WaiverRequest(WaiverType Type, string Permission, string? Reason, Guid ActorId);

/// <summary>Stable rejections of the Sourcing module (SRC-*).</summary>
public static class SourcingErrors
{
    public const string RequisitionNotEligibleCode = "rfq.requisition-not-eligible";
    public const string AlreadyActiveCode = "rfq.already-active";
    public const string DeadlineInvalidCode = "rfq.deadline-invalid";
    public const string InsufficientInvitationsCode = "rfq.insufficient-invitations";
    public const string DeadlinePassedCode = "rfq.deadline-passed";
    public const string SupplierAlreadyInvitedCode = "rfq.supplier-already-invited";
    public const string DirectSingleSupplierCode = "rfq.direct-single-supplier";
    public const string NoPendingSuppliersCode = "rfq.no-pending-suppliers";
    public const string RateNotNeededCode = "rfq.rate-not-needed";
    public const string WaiverRequiredCode = "waiver.required";
    public const string WaiverPermissionRequiredCode = "waiver.permission-required";
    public const string QuoteLateCode = "quote.late";
    public const string QuoteSupplierNotInvitedCode = "quote.supplier-not-invited";
    public const string QuoteImmutableCode = "quote.immutable";
    public const string QuoteExistsCode = "quote.exists";
    public const string QuoteExpiredCode = "quote.expired";
    public const string AwardNotEligibleCode = "award.not-eligible";
    public const string AwardPendingExistsCode = "award.pending-exists";
    public const string AwardInsufficientQuotationsCode = "award.insufficient-quotations";
    public const string AwardNonLeadingCode = "award.non-leading-requires-waiver";
    public const string AwardComparisonIncompleteCode = "award.comparison-incomplete";

    /// <summary>Same value as <c>SupplierErrors.NotEligibleCode</c> (SUP-003); Sourcing does not reference Suppliers.</summary>
    public const string SupplierNotEligibleCode = "supplier.not-eligible";

    public static readonly DomainError RequisitionNotEligible = new(
        RequisitionNotEligibleCode, DomainErrorKind.BusinessRule, "Only a Submitted requisition without another active RFQ can be sourced.", "SRC-001");

    public static readonly DomainError AlreadyActive = new(
        AlreadyActiveCode, DomainErrorKind.Conflict, "The requisition already has a non-terminal RFQ.", "SRC-001");

    public static readonly DomainError DeadlinePassed = new(
        DeadlinePassedCode, DomainErrorKind.Conflict, "The RFQ deadline has passed.", "SRC-011");

    public static readonly DomainError SupplierAlreadyInvited = new(
        SupplierAlreadyInvitedCode, DomainErrorKind.Conflict, "The supplier is already invited.", "SRC-007");

    public static readonly DomainError DirectSingleSupplier = new(
        DirectSingleSupplierCode, DomainErrorKind.BusinessRule, "A Direct RFQ has exactly one invited supplier.", "SRC-006");

    public static readonly DomainError NoPendingSuppliers = new(
        NoPendingSuppliersCode, DomainErrorKind.BusinessRule, "Every invited supplier already has a current quote.", "SRC-022");

    public static readonly DomainError QuoteSupplierNotInvited = new(
        QuoteSupplierNotInvitedCode, DomainErrorKind.BusinessRule, "Quotes are entered only for invited suppliers.", "SRC-008");

    public static readonly DomainError QuoteExists = new(
        QuoteExistsCode, DomainErrorKind.Conflict, "The supplier already has a quote; record a revision instead.", "SRC-009");

    public static readonly DomainError QuoteImmutable = new(
        QuoteImmutableCode, DomainErrorKind.Conflict, "Quote revisions are immutable and change only while the RFQ is Published.", "SRC-009");

    public static readonly DomainError QuoteExpired = new(
        QuoteExpiredCode, DomainErrorKind.BusinessRule, "The quote is no longer valid.", "SRC-010");

    public static readonly DomainError AwardNotEligible = new(
        AwardNotEligibleCode, DomainErrorKind.BusinessRule, "Only a valid, ranked quote revision of the comparison can be awarded.", "SRC-016");

    public static readonly DomainError AwardPendingExists = new(
        AwardPendingExistsCode, DomainErrorKind.Conflict, "The RFQ already has an award pending approval.", "SRC-016");

    public static readonly DomainError AwardComparisonIncomplete = new(
        AwardComparisonIncompleteCode, DomainErrorKind.BusinessRule,
        "The comparison is incomplete; record the missing exchange rate before awarding.", "SRC-013");

    public static readonly DomainError SupplierNotEligible = new(
        SupplierNotEligibleCode, DomainErrorKind.BusinessRule, "Only Active suppliers are eligible for new commitments.", "SUP-003");

    public static DomainError DeadlineInvalid(string message, string ruleId = "SRC-004") =>
        new(DeadlineInvalidCode, DomainErrorKind.Validation, message, ruleId, "deadline");

    public static DomainError InsufficientInvitations(int required, int actual) => new(
        InsufficientInvitationsCode, DomainErrorKind.BusinessRule,
        $"A competitive RFQ needs at least {required} invited eligible suppliers.", "SRC-005",
        Details: new Dictionary<string, string> { ["required"] = Text(required), ["actual"] = Text(actual) });

    public static DomainError SupplierNotEligibleFor(Guid supplierId) => SupplierNotEligible with
    {
        Details = new Dictionary<string, string> { ["supplierId"] = supplierId.ToString() }
    };

    public static DomainError WaiverRequired(WaiverType type, string ruleId) => new(
        WaiverRequiredCode, DomainErrorKind.BusinessRule, $"The operation requires a {type} waiver.", ruleId,
        Details: new Dictionary<string, string> { ["waiverType"] = type.ToString() });

    public static DomainError QuoteLate(DateTime deadlineUtc) => new(
        QuoteLateCode, DomainErrorKind.BusinessRule, "Quotes received after the deadline are rejected (B23-6).", "SRC-008",
        Details: new Dictionary<string, string> { ["deadline"] = deadlineUtc.ToString("O", System.Globalization.DateTimeFormatInfo.InvariantInfo) });

    public static DomainError InsufficientQuotations(int required, int actual) => new(
        AwardInsufficientQuotationsCode, DomainErrorKind.BusinessRule,
        $"Awarding with fewer than {required} valid quotes needs an InsufficientQuotations waiver.", "SRC-017",
        Details: new Dictionary<string, string> { ["required"] = Text(required), ["actual"] = Text(actual) });

    public static DomainError NonLeadingRequiresWaiver(Guid leadingQuoteRevisionId) => new(
        AwardNonLeadingCode, DomainErrorKind.BusinessRule,
        "Awarding a quote that is not the best-ranked awardable quote needs a NonLeadingSelection waiver.", "SRC-018",
        Details: new Dictionary<string, string> { ["leadingQuoteRevisionId"] = leadingQuoteRevisionId.ToString() });

    public static DomainError Validation(string field, string message, string? ruleId = "SRC-008") =>
        new("validation", DomainErrorKind.Validation, message, ruleId, field);

    public static DomainError NotFound(string what) =>
        new("not-found", DomainErrorKind.NotFound, $"The {what} does not belong to this RFQ.");

    private static string Text(int value) => value.ToString(System.Globalization.NumberFormatInfo.InvariantInfo);
}
