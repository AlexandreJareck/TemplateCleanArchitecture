using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Reporting;
using Template.Application.Security;
using Template.Domain.Reporting;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Asynchronous reports (RPT-001..009, FIL-003, Phase 30). A request returns <c>202 Accepted</c> with the job, its
/// <c>Location</c> and <c>Retry-After: 2</c>; the Worker renders it from a parameter snapshot and an as-of instant.
/// Jobs are visible to their requester (with <c>reports.request</c> and the data permission of the report) or to
/// <c>reports.read-all</c> holders; any other job answers 404. Outputs are streamed by the API (never a storage URL)
/// while Ready and inside the 7-day retention, then answer <c>410 report.expired</c>.
/// </summary>
[ApiVersion("2.0")]
[Route("api/v{version:apiVersion}/report-jobs")]
[StableProblemCodes]
[NoStoreSecurityHeaders]
public sealed class ReportJobsController(ISender sender, ApiResultMapper resultMapper) : MasterDataControllerBase(sender, resultMapper)
{
    private readonly ISender sender = sender;
    private readonly ApiResultMapper resultMapper = resultMapper;

    /// <summary>List report jobs: <c>scope=mine</c> (default, <c>reports.request</c>) or <c>scope=all</c> (<c>reports.read-all</c>).</summary>
    /// <param name="page">Page number, 1 or greater (default 1).</param>
    /// <param name="pageSize">Items per page, 1–100 (default 20).</param>
    /// <param name="sort">Comma-separated <c>requestedAt</c>, <c>status</c>, <c>reportType</c>, <c>completedAt</c>; prefix <c>-</c> for descending (default <c>-requestedAt</c>).</param>
    /// <param name="scope"><c>mine</c> or <c>all</c>.</param>
    /// <param name="status">Comma-separated statuses, for example <c>Pending,Running</c>.</param>
    /// <param name="reportType">Comma-separated report types.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [Authorize]
    [Produces("application/json")]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(PagedResult<ReportJobView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public Task<IActionResult> List(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? sort, [FromQuery] string? scope, [FromQuery] string? status,
        [FromQuery] string? reportType, CancellationToken cancellationToken) =>
        SendAsync(new ListReportJobsQuery(page, pageSize, sort, scope, status, reportType), Ok, cancellationToken);

    /// <summary>Report catalog (RPT-009) with the permissions and parameters of each type and whether the caller may request it.</summary>
    [HttpGet("types")]
    [Authorize]
    [Produces("application/json")]
    [ProblemCodes(ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(IReadOnlyList<ReportTypeView>), StatusCodes.Status200OK)]
    public Task<IActionResult> Types(CancellationToken cancellationToken) =>
        SendAsync(new ListReportTypesQuery(), Ok, cancellationToken);

    /// <summary>Job status, progress, safe failure code and, when downloadable, the API content path.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [Produces("application/json")]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(ReportJobView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new GetReportJobQuery(id), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Request a report (RPT-001/002/009). Requires <c>reports.request</c>, the read permission of the underlying data and
    /// <c>Idempotency-Key</c>. Only the parameters of the chosen type are accepted. An identical active request of the
    /// same user returns the existing job (also <c>202</c>).
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.ReportsRequest)]
    [Produces("application/json")]
    [RequireIdempotencyKey]
    [ProblemCodes(ProblemCodes.Validation, ProblemCodes.ReportTypeUnknown, ProblemCodes.ReportSourceNotAvailable, ProblemCodes.NotFound,
        ProblemCodes.IdempotencyKeyRequired, ProblemCodes.IdempotencyKeyReused, ProblemCodes.IdempotencyInProgress, ProblemCodes.SessionExpired,
        ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ReportJobView), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<IActionResult> Create([FromBody] RequestReportBody request, CancellationToken cancellationToken) =>
        SendAsync(new RequestReportCommand(request.ReportType, request.RequisitionId, request.RfqId, request.FiscalPeriodId, request.CostCenterId,
            request.Currency, request.From, request.To, request.SupplierId),
            result =>
            {
                VersionHeaders.SetETag(Response, long.Parse(result.Job.Version, System.Globalization.CultureInfo.InvariantCulture));
                Response.Headers.RetryAfter = AcceptedOperations.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Accepted($"/api/v2/report-jobs/{result.Job.Id}", result.Job);
            },
            cancellationToken);

    /// <summary>Cancel a Pending job of the caller (workflows §6). Requires <c>If-Match</c>.</summary>
    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.ReportsRequest)]
    [Produces("application/json")]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.InvalidTransition, ProblemCodes.VersionMismatch, ProblemCodes.PreconditionRequired,
        ProblemCodes.SessionExpired, ProblemCodes.Forbidden)]
    [ProducesResponseType(typeof(ReportJobView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired, "application/problem+json")]
    public Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        SendAsync(new CancelReportJobCommand(id, IfMatch), view => Versioned(view, view.Version), cancellationToken);

    /// <summary>
    /// Stream the output (FIL-003, RPT-006): <c>Content-Disposition: attachment</c> with the sanitized name, the exact media
    /// type, <c>nosniff</c> and <c>no-store</c>. 409 while not ready, 410 after the retention, 503 when storage is unavailable.
    /// </summary>
    [HttpGet("{id:guid}/content")]
    [Authorize]
    [ProblemCodes(ProblemCodes.NotFound, ProblemCodes.ReportNotReady, ProblemCodes.ReportExpired, ProblemCodes.FileUnavailable, ProblemCodes.SessionExpired)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, ReportCatalog.PdfMediaType, ReportCatalog.XlsxMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DownloadReportQuery(id), cancellationToken);
        return resultMapper.ToActionResult(result, HttpContext, () =>
            new AttachmentStreamResult(result.Value.Content, result.Value.MediaType, result.Value.FileName, result.Value.Length));
    }
}
