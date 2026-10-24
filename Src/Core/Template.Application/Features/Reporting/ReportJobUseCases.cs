#nullable enable
using FluentValidation;
using MediatR;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Requisitions;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Services.Procurement;
using Template.Application.Wrappers;
using Template.Domain.Common;
using Template.Domain.Files;
using Template.Domain.Reporting;

namespace Template.Application.Features.Reporting;

/// <summary>
/// Requests a report (RPT-001..003, RPT-009). Parameters irrelevant to the type are rejected; source versions are
/// pinned server-side. An identical active request of the same user returns the existing job.
/// </summary>
public sealed record RequestReportCommand(
    string? ReportType, Guid? RequisitionId = null, Guid? RfqId = null, Guid? FiscalPeriodId = null, Guid? CostCenterId = null,
    string? Currency = null, DateOnly? From = null, DateOnly? To = null, Guid? SupplierId = null) : IRequest<Result<ReportRequestResult>>;

public sealed record GetReportJobQuery(Guid Id) : IRequest<Result<ReportJobView>>;

/// <summary><c>scope=mine</c> (default) or <c>all</c> (<c>reports.read-all</c>).</summary>
public sealed record ListReportJobsQuery(
    int? Page = null, int? PageSize = null, string? Sort = null, string? Scope = null, string? Status = null, string? ReportType = null)
    : IRequest<Result<PagedResult<ReportJobView>>>;

public sealed record CancelReportJobCommand(Guid Id, long? ExpectedVersion) : IRequest<Result<ReportJobView>>;

public sealed record DownloadReportQuery(Guid Id) : IRequest<Result<ReportDownload>>;

public sealed record ListReportTypesQuery : IRequest<Result<IReadOnlyList<ReportTypeView>>>;

public sealed class RequestReportCommandValidator : AbstractValidator<RequestReportCommand>
{
    public RequestReportCommandValidator()
    {
        RuleFor(command => command.ReportType).NotEmpty().MaximumLength(60);
        RuleFor(command => command.Currency).Length(3).When(command => command.Currency is not null);
    }
}

public sealed class ReportJobHandlers(
    IReportJobRepository jobs,
    IStoredFileRepository files,
    IFileStorage storage,
    IPurchaseRequisitionRepository requisitions,
    IRequestForQuotationRepository rfqs,
    IReportSourceReader sources,
    RequisitionAccess requisitionAccess,
    ReportAccess access,
    ProcurementWriter writer) :
    IRequestHandler<RequestReportCommand, Result<ReportRequestResult>>,
    IRequestHandler<GetReportJobQuery, Result<ReportJobView>>,
    IRequestHandler<ListReportJobsQuery, Result<PagedResult<ReportJobView>>>,
    IRequestHandler<CancelReportJobCommand, Result<ReportJobView>>,
    IRequestHandler<DownloadReportQuery, Result<ReportDownload>>,
    IRequestHandler<ListReportTypesQuery, Result<IReadOnlyList<ReportTypeView>>>
{
    public const string RequestedAction = "report.requested";
    public const string CancelledAction = "report.cancelled";
    public const string DownloadedAction = "file.downloaded";
    public const string ReportResource = "report-job";
    public const string FileResource = "stored-file";

    private static readonly ApplicationError ActiveRace = new("report.active-race", ErrorCategory.Conflict, "An identical job became active.");

    public async Task<Result<ReportRequestResult>> Handle(RequestReportCommand request, CancellationToken cancellationToken)
    {
        if (!TryParseType(request.ReportType, out var type))
            return TypeUnknown();
        if (!await access.MayRequestAsync(cancellationToken) || writer.ActorId is not { } actor)
            return ProcurementWriter.Forbidden();
        if (!await access.HasDataPermissionAsync(type, cancellationToken))
            return new ApplicationError(ProblemCodes.Forbidden, ErrorCategory.Forbidden,
                "The report also requires read permission on its underlying data.", "RPT-002");

        var pinned = await PinAsync(type, request, cancellationToken);
        if (pinned.Error is not null)
            return pinned.Error;

        var settings = await writer.SettingsAsync(cancellationToken);
        var json = pinned.Parameters!.ToCanonicalJson();
        var hash = ReportParameters.Fingerprint(type, json);
        if (await jobs.FindActiveAsync(actor, type, hash, cancellationToken) is { } existing)
            return new ReportRequestResult(ReportJobView.From(existing, writer.UtcNow, mayAct: true), Reused: true);

        var now = writer.UtcNow;
        var (job, error) = ReportJob.Request(Guid.NewGuid(), type, actor, json, hash, now, settings.TimeZone, ReportPolicy.V1, now);
        if (error is not null)
            return error.ToApplicationError();

        jobs.Add(job!);
        writer.Audit(RequestedAction, ReportResource, job!.Id, toState: job.Status.ToString(), ruleIds: ["RPT-001", "RPT-002"],
            details: new Dictionary<string, string> { ["reportType"] = type.ToString() });
        var staged = writer.Stage(job.DomainEvents);
        var commit = await writer.CommitAsync(
            _ => Task.FromResult<long?>(null),
            constraint => constraint == IReportJobRepository.ActiveUniqueKey ? ActiveRace : null,
            cancellationToken);
        if (commit is not null)
        {
            // A concurrent identical request won the partial unique index: answer with its job (RPT-001).
            if (await jobs.FindActiveAsync(actor, type, hash, cancellationToken) is { } winner)
                return new ReportRequestResult(ReportJobView.From(winner, writer.UtcNow, mayAct: true), Reused: true);
            return commit == ActiveRace ? new ApplicationError(ProblemCodes.VersionMismatch, ErrorCategory.PreconditionFailed, "Retry the request.") : commit;
        }

        writer.Record(staged);
        job.ClearDomainEvents();
        return new ReportRequestResult(ReportJobView.From(job, writer.UtcNow, mayAct: true), Reused: false);
    }

    public async Task<Result<ReportJobView>> Handle(GetReportJobQuery request, CancellationToken cancellationToken)
    {
        var job = await jobs.FindAsync(request.Id, cancellationToken);
        if (job is null || !await access.MayReadAsync(job, cancellationToken))
            return ReportAccess.JobNotFound();
        return ReportJobView.From(job, writer.UtcNow, access.IsRequester(job));
    }

    public async Task<Result<PagedResult<ReportJobView>>> Handle(ListReportJobsQuery request, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "mine" : request.Scope.Trim().ToLowerInvariant();
        if (scope is not ("mine" or "all"))
            return ApplicationError.Validation("scope", "scope accepts mine or all.");
        if (scope == "all" ? !await access.MayReadAllAsync(cancellationToken) : !await access.MayRequestAsync(cancellationToken) && !await access.MayReadAllAsync(cancellationToken))
            return ProcurementWriter.Forbidden();
        var statuses = ProcurementQueries.ParseEnumList<ReportJobStatus>(request.Status, "status");
        if (!statuses.IsSuccess)
            return statuses.Error!;
        var types = ProcurementQueries.ParseEnumList<ReportType>(request.ReportType, "reportType");
        if (!types.IsSuccess)
            return types.Error!;
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IReportJobRepository.SortFields, "-requestedAt");
        if (!page.IsSuccess)
            return page.Error!;

        var filter = new ReportJobFilter(scope == "mine" ? writer.ActorId ?? Guid.Empty : null, statuses.Value, types.Value);
        var result = await jobs.ListAsync(filter, page.Value, cancellationToken);
        var now = writer.UtcNow;
        return new PagedResult<ReportJobView>(
            result.Items.Select(job => ReportJobView.From(job, now, access.IsRequester(job))).ToArray(), result.Page, result.PageSize, result.TotalItems);
    }

    public async Task<Result<ReportJobView>> Handle(CancelReportJobCommand request, CancellationToken cancellationToken)
    {
        var job = await jobs.FindAsync(request.Id, cancellationToken);
        if (job is null || !await access.MayReadAsync(job, cancellationToken))
            return ReportAccess.JobNotFound();
        if (!access.IsRequester(job))
            return ProcurementWriter.Forbidden("Only the requester may cancel a report job.");
        if (VersionPrecondition.Check(request.ExpectedVersion, job.Version) is { } precondition)
            return precondition;
        var previous = job.Status;
        if (job.Cancel(writer.UtcNow) is { } error)
            return error.ToApplicationError();
        if (previous == ReportJobStatus.Cancelled)
            return ReportJobView.From(job, writer.UtcNow, mayAct: true);

        writer.Audit(CancelledAction, ReportResource, job.Id, previous.ToString(), job.Status.ToString(), ruleIds: ["RPT-001"]);
        var commit = await writer.CommitAsync(token => jobs.GetVersionAsync(job.Id, token), null, cancellationToken);
        return commit is null ? ReportJobView.From(job, writer.UtcNow, mayAct: true) : commit;
    }

    public async Task<Result<ReportDownload>> Handle(DownloadReportQuery request, CancellationToken cancellationToken)
    {
        var job = await jobs.FindAsync(request.Id, cancellationToken);
        if (job is null || !await access.MayReadAsync(job, cancellationToken))
            return ReportAccess.JobNotFound();
        var now = writer.UtcNow;
        if (job.IsPastRetention(now))
            return new ApplicationError(ProblemCodes.ReportExpired, ErrorCategory.Gone, "The report output expired.", "RPT-006");
        if (!job.IsDownloadable(now))
            return new ApplicationError(ProblemCodes.ReportNotReady, ErrorCategory.Conflict, "The report output is not ready.", "RPT-006",
                new Dictionary<string, object?> { ["currentState"] = job.Status.ToString() });

        var file = await files.FindAsync(job.OutputFileId!.Value, cancellationToken);
        if (file is null || !file.IsDownloadable(now))
            return FileUnavailable();
        Stream? content;
        try
        {
            content = await storage.OpenReadAsync(file.Id, cancellationToken);
        }
        catch (FileStorageUnavailableException)
        {
            return FileUnavailable();
        }

        if (content is null)
            return FileUnavailable();

        if (file.Classification == FileClassification.Confidential)
        {
            writer.Audit(DownloadedAction, FileResource, file.Id, ruleIds: ["FIL-003", "RPT-006"],
                details: new Dictionary<string, string> { ["reportJobId"] = job.Id.ToString(), ["reportType"] = job.ReportType.ToString() });
            if (await writer.CommitAsync(_ => Task.FromResult<long?>(null), null, cancellationToken) is { } commit)
            {
                await content.DisposeAsync();
                return commit;
            }
        }

        return new ReportDownload(content, file.OriginalName, file.MediaType, file.Length ?? job.OutputLength ?? 0);
    }

    public async Task<Result<IReadOnlyList<ReportTypeView>>> Handle(ListReportTypesQuery request, CancellationToken cancellationToken)
    {
        var mayRequest = await access.MayRequestAsync(cancellationToken);
        var views = new List<ReportTypeView>();
        foreach (var type in Enum.GetValues<ReportType>().Where(type => !ReportCatalog.IsSystemOnly(type)))
        {
            views.Add(new ReportTypeView(
                type.ToString(), ReportCatalog.Format(type).ToString(), ReportCatalog.MediaType(type),
                [Security.Permissions.ReportsRequest, .. ReportAccess.DataPermissions(type)], ReportAccess.ParameterNames(type),
                mayRequest && await access.HasDataPermissionAsync(type, cancellationToken)));
        }

        return views;
    }

    private async Task<(ReportParameters? Parameters, ApplicationError? Error)> PinAsync(ReportType type, RequestReportCommand request, CancellationToken cancellationToken)
    {
        var supplied = new Dictionary<string, bool>
        {
            ["requisitionId"] = request.RequisitionId is not null,
            ["rfqId"] = request.RfqId is not null,
            ["fiscalPeriodId"] = request.FiscalPeriodId is not null,
            ["costCenterId"] = request.CostCenterId is not null,
            ["currency"] = request.Currency is not null,
            ["from"] = request.From is not null,
            ["to"] = request.To is not null,
            ["supplierId"] = request.SupplierId is not null
        };
        var allowed = ReportAccess.ParameterNames(type).Select(name => name.TrimEnd('?')).ToHashSet(StringComparer.Ordinal);
        var unexpected = supplied.Where(pair => pair.Value && !allowed.Contains(pair.Key)).Select(pair => pair.Key).ToArray();
        if (unexpected.Length > 0)
            return (null, ApplicationError.Validation(unexpected.ToDictionary(name => name, _ => new[] { $"This parameter does not apply to {type}." })));
        var missing = ReportAccess.ParameterNames(type).Where(name => !name.EndsWith('?') && !supplied[name]).ToArray();
        if (missing.Length > 0)
            return (null, ApplicationError.Validation(missing.ToDictionary(name => name, _ => new[] { $"This parameter is required for {type}." })));

        switch (type)
        {
            case ReportType.RequisitionPdf:
            {
                var requisition = await requisitions.FindAsync(request.RequisitionId!.Value, cancellationToken);
                if (requisition is null || !await requisitionAccess.CanReadAsync(requisition, cancellationToken))
                    return (null, RequisitionAudit.NotFound());
                return (new ReportParameters(RequisitionId: requisition.Id, RequisitionVersion: requisition.Version), null);
            }
            case ReportType.QuoteComparisonPdf:
            {
                var rfq = await rfqs.FindAsync(request.RfqId!.Value, cancellationToken);
                if (rfq is null)
                    return (null, ProcurementWriter.NotFound("request for quotation"));
                if (rfq.CurrentComparison is not { } comparison)
                    return (null, new ApplicationError(ProblemCodes.ReportSourceNotAvailable, ErrorCategory.BusinessRule,
                        "The request for quotation has no comparison yet; close it first.", "RPT-003"));
                return (new ReportParameters(RfqId: rfq.Id, ComparisonSequence: comparison.Sequence), null);
            }
            case ReportType.BudgetConsumptionXlsx:
            {
                if (await sources.FindFiscalPeriodAsync(request.FiscalPeriodId!.Value, cancellationToken) is null)
                    return (null, ApplicationError.Validation("fiscalPeriodId", "The fiscal period does not exist."));
                string? currency = null;
                if (request.Currency is not null && (currency = CurrencyCodes.Normalize(request.Currency)) is null)
                    return (null, ApplicationError.Validation("currency", "The currency must be an ISO 4217 code."));
                return (new ReportParameters(FiscalPeriodId: request.FiscalPeriodId, CostCenterId: request.CostCenterId, Currency: currency), null);
            }
            default:
            {
                if (request.To < request.From)
                    return (null, ApplicationError.Validation("to", "The end date must be on or after the start date."));
                if (request.To!.Value.DayNumber - request.From!.Value.DayNumber + 1 > ReportParameters.MaxWindowDays)
                    return (null, ApplicationError.Validation("to", $"The window has at most {ReportParameters.MaxWindowDays} days."));
                return (new ReportParameters(From: request.From, To: request.To, SupplierId: request.SupplierId), null);
            }
        }
    }

    private static bool TryParseType(string? value, out ReportType type) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out type) && !int.TryParse(value, out _) && Enum.IsDefined(type) && !ReportCatalog.IsSystemOnly(type);

    private static ApplicationError TypeUnknown() => new(
        ProblemCodes.ReportTypeUnknown, ErrorCategory.Validation, "The report type is not in the report catalog.", "RPT-009",
        FieldErrors: new Dictionary<string, string[]> { ["reportType"] = [$"reportType accepts: {string.Join(", ", Enum.GetValues<ReportType>().Where(type => !ReportCatalog.IsSystemOnly(type)))}."] });

    private static ApplicationError FileUnavailable() => new(
        ProblemCodes.FileUnavailable, ErrorCategory.Unavailable, "The stored output is temporarily unavailable.", "FIL-003");
}
