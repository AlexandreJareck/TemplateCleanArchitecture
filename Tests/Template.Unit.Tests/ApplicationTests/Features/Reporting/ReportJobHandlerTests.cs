using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Reporting;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Wrappers;
using Template.Domain.Files;
using Template.Domain.Reporting;
using Template.Unit.Tests.ApplicationTests.Features.Procurement;

namespace Template.Unit.Tests.ApplicationTests.Features.Reporting;

/// <summary>
/// Report request, status, list, cancel and download use cases against port fakes: RPT-001 idempotent active jobs,
/// RPT-002 data permissions, RPT-006 requester/read-all visibility and retention, RPT-009 catalog and FIL-003 download
/// authorization, unavailability and audit. All data is synthetic.
/// </summary>
public sealed class ReportJobHandlerTests
{
    private readonly ReportingTestDoubles doubles = new();

    [Fact]
    public async Task T_RPT_001_a_RequestQueuesAPendingJob_WithSnapshotAuditAndOutboxEvent()
    {
        var requisition = await doubles.SubmittedRequisitionAsync();

        var result = await doubles.Handlers().Handle(new RequestReportCommand("requisitionpdf", RequisitionId: requisition.Id), default);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.Reused.ShouldBeFalse();
        var view = result.Value.Job;
        view.Status.ShouldBe("Pending");
        view.ReportType.ShouldBe("RequisitionPdf");
        view.Format.ShouldBe("Pdf");
        view.Parameters.RequisitionId.ShouldBe(requisition.Id);
        view.AsOf.ShouldBe(ProcurementTestDoubles.Now);
        view.TimeZone.ShouldBe("America/Sao_Paulo");
        view.ContentUrl.ShouldBeNull();
        view.AllowedActions.ShouldBe([ReportJobView.ActionCancel]);
        var job = doubles.Jobs.Items.Single();
        ReportParameters.Parse(job.ParametersJson).RequisitionVersion.ShouldBe(requisition.Version);
        doubles.Procurement.Outbox.Events.Last().EventType.ShouldBe("procurement.report.requested");
        doubles.Procurement.Audit.Records.Last().Action.ShouldBe(ReportJobHandlers.RequestedAction);
    }

    [Fact]
    public async Task T_RPT_001_b_IdenticalActiveRequestReturnsTheExistingJob_DifferentParametersDoNot()
    {
        var first = await doubles.SubmittedRequisitionAsync();
        var second = await doubles.SubmittedRequisitionAsync();
        var handlers = doubles.Handlers();

        var original = (await handlers.Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: first.Id), default)).Value;
        var repeated = (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: first.Id), default)).Value;
        var other = (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: second.Id), default)).Value;

        repeated.Reused.ShouldBeTrue();
        repeated.Job.Id.ShouldBe(original.Job.Id);
        other.Job.Id.ShouldNotBe(original.Job.Id);
        doubles.Jobs.Items.Count.ShouldBe(2);

        // After completion or cancellation a new request creates a new job.
        doubles.Jobs.Items.Single(job => job.Id == original.Job.Id).Cancel(ProcurementTestDoubles.Now);
        (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: first.Id), default)).Value.Reused.ShouldBeFalse();
    }

    [Fact]
    public async Task T_RPT_001_c_ConcurrentIdenticalRequest_LosingTheUniqueIndex_ReturnsTheWinner()
    {
        var requisition = await doubles.SubmittedRequisitionAsync();
        var winner = (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: requisition.Id), default)).Value.Job;
        var winnerJob = doubles.Jobs.Items.Single();
        doubles.Jobs.Items.Clear(); // the loser checks before the winner is visible ...
        doubles.Procurement.UnitOfWork.UniqueViolation = Template.Application.Interfaces.Reporting.IReportJobRepository.ActiveUniqueKey;
        doubles.Procurement.UnitOfWork.BeforeSave = () =>
        {
            // ... and the winner commits first, so the loser's insert hits the partial unique index.
            doubles.Jobs.Items.Clear();
            doubles.Jobs.Items.Add(winnerJob);
        };
        var loser = await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: requisition.Id), default);

        loser.IsSuccess.ShouldBeTrue(loser.Error?.Message);
        loser.Value.Reused.ShouldBeTrue();
        loser.Value.Job.Id.ShouldBe(winner.Id);
    }

    [Fact]
    public async Task T_RPT_002_a_RequestNeedsReportsRequestAndTheDataPermission()
    {
        var requisition = await doubles.SubmittedRequisitionAsync();
        var nobody = new ProcurementTestDoubles.Actor();
        doubles.Procurement.ActAs(nobody);
        (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: requisition.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);

        doubles.Procurement.ActAs(doubles.Procurement.Requester); // Requester: no budget.read
        var budget = await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: doubles.Procurement.Period.Id), default);
        budget.Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        budget.Error.RuleId.ShouldBe("RPT-002");
        (await doubles.Handlers().Handle(new RequestReportCommand("SupplierQuotesXlsx", From: new DateOnly(2027, 1, 1), To: new DateOnly(2027, 1, 31)), default))
            .Error!.Code.ShouldBe(ProblemCodes.Forbidden);

        doubles.Procurement.ActAs(doubles.Controller);
        (await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: doubles.Procurement.Period.Id, Currency: "brl"), default))
            .Value.Job.Parameters.Currency.ShouldBe("BRL");
    }

    [Fact]
    public async Task T_RPT_002_b_InvisibleRequisitionAnswersNotFound_AndRfqWithoutComparisonIsUnavailable()
    {
        var requisition = await doubles.SubmittedRequisitionAsync();
        var otherRequester = new ProcurementTestDoubles.Actor();
        doubles.Procurement.Permissions.Grant(otherRequester.Id, SeededRoles.PermissionsByRole[SeededRoles.Requester]);
        doubles.Procurement.ActAs(otherRequester);
        (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: requisition.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: Guid.NewGuid()), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);

        doubles.Procurement.ActAs(doubles.Buyer);
        (await doubles.Procurement.RfqCommands().Handle(new Template.Application.Features.Sourcing.CreateRfqCommand(requisition.Id, null), default)).IsSuccess.ShouldBeTrue();
        var rfq = doubles.Procurement.Rfqs.Items.Single();
        var draftRfq = await doubles.Handlers().Handle(new RequestReportCommand("QuoteComparisonPdf", RfqId: rfq.Id), default);
        draftRfq.Error!.Code.ShouldBe(ProblemCodes.ReportSourceNotAvailable);
        draftRfq.Error.Category.ShouldBe(ErrorCategory.BusinessRule);
        (await doubles.Handlers().Handle(new RequestReportCommand("QuoteComparisonPdf", RfqId: Guid.NewGuid()), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
    }

    [Fact]
    public async Task T_RPT_009_a_UnknownTypesAndForeignOrMissingParametersAreRejected()
    {
        doubles.Procurement.ActAs(doubles.Controller);
        var unknown = await doubles.Handlers().Handle(new RequestReportCommand("PurchaseOrderPdf"), default);
        unknown.Error!.Code.ShouldBe(ProblemCodes.ReportTypeUnknown);
        unknown.Error.Category.ShouldBe(ErrorCategory.Validation);
        (await doubles.Handlers().Handle(new RequestReportCommand("1"), default)).Error!.Code.ShouldBe(ProblemCodes.ReportTypeUnknown);
        (await doubles.Handlers().Handle(new RequestReportCommand("<script>"), default)).Error!.Code.ShouldBe(ProblemCodes.ReportTypeUnknown);

        var foreign = await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: doubles.Procurement.Period.Id, RfqId: Guid.NewGuid()), default);
        foreign.Error!.FieldErrors!.Keys.ShouldBe(["rfqId"]);
        (await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx"), default)).Error!.FieldErrors!.Keys.ShouldBe(["fiscalPeriodId"]);
        (await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: Guid.NewGuid()), default)).Error!.FieldErrors!.Keys.ShouldBe(["fiscalPeriodId"]);
        (await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: doubles.Procurement.Period.Id, Currency: "1$X"), default))
            .Error!.FieldErrors!.Keys.ShouldBe(["currency"]);

        doubles.Procurement.ActAs(doubles.Buyer);
        (await doubles.Handlers().Handle(new RequestReportCommand("SupplierQuotesXlsx", From: new DateOnly(2027, 2, 1), To: new DateOnly(2027, 1, 1)), default))
            .Error!.FieldErrors!.Keys.ShouldBe(["to"]);
        (await doubles.Handlers().Handle(new RequestReportCommand("SupplierQuotesXlsx", From: new DateOnly(2026, 1, 1), To: new DateOnly(2027, 1, 2)), default))
            .Error!.FieldErrors!.Keys.ShouldBe(["to"]);
        (await doubles.Handlers().Handle(new RequestReportCommand("SupplierQuotesXlsx", From: new DateOnly(2026, 1, 2), To: new DateOnly(2027, 1, 2)), default))
            .IsSuccess.ShouldBeTrue(); // exactly 366 days

        var catalog = (await doubles.Handlers().Handle(new ListReportTypesQuery(), default)).Value;
        catalog.Select(type => type.Type).ShouldBe(["RequisitionPdf", "QuoteComparisonPdf", "BudgetConsumptionXlsx", "SupplierQuotesXlsx"]);
        catalog.Single(type => type.Type == "SupplierQuotesXlsx").Available.ShouldBeTrue();
        catalog.Single(type => type.Type == "BudgetConsumptionXlsx").Available.ShouldBeFalse(); // buyer has no budget.read
        catalog.ShouldAllBe(type => type.RequiredPermissions.Contains(Permissions.ReportsRequest));
    }

    [Fact]
    public async Task T_RPT_006_a_OnlyTheRequesterOrReadAllHoldersSeeAJob()
    {
        var job = await RequestedRequisitionReportAsync();
        doubles.Procurement.ActAs(doubles.Buyer); // holds requisitions.read-all, but not the requester
        (await doubles.Handlers().Handle(new GetReportJobQuery(job.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await doubles.Handlers().Handle(new ListReportJobsQuery(), default)).Value.Items.ShouldBeEmpty();
        (await doubles.Handlers().Handle(new ListReportJobsQuery(Scope: "all"), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);

        doubles.Procurement.ActAs(doubles.Auditor); // reports.read-all
        var seen = (await doubles.Handlers().Handle(new GetReportJobQuery(job.Id), default)).Value;
        seen.AllowedActions.ShouldBeEmpty(); // cancel belongs to the requester
        (await doubles.Handlers().Handle(new ListReportJobsQuery(Scope: "ALL"), default)).Value.TotalItems.ShouldBe(1);
        (await doubles.Handlers().Handle(new ListReportJobsQuery(Scope: "others"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await doubles.Handlers().Handle(new ListReportJobsQuery(Scope: "all", Status: "Done"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await doubles.Handlers().Handle(new ListReportJobsQuery(Scope: "all", ReportType: "Nope"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await doubles.Handlers().Handle(new ListReportJobsQuery(Scope: "all", Sort: "secret"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await doubles.Handlers().Handle(new ListReportJobsQuery(Scope: "all", Status: "Pending", ReportType: "RequisitionPdf"), default)).Value.TotalItems.ShouldBe(1);
        (await doubles.Handlers().Handle(new CancelReportJobCommand(job.Id, job.Version), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);

        // Losing the data permission hides the requester's own job (RPT-002 re-checked on every read).
        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        (await doubles.Handlers().Handle(new GetReportJobQuery(job.Id), default)).IsSuccess.ShouldBeTrue();
        doubles.Procurement.Permissions.Revoke(doubles.Procurement.Requester.Id, Permissions.RequisitionsCreate);
        (await doubles.Handlers().Handle(new GetReportJobQuery(job.Id), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
    }

    [Fact]
    public async Task T_RPT_001_d_CancelNeedsIfMatch_AndOnlyPendingJobsCanBeCancelled()
    {
        var job = await RequestedRequisitionReportAsync();
        (await doubles.Handlers().Handle(new CancelReportJobCommand(job.Id, null), default)).Error!.Code.ShouldBe(ProblemCodes.PreconditionRequired);
        (await doubles.Handlers().Handle(new CancelReportJobCommand(job.Id, job.Version + 5), default)).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        var cancelled = await doubles.Handlers().Handle(new CancelReportJobCommand(job.Id, job.Version), default);
        cancelled.Value.Status.ShouldBe("Cancelled");
        cancelled.Value.AllowedActions.ShouldBe([ReportJobView.ActionRetry]);
        (await doubles.Handlers().Handle(new CancelReportJobCommand(job.Id, job.Version), default)).Value.Status.ShouldBe("Cancelled");
        doubles.Procurement.Audit.Records.Count(record => record.Action == ReportJobHandlers.CancelledAction).ShouldBe(1);

        var running = await RequestedRequisitionReportAsync(second: true);
        running.Claim("w1", ReportPolicy.V1, ProcurementTestDoubles.Now);
        (await doubles.Handlers().Handle(new CancelReportJobCommand(running.Id, running.Version), default)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await doubles.Handlers().Handle(new CancelReportJobCommand(Guid.NewGuid(), 1), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
    }

    [Fact]
    public async Task T_FIL_003_a_DownloadStreamsReadyOutputs_AuditsConfidentialFiles_AndExplainsOtherStates()
    {
        var job = await RequestedRequisitionReportAsync();
        var notReady = await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default);
        notReady.Error!.Code.ShouldBe(ProblemCodes.ReportNotReady);
        notReady.Error.Extensions!["currentState"].ShouldBe("Pending");

        (await doubles.Processor().ProcessNextAsync("w1", default)).Outcome.ShouldBe(Template.Application.Services.Reporting.ReportProcessingOutcome.Ready);
        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        var view = (await doubles.Handlers().Handle(new GetReportJobQuery(job.Id), default)).Value;
        view.Status.ShouldBe("Ready");
        view.ContentUrl.ShouldBe($"/api/v2/report-jobs/{job.Id}/content");
        view.MediaType.ShouldBe("application/pdf");
        view.FileName.ShouldBe($"requisicao-{doubles.Procurement.Requisitions.Items[0].Number}.pdf");

        var download = await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default);
        download.IsSuccess.ShouldBeTrue(download.Error?.Message);
        download.Value.MediaType.ShouldBe("application/pdf");
        using (var reader = new StreamReader(download.Value.Content))
            (await reader.ReadToEndAsync()).ShouldStartWith("%PDF-");
        var audit = doubles.Procurement.Audit.Records.Last();
        audit.Action.ShouldBe(ReportJobHandlers.DownloadedAction);
        audit.ResourceType.ShouldBe(ReportJobHandlers.FileResource);

        doubles.Storage.UnavailableOnRead = true;
        var outage = await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default);
        outage.Error!.Code.ShouldBe(ProblemCodes.FileUnavailable);
        outage.Error.Category.ShouldBe(ErrorCategory.Unavailable);
        doubles.Storage.UnavailableOnRead = false;
        doubles.Storage.Objects.Clear(); // database restored without blobs (NFR-REC-3)
        (await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default)).Error!.Code.ShouldBe(ProblemCodes.FileUnavailable);

        doubles.Procurement.Clock.UtcNow = job.ExpiresAtUtc!.Value;
        var expired = await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default);
        expired.Error!.Code.ShouldBe(ProblemCodes.ReportExpired);
        expired.Error.Category.ShouldBe(ErrorCategory.Gone);
        (await doubles.Handlers().Handle(new GetReportJobQuery(job.Id), default)).Value.Status.ShouldBe("Expired");
    }

    [Fact]
    public async Task T_FIL_003_b_DeletedFileMetadataIsUnavailable_AndInternalFilesAreNotAudited()
    {
        var job = await RequestedRequisitionReportAsync();
        await doubles.Processor().ProcessNextAsync("w1", default);
        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        var file = doubles.Files.Items.Single();
        file.MarkDeleted("manual", ProcurementTestDoubles.Now);
        (await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default)).Error!.Code.ShouldBe(ProblemCodes.FileUnavailable);

        var internalFile = StoredFile.CreatePending(Guid.NewGuid(), "ReportJob", job.Id, "x.pdf", "application/pdf", FileClassification.Internal, null,
            ProcurementTestDoubles.Now, ProcurementTestDoubles.Now.AddDays(7)).File!;
        internalFile.MarkAvailable(5, new string('a', 64), 100, ProcurementTestDoubles.Now);
        doubles.Files.Items.Add(internalFile);
        doubles.Storage.Objects[internalFile.Id] = "%PDF-"u8.ToArray();
        typeof(ReportJob).GetProperty(nameof(ReportJob.OutputFileId))!.SetValue(job, internalFile.Id);
        var before = doubles.Procurement.Audit.Records.Count;
        (await doubles.Handlers().Handle(new DownloadReportQuery(job.Id), default)).IsSuccess.ShouldBeTrue();
        doubles.Procurement.Audit.Records.Count.ShouldBe(before);
    }

    [Fact]
    public void T_RPT_005_f_ReportEventsHaveVersionedEnvelopes_AndNotifyTheRequesterOnly()
    {
        var requester = Guid.NewGuid();
        var job = Guid.NewGuid();
        var ready = ProcurementIntegrationEventContract.FromDomainEvent(
            new ReportReady(ProcurementTestDoubles.Now, job, ReportType.BudgetConsumptionXlsx, requester, Guid.NewGuid()), "trace");
        ready.AggregateType.ShouldBe(nameof(ReportJob));
        ready.Payload.GetProperty("reportType").GetString().ShouldBe("BudgetConsumptionXlsx");
        ready.Payload.TryGetProperty("outputFileId", out _).ShouldBeFalse(); // no storage detail travels in events
        var failed = ProcurementIntegrationEventContract.FromDomainEvent(
            new ReportFailed(ProcurementTestDoubles.Now, job, ReportType.RequisitionPdf, requester, ReportFailureCodes.TooLarge), "trace");
        failed.Payload.GetProperty("failureCode").GetString().ShouldBe(ReportFailureCodes.TooLarge);
        ProcurementIntegrationEventContract.Deserialize(ProcurementIntegrationEventContract.Serialize(failed)).EventType.ShouldBe("procurement.report.failed");

        var notifications = RfqNotificationPlanner.Plan(ready);
        notifications.Single().ShouldBe(new Template.Application.Interfaces.Messaging.NotificationRequest(
            RfqNotificationPlanner.UserRecipient, requester, RfqNotificationPlanner.ReportReadyTemplate, nameof(ReportJob), job));
        RfqNotificationPlanner.Plan(failed).Single().Template.ShouldBe(RfqNotificationPlanner.ReportFailedTemplate);
        ProcurementConsumers.NotificationEventTypes.ShouldContain("procurement.report.ready");
        ProcurementConsumers.NotificationEventTypes.ShouldNotContain("procurement.report.requested");
        var requested = ProcurementIntegrationEventContract.FromDomainEvent(new ReportRequested(ProcurementTestDoubles.Now, job, ReportType.RequisitionPdf, requester), "trace");
        Should.Throw<System.Text.Json.JsonException>(() => RfqNotificationPlanner.Plan(requested));
    }

    private async Task<ReportJob> RequestedRequisitionReportAsync(bool second = false)
    {
        var requisition = await doubles.SubmittedRequisitionAsync(price: second ? "999" : "1000");
        var result = await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: requisition.Id), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return doubles.Jobs.Items.Single(job => job.Id == result.Value.Job.Id);
    }
}
