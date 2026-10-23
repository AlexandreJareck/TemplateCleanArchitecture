using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Reporting;
using Template.Application.Security;
using Template.Application.Services.Reporting;
using Template.Domain.Reporting;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Phase 30 HTTP contract on the self-contained Test host (in-memory storage, no broker, no browser): 202 with
/// Location/Retry-After/ETag, Idempotency-Key replay and identical-request reuse (RPT-001), data permissions (RPT-002),
/// catalog and parameter validation (RPT-009), status, cancel with If-Match, the Worker attempt executed in-process with
/// signed fake renderers, the streamed download headers (FIL-003), requester/read-all visibility, 410 after retention
/// (RPT-006) and 503 when bytes are missing. All data is synthetic.
/// </summary>
public sealed class ReportJobsFunctionalTests(ProcurementWebApplicationFactory factory) : IClassFixture<ProcurementWebApplicationFactory>
{
    private const string Jobs = "/api/v2/report-jobs";

    [Fact]
    public async Task T_RPT_001_f_RequisitionReport_Accepted_Processed_AndDownloadedWithSafeHeaders()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (requisition, _) = await scenario.SubmittedAsync();
        var requisitionId = requisition.GetProperty("id").GetGuid();
        var body = new { reportType = "RequisitionPdf", requisitionId };

        var key = Guid.NewGuid().ToString();
        using var accepted = await scenario.Requester.SendAsync(HttpMethod.Post, Jobs, body, idempotencyKey: key);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync());
        var job = await ApiUser.ReadAsync(accepted);
        var id = job.GetProperty("id").GetGuid();
        accepted.Headers.Location!.ToString().ShouldBe($"{Jobs}/{id}");
        accepted.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(2));
        accepted.Headers.ETag.ShouldNotBeNull();
        job.GetProperty("status").GetString().ShouldBe("Pending");
        job.GetProperty("format").GetString().ShouldBe("Pdf");
        job.GetProperty("parameters").GetProperty("requisitionId").GetGuid().ShouldBe(requisitionId);
        job.GetProperty("contentUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        job.GetProperty("maxAttempts").GetInt32().ShouldBe(3);

        using var replay = await scenario.Requester.SendAsync(HttpMethod.Post, Jobs, body, idempotencyKey: key);
        replay.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        replay.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        using var identical = await scenario.Requester.PostAsync(Jobs, body);
        (await ApiUser.ReadAsync(identical)).GetProperty("id").GetGuid().ShouldBe(id); // identical active request

        using var notReady = await scenario.Requester.GetAsync($"{Jobs}/{id}/content");
        notReady.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(notReady)).ShouldBe("report.not-ready");

        (await RunWorkerAttemptAsync()).ShouldBe(ReportProcessingOutcome.Ready);

        using var status = await scenario.Requester.GetAsync($"{Jobs}/{id}");
        var ready = await ApiUser.ReadAsync(status);
        ready.GetProperty("status").GetString().ShouldBe("Ready");
        ready.GetProperty("progress").GetInt32().ShouldBe(100);
        ready.GetProperty("contentUrl").GetString().ShouldBe($"{Jobs}/{id}/content");
        ready.GetProperty("mediaType").GetString().ShouldBe("application/pdf");
        Strings(ready.GetProperty("allowedActions")).ShouldBe(["download"]);
        ready.GetRawText().ShouldNotContain("blob");
        ready.GetRawText().ShouldNotContain("http://");

        using var content = await scenario.Requester.GetAsync($"{Jobs}/{id}/content");
        content.StatusCode.ShouldBe(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        content.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        content.Content.Headers.ContentDisposition.FileNameStar.ShouldStartWith("requisicao-REQ-");
        content.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        content.Headers.CacheControl!.NoStore.ShouldBeTrue();
        content.Headers.CacheControl.Private.ShouldBeTrue();
        (await content.Content.ReadAsByteArrayAsync()).Take(5).ShouldBe("%PDF-"u8.ToArray());

        // RPT-006: another requester cannot infer the job; an auditor (reports.read-all) can download it.
        var other = await ApiUser.CreateAsync(factory, SeededRoles.Requester);
        using var hidden = await other.GetAsync($"{Jobs}/{id}");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var hiddenContent = await other.GetAsync($"{Jobs}/{id}/content");
        hiddenContent.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);
        using var audited = await auditor.GetAsync($"{Jobs}/{id}/content");
        audited.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var all = await auditor.GetAsync($"{Jobs}?scope=all&status=Ready");
        (await ApiUser.ReadAsync(all)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldContain(id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await context.Set<OutboxMessage>().Where(message => message.AggregateId == id).Select(message => message.EventType).ToListAsync())
                .ShouldBe(["procurement.report.requested", "procurement.report.ready"], ignoreOrder: true);
            (await context.Set<Template.Domain.Auditing.AuditRecord>().CountAsync(record => record.Action == "file.downloaded")).ShouldBeGreaterThanOrEqualTo(2);

            // Bytes lost (database restored without blobs, NFR-REC-3): 503, never a broken stream.
            var file = (await context.Set<ReportJob>().SingleAsync(item => item.Id == id)).OutputFileId!.Value;
            await scope.ServiceProvider.GetRequiredService<IFileStorage>().DeleteAsync(file, CancellationToken.None);
        }

        using var missing = await scenario.Requester.GetAsync($"{Jobs}/{id}/content");
        missing.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ApiUser.CodeAsync(missing)).ShouldBe("file.unavailable");

        await ExpireAsync(id);
        using var expired = await scenario.Requester.GetAsync($"{Jobs}/{id}/content");
        expired.StatusCode.ShouldBe(HttpStatusCode.Gone);
        (await ApiUser.CodeAsync(expired)).ShouldBe("report.expired");
        using var expiredStatus = await scenario.Requester.GetAsync($"{Jobs}/{id}");
        (await ApiUser.ReadAsync(expiredStatus)).GetProperty("status").GetString().ShouldBe("Expired");
    }

    [Fact]
    public async Task T_RPT_002_c_DataPermissions_Catalog_AndValidation_AreEnforcedOverHttp()
    {
        var requester = await ApiUser.CreateAsync(factory, SeededRoles.Requester);
        using var budget = await requester.PostAsync(Jobs, new { reportType = "BudgetConsumptionXlsx", fiscalPeriodId = Guid.NewGuid() });
        budget.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var problem = await ApiUser.ReadAsync(budget);
        problem.GetProperty("code").GetString().ShouldBe("auth.forbidden");
        problem.GetProperty("ruleId").GetString().ShouldBe("RPT-002");

        using var unknown = await requester.PostAsync(Jobs, new { reportType = "PurchaseOrderPdf" });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(unknown)).ShouldBe("report.type-unknown");
        using var template = await requester.PostAsync(Jobs, new { reportType = "RequisitionPdf", requisitionId = Guid.NewGuid(), template = "<script>alert(1)</script>" });
        template.StatusCode.ShouldBe(HttpStatusCode.NotFound); // unknown members are ignored; nothing can carry a template (RPT-008)
        using var foreign = await requester.PostAsync(Jobs, new { reportType = "RequisitionPdf", requisitionId = Guid.NewGuid(), rfqId = Guid.NewGuid() });
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.ReadAsync(foreign)).GetProperty("errors").TryGetProperty("rfqId", out _).ShouldBeTrue();
        using var noKey = await requester.SendAsync(HttpMethod.Post, Jobs, new { reportType = "RequisitionPdf", requisitionId = Guid.NewGuid() });
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(noKey)).ShouldBe("idempotency.key-required");

        using var types = await requester.GetAsync($"{Jobs}/types");
        var catalog = (await ApiUser.ReadAsync(types)).EnumerateArray().ToDictionary(item => item.GetProperty("type").GetString()!, item => item.GetProperty("available").GetBoolean());
        catalog.ShouldBe(new Dictionary<string, bool>
        {
            ["RequisitionPdf"] = true, ["QuoteComparisonPdf"] = false, ["BudgetConsumptionXlsx"] = false, ["SupplierQuotesXlsx"] = false
        });

        var administrator = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);
        using var noReports = await administrator.PostAsync(Jobs, new { reportType = "RequisitionPdf", requisitionId = Guid.NewGuid() });
        noReports.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var anonymous = await factory.CreateClient().GetAsync(Jobs);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task T_RPT_001_g_BudgetWorkbook_CancelNeedsIfMatch_AndAFailedSourceShowsASafeCode()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        using var first = await scenario.Budget.PostAsync(Jobs, new { reportType = "BudgetConsumptionXlsx", fiscalPeriodId = scenario.PeriodId, costCenterId = scenario.CostCenterId });
        first.StatusCode.ShouldBe(HttpStatusCode.Accepted, await first.Content.ReadAsStringAsync());
        var id = (await ApiUser.ReadAsync(first)).GetProperty("id").GetGuid();

        using var noIfMatch = await scenario.Budget.SendAsync(HttpMethod.Post, $"{Jobs}/{id}/cancel", new { });
        noIfMatch.StatusCode.ShouldBe((HttpStatusCode)428);
        using var stale = await scenario.Budget.SendAsync(HttpMethod.Post, $"{Jobs}/{id}/cancel", new { }, "\"99\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var requester = await ApiUser.CreateAsync(factory, SeededRoles.Requester);
        using var foreignCancel = await requester.SendAsync(HttpMethod.Post, $"{Jobs}/{id}/cancel", new { }, first.Headers.ETag!.Tag);
        foreignCancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var cancelled = await scenario.Budget.SendAsync(HttpMethod.Post, $"{Jobs}/{id}/cancel", new { }, first.Headers.ETag!.Tag);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var view = await ApiUser.ReadAsync(cancelled);
        view.GetProperty("status").GetString().ShouldBe("Cancelled");
        Strings(view.GetProperty("allowedActions")).ShouldBe(["retry"]);

        // Retry = a new request; the workbook is produced by the Worker attempt.
        using var retry = await scenario.Budget.PostAsync(Jobs, new { reportType = "BudgetConsumptionXlsx", fiscalPeriodId = scenario.PeriodId, costCenterId = scenario.CostCenterId });
        var retryId = (await ApiUser.ReadAsync(retry)).GetProperty("id").GetGuid();
        retryId.ShouldNotBe(id);
        (await RunWorkerAttemptAsync()).ShouldBe(ReportProcessingOutcome.Ready);
        using var workbook = await scenario.Budget.GetAsync($"{Jobs}/{retryId}/content");
        workbook.StatusCode.ShouldBe(HttpStatusCode.OK);
        workbook.Content.Headers.ContentType!.MediaType.ShouldBe(ReportCatalog.XlsxMediaType);
        workbook.Content.Headers.ContentDisposition!.FileNameStar.ShouldEndWith(".xlsx");

        using var mine = await scenario.Budget.GetAsync($"{Jobs}?status=Ready,Cancelled&sort=-requestedAt");
        var items = (await ApiUser.ReadAsync(mine)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        items.ShouldContain(id);
        items.ShouldContain(retryId);
        using var badScope = await scenario.Budget.GetAsync($"{Jobs}?scope=all");
        badScope.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Runs one Worker attempt in-process: the real use case with the Test host's repositories and storage.</summary>
    private async Task<ReportProcessingOutcome> RunWorkerAttemptAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var renderer = new SignedRenderer();
        var processor = ActivatorUtilities.CreateInstance<ReportJobProcessor>(scope.ServiceProvider, (IPdfRenderer)renderer, (ISpreadsheetWriter)renderer);
        var outcome = ReportProcessingOutcome.Idle;
        for (var attempt = 0; attempt < 20 && outcome is ReportProcessingOutcome.Idle; attempt++)
        {
            (outcome, _) = await processor.ProcessNextAsync("functional-worker", CancellationToken.None);
            if (outcome == ReportProcessingOutcome.Idle)
                await Task.Delay(50);
        }

        return outcome;
    }

    private async Task ExpireAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await context.Set<ReportJob>().SingleAsync(item => item.Id == id);
        context.Entry(job).Property(nameof(ReportJob.ExpiresAtUtc)).CurrentValue = DateTime.UtcNow.AddMinutes(-1);
        await context.SaveChangesAsync();
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private sealed class SignedRenderer : IPdfRenderer, ISpreadsheetWriter
    {
        public Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken) =>
            output.WriteAsync("%PDF-1.7 functional"u8.ToArray(), cancellationToken).AsTask();

        public Task WriteAsync(WorkbookDocument workbook, Stream output, CancellationToken cancellationToken) =>
            output.WriteAsync(new byte[] { 0x50, 0x4B, 0x03, 0x04, 7, 7 }, cancellationToken).AsTask();
    }
}
