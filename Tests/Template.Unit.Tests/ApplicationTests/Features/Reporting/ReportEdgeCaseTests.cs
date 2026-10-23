using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Reporting;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Security;
using Template.Application.Services.Reporting;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Files;
using Template.Domain.Reporting;
using Template.Unit.Tests.ApplicationTests.Features.Procurement;

namespace Template.Unit.Tests.ApplicationTests.Features.Reporting;

/// <summary>
/// Report edge cases: every catalogued status label, canonical parameter edge values, representation states, guard
/// paths of the request/cancel/list use cases, the Worker's failure classification and lease-loss paths, and builder
/// branches (incomplete comparisons, manual rates with waivers, submitted awards, several currencies, unknown labels,
/// withdrawn quotes). All data is synthetic.
/// </summary>
public sealed class ReportEdgeCaseTests
{
    private readonly ReportingTestDoubles doubles = new();

    [Theory]
    [InlineData("Draft", "Rascunho")]
    [InlineData("Submitted", "Submetida")]
    [InlineData("InSourcing", "Em cotação")]
    [InlineData("PendingApproval", "Aguardando aprovação")]
    [InlineData("Approved", "Aprovada")]
    [InlineData("Ordered", "Pedido emitido")]
    [InlineData("Closed", "Encerrada")]
    [InlineData("Rejected", "Rejeitada")]
    [InlineData("Cancelled", "Cancelada")]
    [InlineData("Expired", "Expirada")]
    [InlineData("Published", "Publicada")]
    [InlineData("AwardPending", "Adjudicação pendente")]
    [InlineData("Awarded", "Adjudicada")]
    [InlineData("Reserved", "Reservado")]
    [InlineData("Committed", "Comprometido")]
    [InlineData("Released", "Liberado")]
    [InlineData("Adjusted", "Ajustado")]
    [InlineData("Active", "Ativa")]
    [InlineData("Current", "Vigente")]
    [InlineData("Superseded", "Substituída")]
    [InlineData("Withdrawn", "Retirada")]
    [InlineData("Complete", "Completa")]
    [InlineData("Incomplete", "Incompleta")]
    [InlineData("Open", "Aberto")]
    [InlineData("Planned", "Planejado")]
    [InlineData("PendingSomething", "PendingSomething")]
    public void T_RPT_004_c_StatusLabelsAreLocalized(string status, string label) => ReportText.Status(status).ShouldBe(label);

    [Fact]
    public void T_RPT_003_g_ParameterSnapshotEdgeValues()
    {
        new ReportParameters().ToCanonicalJson().ShouldBe("{}");
        ReportParameters.Parse("{}").ShouldBe(new ReportParameters());
        Should.Throw<System.Text.Json.JsonException>(() => ReportParameters.Parse("null"));
        foreach (var single in new[]
                 {
                     new ReportParameters(ComparisonSequence: 3), new ReportParameters(CostCenterId: Guid.NewGuid()), new ReportParameters(Currency: "USD"),
                     new ReportParameters(FiscalPeriodId: Guid.NewGuid()), new ReportParameters(From: new DateOnly(2027, 1, 2)),
                     new ReportParameters(RequisitionId: Guid.NewGuid()), new ReportParameters(RequisitionVersion: 9), new ReportParameters(RfqId: Guid.NewGuid()),
                     new ReportParameters(SupplierId: Guid.NewGuid()), new ReportParameters(To: new DateOnly(2027, 2, 3))
                 })
            ReportParameters.Parse(single.ToCanonicalJson()).ShouldBe(single);
        foreach (var type in Enum.GetValues<ReportType>())
        {
            ReportAccess.DataPermissions(type).ShouldNotBeEmpty();
            ReportAccess.ParameterNames(type).ShouldNotBeEmpty();
            ReportAccess.AnyOfDataPermissions(type).ShouldBe(type == ReportType.RequisitionPdf);
        }
    }

    [Fact]
    public void T_RPT_006_c_ViewStates_ReflectRetentionAndTheActor()
    {
        var job = Job();
        ReportJobView.From(job, Now, mayAct: false).AllowedActions.ShouldBeEmpty();
        job.Claim("w", ReportPolicy.V1, Now);
        var running = ReportJobView.From(job, Now, mayAct: true);
        running.AllowedActions.ShouldBeEmpty();
        running.MediaType.ShouldBeNull();
        job.Complete("w", Guid.NewGuid(), "x.pdf", 10, ReportPolicy.V1, Now);
        var past = ReportJobView.From(job, Now.AddDays(8), mayAct: true);
        past.Status.ShouldBe("Expired"); // Ready in storage, past retention on the wire
        past.ContentUrl.ShouldBeNull();
        past.AllowedActions.ShouldBe([ReportJobView.ActionRetry]);
        ReportJobView.From(job, Now, mayAct: false).AllowedActions.ShouldBe([ReportJobView.ActionDownload]);
    }

    [Fact]
    public async Task T_RPT_002_d_SystemActorCannotRequest_AndListNeedsAReportPermission()
    {
        doubles.Procurement.ActAsSystem();
        (await doubles.Handlers().Handle(new RequestReportCommand("RequisitionPdf", RequisitionId: Guid.NewGuid()), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        var stranger = new ProcurementTestDoubles.Actor();
        doubles.Procurement.Permissions.Grant(stranger.Id, [Permissions.OrganizationRead]);
        doubles.Procurement.ActAs(stranger);
        (await doubles.Handlers().Handle(new ListReportJobsQuery(), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        (await doubles.Handlers().Handle(new ListReportTypesQuery(), default)).Value.ShouldAllBe(type => !type.Available);
        (await doubles.Handlers().Handle(new GetReportJobQuery(Guid.NewGuid()), default)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
    }

    [Fact]
    public async Task T_RPT_001_h_UniqueRaceWithoutAVisibleWinner_AndCancelConflicts_AnswerPreconditionFailed()
    {
        doubles.Procurement.ActAs(doubles.Controller);
        doubles.Procurement.UnitOfWork.UniqueViolation = IReportJobRepository.ActiveUniqueKey;
        doubles.Procurement.UnitOfWork.BeforeSave = () => doubles.Jobs.Items.Clear();
        var race = await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: doubles.Procurement.Period.Id), default);
        race.Error!.Category.ShouldBe(ErrorCategory.PreconditionFailed);
        doubles.Procurement.UnitOfWork.UniqueViolation = null;
        doubles.Procurement.UnitOfWork.BeforeSave = null;

        var job = (await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: doubles.Procurement.Period.Id), default)).Value.Job;
        doubles.Procurement.UnitOfWork.ThrowConflict = true;
        var conflict = await doubles.Handlers().Handle(new CancelReportJobCommand(job.Id, long.Parse(job.Version)), default);
        conflict.Error!.Category.ShouldBe(ErrorCategory.PreconditionFailed);
    }

    [Fact]
    public async Task T_RPT_005_q_FailureClassification_OfUnexpectedTimeoutAndOversizedOutputs()
    {
        var job = await RequestBudgetAsync();
        doubles.Renderer.Failure = new InvalidOperationException("synthetic bug");
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.RetryScheduled);
        job.FailureCode.ShouldBe(ReportFailureCodes.Unexpected);

        doubles.Procurement.Clock.UtcNow = job.NextAttemptAtUtc;
        doubles.Renderer.Failure = new TimeoutException("synthetic");
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.RetryScheduled);
        job.FailureCode.ShouldBe(ReportFailureCodes.Timeout);

        doubles.Procurement.Clock.UtcNow = job.NextAttemptAtUtc;
        doubles.Renderer.Failure = new FileContentTooLargeException(10);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        job.FailureCode.ShouldBe(ReportFailureCodes.TooLarge);

        var oversized = await RequestBudgetAsync(Guid.NewGuid());
        doubles.Renderer.Failure = null;
        doubles.Renderer.Output = [0x50, 0x4B, 0x03, 0x04, .. new byte[FileRetentionPolicy.V1.MaxReportOutputBytes]];
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        oversized.FailureCode.ShouldBe(ReportFailureCodes.TooLarge);
        doubles.Storage.Objects.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_RPT_005_r_StaleExhaustedJob_FailsAtClaim_WithoutRendering()
    {
        var job = await RequestBudgetAsync();
        for (var attempt = 0; attempt < 3; attempt++)
            job.Claim($"crashed-{attempt}", ReportPolicy.V1, ProcurementTestDoubles.Now.AddHours(attempt)).Claimed.ShouldBeTrue();
        doubles.Procurement.Clock.UtcNow = ProcurementTestDoubles.Now.AddHours(5);

        (await doubles.Processor().ProcessNextAsync("survivor", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        job.FailureCode.ShouldBe(ReportFailureCodes.AttemptsExhausted);
        doubles.Renderer.Workbooks.ShouldBeEmpty();
        doubles.Procurement.Outbox.Events.ShouldContain(envelope => envelope.EventType == "procurement.report.failed");
    }

    [Fact]
    public async Task T_RPT_005_s_LeaseLostAtStoring_AndJobsVanishingOrTakenOverDuringFailureOrShutdown()
    {
        var first = await RequestBudgetAsync();
        doubles.Renderer.DuringRender = () => first.Claim("other", ReportPolicy.V1, first.ClaimExpiresAtUtc!.Value);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.LeaseLost);
        doubles.Files.Items.ShouldBeEmpty(); // lost before any output metadata

        var second = await RequestBudgetAsync(Guid.NewGuid());
        doubles.Renderer.DuringRender = () => doubles.Jobs.Items.Remove(second);
        doubles.Renderer.Failure = new ReportRendererUnavailableException("crash");
        doubles.Procurement.Clock.UtcNow = first.ClaimExpiresAtUtc!.Value.AddDays(1); // first stays Running with an old lease
        doubles.Jobs.Items.Remove(first);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.LeaseLost);

        var third = await RequestBudgetAsync(Guid.NewGuid());
        doubles.Renderer.DuringRender = () => third.Claim("other", ReportPolicy.V1, third.ClaimExpiresAtUtc!.Value);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.LeaseLost);
        third.ClaimOwner.ShouldBe("other");

        var fourth = await RequestBudgetAsync(Guid.NewGuid());
        using var stopping = new CancellationTokenSource();
        doubles.Renderer.Failure = null;
        doubles.Renderer.DuringRender = () =>
        {
            fourth.Claim("other", ReportPolicy.V1, fourth.ClaimExpiresAtUtc!.Value);
            stopping.Cancel();
        };
        (await doubles.Processor().ProcessNextAsync("w", stopping.Token)).Outcome.ShouldBe(ReportProcessingOutcome.LeaseLost);
        fourth.ClaimOwner.ShouldBe("other");
    }

    [Fact]
    public async Task T_RPT_003_h_ComparisonPdf_IncompleteThenManualRateWithWaiver_ThenSubmittedAward()
    {
        var (rfq, _) = await doubles.ClosedRfqAsync(firstCurrency: "USD");
        rfq.CurrentComparison!.Status.ShouldBe(Template.Domain.Sourcing.ComparisonStatus.Incomplete);
        await RequestComparisonAsync(rfq.Id);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var incomplete = doubles.Renderer.Documents.Last();
        var ranking = incomplete.Sections.Single(section => section.Heading == "Classificação");
        ranking.Table!.Rows.ShouldContain(row => row[6] == "sem taxa" && row[5] == "—" && row[0] == "—");
        ranking.Notes!.Last().ShouldStartWith("Sem recomendação");
        incomplete.Sections.Single(section => section.Heading == "Taxas de câmbio utilizadas").EmptyMessage.ShouldNotBeNull();

        var manager = new ProcurementTestDoubles.Actor();
        doubles.Procurement.Permissions.Grant(manager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        doubles.Procurement.ActAs(manager);
        var rated = await doubles.Procurement.RfqCommands().Handle(new RecordManualExchangeRateCommand(rfq.Id, rfq.Version, "USD", "5.4321",
            ProcurementTestDoubles.Today.AddDays(2), "Synthetic manual rate for the report test"), default);
        rated.IsSuccess.ShouldBeTrue(rated.Error?.Message);
        var leading = rated.Value.Comparison!.RecommendedQuoteRevisionId!.Value;
        var awarded = await doubles.Procurement.RfqCommands().Handle(new SubmitAwardCommand(rfq.Id, rfq.Version, leading, "Best total", null, null), default);
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);

        doubles.Procurement.ActAs(doubles.Buyer);
        await RequestComparisonAsync(rfq.Id);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var complete = doubles.Renderer.Documents.Last();
        complete.Sections.Single(section => section.Heading == "Taxas de câmbio utilizadas").Table!.Rows.Single()[2].ShouldBe("Manual");
        complete.Sections.Single(section => section.Heading == "Exceções aprovadas (waivers)").Table!.Rows.ShouldNotBeEmpty();
        complete.Sections.Single(section => section.Heading == "Seleção submetida").Table!.Rows.Single()[6].ShouldBe("Aguardando aprovação");
    }

    [Fact]
    public async Task T_RPT_003_i_BudgetXlsx_SeveralCurrencies_UnknownLabels_AndSupplierQuotesWithAWithdrawal()
    {
        var usd = BudgetAllocation.Create(Guid.NewGuid(), doubles.Procurement.Period, new MasterDataReference(Guid.NewGuid(), true), "USD", true, 500m, null,
            ProcurementTestDoubles.Now.AddHours(-1)).Allocation!;
        doubles.Procurement.Allocations.Items.Add(usd);
        await RequestBudgetAsync();
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var summary = doubles.Renderer.Workbooks.Last().Sheets[0];
        summary.Totals.ShouldBeNull(); // never a total across currencies
        summary.Rows.Count.ShouldBe(2);
        summary.Rows.ShouldContain(row => row[0].Text == usd.CostCenterId.ToString("D") && row[1].Text == string.Empty);
        summary.Context.Single(field => field.Label == "Moeda").Value.ShouldStartWith("todas");

        var (rfq, suppliers) = await doubles.ClosedRfqAsync();
        doubles.Procurement.ActAs(doubles.Buyer);
        await doubles.Handlers().Handle(new RequestReportCommand("SupplierQuotesXlsx", From: new DateOnly(2027, 3, 1), To: new DateOnly(2027, 3, 1)), default);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var performance = doubles.Renderer.Workbooks.Last().Sheets.Single(sheet => sheet.Name == "Desempenho");
        performance.Rows.Count.ShouldBe(suppliers.Length);
        rfq.Quotes.Count.ShouldBe(3);
    }

    private static readonly DateTime Now = ProcurementTestDoubles.Now;

    private static ReportJob Job()
    {
        var json = new ReportParameters(FiscalPeriodId: Guid.NewGuid()).ToCanonicalJson();
        return ReportJob.Request(Guid.NewGuid(), ReportType.BudgetConsumptionXlsx, Guid.NewGuid(), json,
            ReportParameters.Fingerprint(ReportType.BudgetConsumptionXlsx, json), Now, "America/Sao_Paulo", ReportPolicy.V1, Now).Job!;
    }

    private async Task<ReportJob> RequestBudgetAsync(Guid? costCenterId = null)
    {
        doubles.Procurement.ActAs(doubles.Controller);
        var result = await doubles.Handlers().Handle(new RequestReportCommand("BudgetConsumptionXlsx", FiscalPeriodId: doubles.Procurement.Period.Id, CostCenterId: costCenterId), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return doubles.Jobs.Items.Single(job => job.Id == result.Value.Job.Id);
    }

    private async Task RequestComparisonAsync(Guid rfqId)
    {
        doubles.Procurement.ActAs(doubles.Buyer);
        var result = await doubles.Handlers().Handle(new RequestReportCommand("QuoteComparisonPdf", RfqId: rfqId), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
    }
}
