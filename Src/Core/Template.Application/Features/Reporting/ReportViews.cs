#nullable enable
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Wrappers;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Domain.Reporting;

namespace Template.Application.Features.Reporting;

public sealed record ReportParametersView(
    Guid? RequisitionId, Guid? RfqId, int? ComparisonSequence, Guid? FiscalPeriodId, Guid? CostCenterId, string? Currency,
    DateOnly? From, DateOnly? To, Guid? SupplierId, Guid? PurchaseOrderId = null)
{
    public static ReportParametersView Of(ReportParameters parameters) => new(
        parameters.RequisitionId, parameters.RfqId, parameters.ComparisonSequence, parameters.FiscalPeriodId, parameters.CostCenterId,
        parameters.Currency, parameters.From, parameters.To, parameters.SupplierId, parameters.PurchaseOrderId);
}

/// <summary>
/// Report job representation (api-guidelines §8). <see cref="ContentUrl"/> is an API path set only while the output is
/// downloadable, never a storage URL; <see cref="FailureCode"/> is a catalogued safe code (RPT-005).
/// </summary>
public sealed record ReportJobView(
    Guid Id,
    string ReportType,
    string Format,
    string Status,
    ReportParametersView Parameters,
    Guid RequestedBy,
    DateTime RequestedAt,
    DateTime AsOf,
    string TimeZone,
    int PolicyVersion,
    string TemplateVersion,
    int Attempts,
    int MaxAttempts,
    int Progress,
    string? Stage,
    string? FailureCode,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? ExpiresAt,
    string? FileName,
    string? MediaType,
    long? Length,
    string? ContentUrl,
    IReadOnlyList<string> AllowedActions,
    string Version)
{
    public const string ActionCancel = "cancel";
    public const string ActionDownload = "download";
    public const string ActionRetry = "retry";

    public static ReportJobView From(ReportJob job, DateTime nowUtc, bool mayAct)
    {
        var downloadable = job.IsDownloadable(nowUtc);
        // A Ready job past its retention is Expired on the wire before the retention pass marks it (RPT-006).
        var status = job.Status == ReportJobStatus.Ready && !downloadable ? ReportJobStatus.Expired : job.Status;
        var actions = new List<string>();
        if (mayAct && status == ReportJobStatus.Pending)
            actions.Add(ActionCancel);
        if (downloadable)
            actions.Add(ActionDownload);
        if (mayAct && status is ReportJobStatus.Failed or ReportJobStatus.Expired or ReportJobStatus.Cancelled)
            actions.Add(ActionRetry);
        return new ReportJobView(
            job.Id, job.ReportType.ToString(), job.Format.ToString(), status.ToString(),
            ReportParametersView.Of(ReportParameters.Parse(job.ParametersJson)), job.RequestedBy, job.RequestedAtUtc, job.AsOfUtc, job.TimeZoneId,
            job.PolicyVersion, job.TemplateVersion, job.AttemptCount, ReportPolicy.V1.MaxAttempts, job.ProgressPercent, job.ProgressStage, job.FailureCode,
            job.StartedAtUtc, job.CompletedAtUtc, job.ExpiresAtUtc, job.OutputFileName,
            job.OutputFileId is null ? null : ReportCatalog.MediaType(job.ReportType), job.OutputLength,
            downloadable ? $"/api/v2/report-jobs/{job.Id}/content" : null, actions, VersionPrecondition.Format(job.Version));
    }
}

/// <summary>Catalog entry for the actor: format, required permissions, parameters and whether the actor may request it.</summary>
public sealed record ReportTypeView(string Type, string Format, string MediaType, IReadOnlyList<string> RequiredPermissions, IReadOnlyList<string> Parameters, bool Available);

/// <summary>Streamed report output. The caller disposes <see cref="Content"/>.</summary>
public sealed record ReportDownload(Stream Content, string FileName, string MediaType, long Length);

/// <summary>Result of a report request: the job and whether an identical active job was returned (RPT-001).</summary>
public sealed record ReportRequestResult(ReportJobView Job, bool Reused);

/// <summary>
/// Report authorization (RPT-002, RPT-006): <c>reports.request</c> plus the read permission of the underlying data;
/// a job is visible to its requester or to <c>reports.read-all</c> holders. Unreadable jobs answer 404.
/// </summary>
public sealed class ReportAccess(ProcurementWriter writer)
{
    public static IReadOnlyList<string> DataPermissions(ReportType type) => type switch
    {
        ReportType.RequisitionPdf => [Permissions.RequisitionsCreate, Permissions.RequisitionsReadTeam, Permissions.RequisitionsReadAll],
        ReportType.QuoteComparisonPdf => [Permissions.SourcingQuotesRead],
        ReportType.BudgetConsumptionXlsx => [Permissions.BudgetRead],
        ReportType.PurchaseOrderPdf => [Permissions.PurchasingOrdersRead],
        _ => [Permissions.SourcingQuotesRead, Permissions.SuppliersRead]
    };

    /// <summary><c>true</c> when any listed permission suffices (requisitions), otherwise every listed permission is required.</summary>
    public static bool AnyOfDataPermissions(ReportType type) => type == ReportType.RequisitionPdf;

    public static IReadOnlyList<string> ParameterNames(ReportType type) => type switch
    {
        ReportType.RequisitionPdf => ["requisitionId"],
        ReportType.QuoteComparisonPdf => ["rfqId"],
        ReportType.BudgetConsumptionXlsx => ["fiscalPeriodId", "currency?", "costCenterId?"],
        ReportType.PurchaseOrderPdf => ["purchaseOrderId"],
        _ => ["from", "to", "supplierId?"]
    };

    public Task<bool> MayRequestAsync(CancellationToken cancellationToken) =>
        writer.HasPermissionAsync(Permissions.ReportsRequest, cancellationToken);

    public async Task<bool> HasDataPermissionAsync(ReportType type, CancellationToken cancellationToken)
    {
        var permissions = await writer.PermissionsAsync(cancellationToken);
        var required = DataPermissions(type);
        return AnyOfDataPermissions(type) ? required.Any(permissions.Contains) : required.All(permissions.Contains);
    }

    public Task<bool> MayReadAllAsync(CancellationToken cancellationToken) =>
        writer.HasPermissionAsync(Permissions.ReportsReadAll, cancellationToken);

    /// <summary>RPT-006: the requester (still holding <c>reports.request</c> and the data permission) or a <c>reports.read-all</c> holder.</summary>
    public async Task<bool> MayReadAsync(ReportJob job, CancellationToken cancellationToken)
    {
        if (await MayReadAllAsync(cancellationToken))
            return true;
        return writer.ActorId == job.RequestedBy && await MayRequestAsync(cancellationToken) && await HasDataPermissionAsync(job.ReportType, cancellationToken);
    }

    /// <summary>Cancel and retry belong to the requester only.</summary>
    public bool IsRequester(ReportJob job) => writer.ActorId == job.RequestedBy;

    public static ApplicationError JobNotFound() => new(ProblemCodes.NotFound, ErrorCategory.NotFound, "The report job was not found.");
}
