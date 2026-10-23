#nullable enable
using Template.Application.Common.Paging;
using Template.Application.Reporting;
using Template.Domain.Budgeting;
using Template.Domain.Reporting;
using Template.Domain.Sourcing;

namespace Template.Application.Interfaces.Reporting;

public sealed record ReportJobFilter(
    Guid? RequestedBy = null,
    IReadOnlyCollection<ReportJobStatus>? Statuses = null,
    IReadOnlyCollection<ReportType>? Types = null);

/// <summary>Aggregate repository of <see cref="ReportJob"/>.</summary>
public interface IReportJobRepository
{
    /// <summary>Partial unique index: one active (Pending/Running) job per (requester, type, parameter fingerprint) (RPT-001).</summary>
    public const string ActiveUniqueKey = "IX_ReportingReportJobs_Active";

    public static readonly IReadOnlyList<string> SortFields = ["requestedAt", "status", "reportType", "completedAt"];

    void Add(ReportJob job);

    Task<ReportJob?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The Pending or Running job of the same requester, type and parameter fingerprint, if any.</summary>
    Task<ReportJob?> FindActiveAsync(Guid requestedBy, ReportType type, string parametersHash, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<ReportJob>> ListAsync(ReportJobFilter filter, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>
/// Durable job queue over the report-job table (workflows §6). Inside the unit-of-work transaction it locks one due row
/// with <c>FOR UPDATE SKIP LOCKED</c>, so concurrent Workers never claim the same attempt; the use case applies
/// <see cref="ReportJob.Claim"/> and commits.
/// </summary>
public interface IReportJobQueue
{
    /// <summary>
    /// Tracked oldest due job (Pending and due, or Running with an expired lease) locked until the transaction ends, or
    /// <c>null</c> when nothing is due. Must run inside the unit-of-work transaction.
    /// </summary>
    Task<ReportJob?> LockNextDueAsync(DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>IDs of Ready jobs whose retention elapsed (RPT-006), oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListExpiredReadyAsync(DateTime nowUtc, int max, CancellationToken cancellationToken);
}

/// <summary>Renders a <see cref="ReportDocument"/> with the versioned HTML/CSS template to PDF (D23-5).</summary>
public interface IPdfRenderer
{
    /// <exception cref="ReportRendererUnavailableException">The browser could not start, crashed or timed out.</exception>
    Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken);
}

/// <summary>Writes a <see cref="WorkbookDocument"/> as XLSX (D23-6) with formula-injection neutralization (RPT-007).</summary>
public interface ISpreadsheetWriter
{
    Task WriteAsync(WorkbookDocument workbook, Stream output, CancellationToken cancellationToken);
}

/// <summary>Transient renderer failure (browser start, crash or timeout); the attempt is retried (RPT-005).</summary>
public sealed class ReportRendererUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Read-only sources of the reports that the aggregate repositories do not already provide. Every result is untracked
/// and carries the immutable history needed for as-of reconstruction (RPT-003).
/// </summary>
public interface IReportSourceReader
{
    Task<FiscalPeriod?> FindFiscalPeriodAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Allocations of the period with amount history and every reservation with its monetary history, bounded by
    /// <paramref name="max"/> + 1 so the caller can detect an oversized report.
    /// </summary>
    Task<IReadOnlyList<BudgetAllocation>> ListAllocationsAsync(
        Guid fiscalPeriodId, string? currency, Guid? costCenterId, int max, CancellationToken cancellationToken);

    /// <summary>Current code and name of cost centers (labels only; amounts never depend on them).</summary>
    Task<IReadOnlyDictionary<Guid, (string Code, string Name)>> GetCostCenterLabelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// RFQs with at least one quote revision received in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), with lines,
    /// invitations, quotes, comparisons and awards, optionally limited to one invited supplier; at most <paramref name="max"/> + 1.
    /// </summary>
    Task<IReadOnlyList<RequestForQuotation>> ListRfqsWithQuotesAsync(
        DateTime fromUtc, DateTime toUtc, Guid? supplierId, int max, CancellationToken cancellationToken);
}
