using System;
using System.Collections.Generic;
using Template.Domain.Common;

namespace Template.Domain.Reporting;

/// <summary>Report catalog (RPT-009). <c>PurchaseOrderPdf</c> (Phase 32) is requested by the issue use case only (D32-4).</summary>
public enum ReportType
{
    RequisitionPdf,
    QuoteComparisonPdf,
    BudgetConsumptionXlsx,
    SupplierQuotesXlsx,
    PurchaseOrderPdf
}

public enum ReportFormat
{
    Pdf,
    Xlsx
}

/// <summary>Report job lifecycle (docs/product/workflows.md §6).</summary>
public enum ReportJobStatus
{
    Pending,
    Running,
    Ready,
    Failed,
    Expired,
    Cancelled
}

/// <summary>
/// <c>ReportPolicy</c> version 1: at most 3 attempts (RPT-005), outputs downloadable for 7 days (RPT-006, FIL-005),
/// a renewable 5-minute claim lease, exponential retry from 30 seconds and at most 10,000 data rows per report
/// (NFR-PERF-6 bound).
/// </summary>
public sealed record ReportPolicy(int Version, int MaxAttempts, TimeSpan OutputRetention, TimeSpan Lease, TimeSpan FirstRetryDelay, int MaxRows)
{
    public static ReportPolicy V1 { get; } = new(1, 3, TimeSpan.FromDays(7), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), 10_000);

    /// <summary>Delay before attempt <paramref name="completedAttempts"/> + 1: 30 s, 60 s, 120 s, ...</summary>
    public TimeSpan RetryDelay(int completedAttempts) =>
        TimeSpan.FromTicks(FirstRetryDelay.Ticks * (1L << Math.Clamp(completedAttempts - 1, 0, 10)));
}

/// <summary>Static facts of each catalog entry: output format, media type, extension and the versioned template (RPT-008).</summary>
public static class ReportCatalog
{
    public const string PdfMediaType = "application/pdf";
    public const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static ReportFormat Format(ReportType type) => type switch
    {
        ReportType.RequisitionPdf or ReportType.QuoteComparisonPdf or ReportType.PurchaseOrderPdf => ReportFormat.Pdf,
        _ => ReportFormat.Xlsx
    };

    public static string MediaType(ReportType type) => Format(type) == ReportFormat.Pdf ? PdfMediaType : XlsxMediaType;

    public static string Extension(ReportType type) => Format(type) == ReportFormat.Pdf ? ".pdf" : ".xlsx";

    /// <summary>Server template version pinned on the job; API users never supply templates (RPT-008).</summary>
    public static string TemplateVersion(ReportType type) => type switch
    {
        ReportType.RequisitionPdf => "requisition-pdf.v1",
        ReportType.QuoteComparisonPdf => "quote-comparison-pdf.v1",
        ReportType.BudgetConsumptionXlsx => "budget-consumption-xlsx.v1",
        ReportType.PurchaseOrderPdf => "purchase-order-pdf.v1",
        _ => "supplier-quotes-xlsx.v1"
    };

    public static bool IsKnown(ReportType type) => Enum.IsDefined(type);

    /// <summary>
    /// D32-4: the issued purchase-order document is a business record produced by the issue use case, not a user
    /// request; the report-jobs API neither lists nor accepts it.
    /// </summary>
    public static bool IsSystemOnly(ReportType type) => type == ReportType.PurchaseOrderPdf;

    /// <summary>
    /// D32-4: the stored output of a business-record document never expires (NFR retention: five years after closure,
    /// owner-approved purge only); the job itself still expires after the report retention.
    /// </summary>
    public static bool KeepsOutput(ReportType type) => type == ReportType.PurchaseOrderPdf;
}

/// <summary>Safe failure codes exposed on a failed job (RPT-005); never exception text.</summary>
public static class ReportFailureCodes
{
    public const string SourceNotFound = "report.source-not-found";
    public const string SourceChanged = "report.source-changed";
    public const string TooLarge = "report.too-large";
    public const string RendererUnavailable = "report.renderer-unavailable";
    public const string StorageUnavailable = "report.storage-unavailable";
    public const string Timeout = "report.timeout";
    public const string AttemptsExhausted = "report.attempts-exhausted";
    public const string OutputInvalid = "report.output-invalid";
    public const string Unexpected = "report.unexpected";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        SourceNotFound, SourceChanged, TooLarge, RendererUnavailable, StorageUnavailable, Timeout, AttemptsExhausted, OutputInvalid, Unexpected
    };
}

/// <summary>
/// Asynchronous report request (RPT-001..009). The job keeps the validated parameter snapshot, the <c>AsOfUtc</c>
/// instant and the organization time zone, so every attempt renders the same history (RPT-003). The Worker claims it
/// with a renewable lease; a lost lease never completes the job and a job has at most one output (RPT-005).
/// </summary>
public sealed class ReportJob : VersionedAggregateRoot
{
    public const int ParametersMaxLength = 4000;
    public const int ParametersHashLength = 64;
    public const int TimeZoneMaxLength = 64;
    public const int TemplateVersionMaxLength = 40;
    public const int ClaimOwnerMaxLength = 128;
    public const int StageMaxLength = 40;
    public const int FailureCodeMaxLength = 60;
    public const int OutputNameMaxLength = 120;

    private readonly DomainEventBuffer events = new();

    private ReportJob()
    {
        ParametersJson = "{}";
        ParametersHash = string.Empty;
        TimeZoneId = string.Empty;
        TemplateVersion = string.Empty;
    }

    public ReportType ReportType { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string ParametersJson { get; private set; }
    public string ParametersHash { get; private set; }
    public DateTime AsOfUtc { get; private set; }
    public string TimeZoneId { get; private set; }
    public int PolicyVersion { get; private set; }
    public string TemplateVersion { get; private set; }
    public ReportJobStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public string? ClaimOwner { get; private set; }
    public DateTime? ClaimExpiresAtUtc { get; private set; }
    public int ProgressPercent { get; private set; }
    public string? ProgressStage { get; private set; }
    public Guid? OutputFileId { get; private set; }
    public string? OutputFileName { get; private set; }
    public long? OutputLength { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<DomainEvent> DomainEvents => events.Events;

    public ReportFormat Format => ReportCatalog.Format(ReportType);

    /// <summary>Pending or Running: the state an identical request joins instead of creating a duplicate (RPT-001).</summary>
    public bool IsActive => Status is ReportJobStatus.Pending or ReportJobStatus.Running;

    public void ClearDomainEvents() => events.Clear();

    public static (ReportJob? Job, DomainError? Error) Request(
        Guid id, ReportType type, Guid requestedBy, string? parametersJson, string? parametersHash, DateTime asOfUtc, string? timeZoneId,
        ReportPolicy policy, DateTime nowUtc)
    {
        if (id == Guid.Empty || requestedBy == Guid.Empty)
            throw new ArgumentException("Job and requester identifiers are required.");
        ArgumentNullException.ThrowIfNull(policy);
        EnsureUtc(nowUtc);
        EnsureUtc(asOfUtc);
        if (!ReportCatalog.IsKnown(type))
            return (null, ReportErrors.TypeUnknown);
        if (string.IsNullOrWhiteSpace(parametersJson) || parametersJson.Length > ParametersMaxLength)
            return (null, ReportErrors.Validation("parameters", $"The parameter snapshot must have 1 to {ParametersMaxLength} characters."));
        if (parametersHash is not { Length: ParametersHashLength })
            return (null, ReportErrors.Validation("parameters", "The parameter fingerprint is invalid."));
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > TimeZoneMaxLength)
            return (null, ReportErrors.Validation("timeZone", "The organization time zone is required."));
        if (asOfUtc > nowUtc)
            return (null, ReportErrors.Validation("asOf", "The as-of instant cannot be in the future."));

        var job = new ReportJob
        {
            Id = id,
            ReportType = type,
            RequestedBy = requestedBy,
            ParametersJson = parametersJson,
            ParametersHash = parametersHash,
            AsOfUtc = asOfUtc,
            TimeZoneId = timeZoneId,
            PolicyVersion = policy.Version,
            TemplateVersion = ReportCatalog.TemplateVersion(type),
            Status = ReportJobStatus.Pending,
            NextAttemptAtUtc = nowUtc,
            RequestedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        job.events.Raise(new ReportRequested(nowUtc, id, type, requestedBy));
        return (job, null);
    }

    /// <summary>
    /// Durable claim (workflows §6 Pending → Running): a due Pending job, or a Running job whose lease expired (the
    /// previous Worker died). A stale job that already used every attempt fails with
    /// <see cref="ReportFailureCodes.AttemptsExhausted"/> instead; the result is then <c>false</c>.
    /// </summary>
    public (bool Claimed, DomainError? Error) Claim(string owner, ReportPolicy policy, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        EnsureUtc(nowUtc);
        if (!IsClaimable(nowUtc))
            return (false, DomainError.InvalidTransition(Status.ToString(), "The report job is not due for processing.", "RPT-005"));
        if (AttemptCount >= policy.MaxAttempts)
        {
            Finish(ReportFailureCodes.AttemptsExhausted, nowUtc);
            return (false, null);
        }

        AttemptCount++;
        Status = ReportJobStatus.Running;
        ClaimOwner = owner.Length <= ClaimOwnerMaxLength ? owner : owner[..ClaimOwnerMaxLength];
        ClaimExpiresAtUtc = nowUtc + policy.Lease;
        StartedAtUtc ??= nowUtc;
        ProgressPercent = 0;
        ProgressStage = "claimed";
        Touch(nowUtc);
        return (true, null);
    }

    public bool IsClaimable(DateTime nowUtc) =>
        (Status == ReportJobStatus.Pending && NextAttemptAtUtc <= nowUtc)
        || (Status == ReportJobStatus.Running && ClaimExpiresAtUtc <= nowUtc);

    /// <summary>Records progress and renews the lease of the current claimant.</summary>
    public DomainError? ReportProgress(string owner, int percent, string stage, ReportPolicy policy, DateTime nowUtc)
    {
        if (OwnershipError(owner) is { } error)
            return error;
        ProgressPercent = Math.Clamp(percent, 0, 99);
        ProgressStage = stage.Length <= StageMaxLength ? stage : stage[..StageMaxLength];
        ClaimExpiresAtUtc = nowUtc + policy.Lease;
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// Running → Ready with the single verified output (RPT-005). A second completion with the same file is a no-op;
    /// a claimant that lost its lease is rejected, so its file stays an orphan for FIL-005 cleanup.
    /// </summary>
    public DomainError? Complete(string owner, Guid outputFileId, string outputFileName, long length, ReportPolicy policy, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (Status == ReportJobStatus.Ready && OutputFileId == outputFileId)
            return null;
        if (OwnershipError(owner) is { } error)
            return error;
        if (outputFileId == Guid.Empty || length <= 0 || string.IsNullOrWhiteSpace(outputFileName))
            throw new ArgumentException("A completed report needs its stored output.");

        Status = ReportJobStatus.Ready;
        OutputFileId = outputFileId;
        OutputFileName = outputFileName.Length <= OutputNameMaxLength ? outputFileName : outputFileName[..OutputNameMaxLength];
        OutputLength = length;
        ProgressPercent = 100;
        ProgressStage = "ready";
        FailureCode = null;
        CompletedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc + policy.OutputRetention;
        ClaimOwner = null;
        ClaimExpiresAtUtc = null;
        Touch(nowUtc);
        events.Raise(new ReportReady(nowUtc, Id, ReportType, RequestedBy, outputFileId));
        return null;
    }

    /// <summary>
    /// Ends the current attempt with a safe failure code. A transient failure with attempts left returns the job to
    /// <c>Pending</c> with exponential back-off; otherwise the job is <c>Failed</c> (RPT-005). Returns <c>true</c> when final.
    /// </summary>
    public (bool Final, DomainError? Error) FailAttempt(string owner, string failureCode, bool transient, ReportPolicy policy, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (!ReportFailureCodes.All.Contains(failureCode))
            throw new ArgumentException("Only catalogued failure codes may be exposed.", nameof(failureCode));
        if (OwnershipError(owner) is { } error)
            return (false, error);

        if (transient && AttemptCount < policy.MaxAttempts)
        {
            Status = ReportJobStatus.Pending;
            FailureCode = failureCode;
            NextAttemptAtUtc = nowUtc + policy.RetryDelay(AttemptCount);
            ClaimOwner = null;
            ClaimExpiresAtUtc = null;
            ProgressStage = "retry-scheduled";
            Touch(nowUtc);
            return (false, null);
        }

        Finish(transient ? ReportFailureCodes.AttemptsExhausted : failureCode, nowUtc);
        return (true, null);
    }

    /// <summary>
    /// Graceful Worker shutdown: the claimant hands the job back as due now without consuming the attempt, so a restart
    /// resumes it at once. A crashed Worker never calls this; its lease expires and the claim counts as an attempt.
    /// </summary>
    public DomainError? ReleaseClaim(string owner, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (OwnershipError(owner) is { } error)
            return error;
        Status = ReportJobStatus.Pending;
        AttemptCount = Math.Max(0, AttemptCount - 1);
        NextAttemptAtUtc = nowUtc;
        ClaimOwner = null;
        ClaimExpiresAtUtc = null;
        ProgressPercent = 0;
        ProgressStage = "released";
        Touch(nowUtc);
        return null;
    }

    /// <summary>Pending → Cancelled by the requester; repeating it returns the current state (workflows §6).</summary>
    public DomainError? Cancel(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (Status == ReportJobStatus.Cancelled)
            return null;
        if (Status != ReportJobStatus.Pending)
            return DomainError.InvalidTransition(Status.ToString(), "Only a pending report job can be cancelled.", "RPT-001");
        Status = ReportJobStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CompletedAtUtc = nowUtc;
        ProgressStage = "cancelled";
        Touch(nowUtc);
        return null;
    }

    /// <summary>Ready → Expired once the retention elapsed (RPT-006); returns <c>true</c> when the state changed.</summary>
    public bool Expire(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (Status != ReportJobStatus.Ready || ExpiresAtUtc > nowUtc)
            return false;
        Status = ReportJobStatus.Expired;
        ProgressStage = "expired";
        Touch(nowUtc);
        return true;
    }

    public bool IsDownloadable(DateTime nowUtc) => Status == ReportJobStatus.Ready && ExpiresAtUtc > nowUtc && OutputFileId is not null;

    /// <summary>Ready but past its retention: downloads answer 410 even before the cleanup marks it Expired.</summary>
    public bool IsPastRetention(DateTime nowUtc) =>
        Status == ReportJobStatus.Expired || (Status == ReportJobStatus.Ready && ExpiresAtUtc <= nowUtc);

    private void Finish(string failureCode, DateTime nowUtc)
    {
        Status = ReportJobStatus.Failed;
        FailureCode = failureCode;
        CompletedAtUtc = nowUtc;
        ClaimOwner = null;
        ClaimExpiresAtUtc = null;
        ProgressStage = "failed";
        Touch(nowUtc);
        events.Raise(new ReportFailed(nowUtc, Id, ReportType, RequestedBy, failureCode));
    }

    private DomainError? OwnershipError(string owner) =>
        Status == ReportJobStatus.Running && string.Equals(ClaimOwner, owner, StringComparison.Ordinal)
            ? null
            : ReportErrors.LeaseLost(Status.ToString());

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Instants must be UTC.", nameof(value));
    }
}

/// <summary>Stable rejections of the Reporting module (RPT-*).</summary>
public static class ReportErrors
{
    public const string TypeUnknownCode = "report.type-unknown";
    public const string LeaseLostCode = "report.lease-lost";

    public static readonly DomainError TypeUnknown = new(
        TypeUnknownCode, DomainErrorKind.Validation, "The report type is not in the report catalog.", "RPT-009", "reportType");

    public static DomainError LeaseLost(string currentState) => new(
        LeaseLostCode, DomainErrorKind.Conflict, "The report job is not claimed by this worker.", "RPT-005",
        Details: new Dictionary<string, string> { ["currentState"] = currentState });

    public static DomainError Validation(string field, string message) =>
        new("validation", DomainErrorKind.Validation, message, "RPT-001", field);
}

public sealed record ReportRequested(DateTime OccurredAtUtc, Guid ReportJobId, ReportType ReportType, Guid RequestedBy) : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.report.requested";
}

public sealed record ReportReady(DateTime OccurredAtUtc, Guid ReportJobId, ReportType ReportType, Guid RequestedBy, Guid OutputFileId)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.report.ready";
}

public sealed record ReportFailed(DateTime OccurredAtUtc, Guid ReportJobId, ReportType ReportType, Guid RequestedBy, string FailureCode)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.report.failed";
}
