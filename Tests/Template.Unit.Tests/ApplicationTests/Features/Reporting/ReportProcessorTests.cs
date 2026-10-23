using Shouldly;
using Template.Application.Features.Reporting;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Reporting;
using Template.Application.Services.Reporting;
using Template.Domain.Budgeting;
using Template.Domain.Files;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Unit.Tests.ApplicationTests.Features.Procurement;

namespace Template.Unit.Tests.ApplicationTests.Features.Reporting;

/// <summary>
/// Worker report attempts against port fakes: RPT-003 as-of consistency from immutable snapshots, RPT-004 context,
/// RPT-005 retries, permanent failures, lease loss and graceful release, FIL-001/002 verified storage and FIL-005
/// retention. All data is synthetic.
/// </summary>
public sealed class ReportProcessorTests
{
    private readonly ReportingTestDoubles doubles = new();

    [Fact]
    public async Task T_RPT_005_g_HappyPath_StoresVerifiedConfidentialOutput_AndCompletesWithOneEvent()
    {
        var job = await RequestAsync(ReportType.RequisitionPdf);

        var (outcome, jobId) = await doubles.Processor().ProcessNextAsync("worker-a", default);

        outcome.ShouldBe(ReportProcessingOutcome.Ready);
        jobId.ShouldBe(job.Id);
        job.Status.ShouldBe(ReportJobStatus.Ready);
        job.AttemptCount.ShouldBe(1);
        var file = doubles.Files.Items.Single();
        file.Status.ShouldBe(StoredFileStatus.Available);
        file.Classification.ShouldBe(FileClassification.Confidential);
        file.OwnerType.ShouldBe(ReportJobProcessor.ReportOwnerType);
        file.OwnerId.ShouldBe(job.Id);
        file.ExpiresAtUtc!.Value.ShouldBe(job.ExpiresAtUtc!.Value, TimeSpan.FromSeconds(1));
        file.Sha256.ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(doubles.Storage.Objects[file.Id])));
        job.OutputFileId.ShouldBe(file.Id);
        doubles.Procurement.Outbox.Events.Count(envelope => envelope.EventType == "procurement.report.ready").ShouldBe(1);
        doubles.Procurement.Audit.Records.Select(record => record.Action).ShouldContain(ReportJobProcessor.ReadyAction);
        doubles.Procurement.Audit.Records.Select(record => record.Action).ShouldContain(ReportJobProcessor.StoredAction);
        (await doubles.Processor().ProcessNextAsync("worker-a", default)).Outcome.ShouldBe(ReportProcessingOutcome.Idle);
    }

    [Fact]
    public async Task T_RPT_005_h_TransientRendererFailures_RetryThenFail_WithoutOutputs()
    {
        var job = await RequestAsync(ReportType.RequisitionPdf);
        doubles.Renderer.Failure = new ReportRendererUnavailableException("synthetic browser crash");

        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.RetryScheduled);
        job.Status.ShouldBe(ReportJobStatus.Pending);
        job.FailureCode.ShouldBe(ReportFailureCodes.RendererUnavailable);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Idle); // back-off
        doubles.Procurement.Clock.UtcNow = job.NextAttemptAtUtc;
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.RetryScheduled);
        doubles.Procurement.Clock.UtcNow = job.NextAttemptAtUtc;
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        job.Status.ShouldBe(ReportJobStatus.Failed);
        job.FailureCode.ShouldBe(ReportFailureCodes.AttemptsExhausted);
        job.AttemptCount.ShouldBe(3);
        doubles.Storage.Objects.ShouldBeEmpty();
        doubles.Procurement.Outbox.Events.Count(envelope => envelope.EventType == "procurement.report.failed").ShouldBe(1);
    }

    [Fact]
    public async Task T_RPT_005_i_StorageOutage_RetriesWithoutCorruptingState_AndRecovers()
    {
        var requisition = await doubles.SubmittedRequisitionAsync();
        var versionBefore = requisition.Version;
        var job = await RequestAsync(ReportType.RequisitionPdf, requisition);
        doubles.Storage.Unavailable = true;

        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.RetryScheduled);
        job.FailureCode.ShouldBe(ReportFailureCodes.StorageUnavailable);
        doubles.Files.Items.Single().Status.ShouldBe(StoredFileStatus.Pending); // left for FIL-005 cleanup (delete also failed)
        requisition.Version.ShouldBe(versionBefore);
        requisition.Status.ShouldBe(RequisitionStatus.Submitted);

        doubles.Storage.Unavailable = false;
        doubles.Procurement.Clock.UtcNow = job.NextAttemptAtUtc;
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        doubles.Files.Items.Count(file => file.Status == StoredFileStatus.Available).ShouldBe(1);

        // FIL-005: the orphan Pending row of the failed attempt is selected after 24 h and removed.
        doubles.Procurement.Clock.UtcNow = ProcurementTestDoubles.Now.AddHours(25);
        var maintenance = await doubles.Maintenance().RunAsync(100, default);
        maintenance.DeletedFiles.ShouldBe(1);
        doubles.Files.Items.Count(file => file.Status == StoredFileStatus.Deleted).ShouldBe(1);
        job.Status.ShouldBe(ReportJobStatus.Ready);
    }

    [Fact]
    public async Task T_FIL_002_d_DigestMismatchOnReadBack_InvalidSignature_OversizeAndInfectedOutputsNeverBecomeReady()
    {
        var job = await RequestAsync(ReportType.RequisitionPdf);
        doubles.Storage.CorruptReads = true;
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.RetryScheduled);
        job.FailureCode.ShouldBe(ReportFailureCodes.StorageUnavailable);
        doubles.Files.Items.Single().Status.ShouldBe(StoredFileStatus.Deleted);
        doubles.Storage.Objects.ShouldBeEmpty();
        doubles.Storage.CorruptReads = false;

        doubles.Renderer.Output = "<html>not a pdf</html>"u8.ToArray();
        doubles.Procurement.Clock.UtcNow = job.NextAttemptAtUtc;
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        job.FailureCode.ShouldBe(ReportFailureCodes.OutputInvalid);

        var infected = await RequestAsync(ReportType.BudgetConsumptionXlsx, actor: doubles.Controller);
        doubles.Renderer.Output = null;
        doubles.ScanVerdict = MalwareScanVerdict.Infected;
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        infected.FailureCode.ShouldBe(ReportFailureCodes.OutputInvalid);
        doubles.Storage.Objects.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_RPT_005_j_LostLease_DiscardsTheOutput_AndTheNewClaimantFinishes()
    {
        var job = await RequestAsync(ReportType.RequisitionPdf);
        doubles.Storage.OnWrite = () =>
        {
            // While worker A writes, its lease expires and worker B takes the job over.
            doubles.Storage.OnWrite = null;
            job.Claim("worker-b", ReportPolicy.V1, job.ClaimExpiresAtUtc!.Value);
        };

        (await doubles.Processor().ProcessNextAsync("worker-a", default)).Outcome.ShouldBe(ReportProcessingOutcome.LeaseLost);
        job.Status.ShouldBe(ReportJobStatus.Running);
        job.ClaimOwner.ShouldBe("worker-b");
        doubles.Storage.Objects.ShouldBeEmpty();
        doubles.Files.Items.Single().Status.ShouldBe(StoredFileStatus.Deleted);
        doubles.Procurement.Outbox.Events.ShouldNotContain(envelope => envelope.EventType == "procurement.report.ready");
    }

    [Fact]
    public async Task T_RPT_005_k_ShutdownDuringAnAttempt_ReleasesTheJobWithoutConsumingTheAttempt()
    {
        var job = await RequestAsync(ReportType.RequisitionPdf);
        using var stopping = new CancellationTokenSource();
        doubles.Renderer.DuringRender = stopping.Cancel;

        (await doubles.Processor().ProcessNextAsync("w", stopping.Token)).Outcome.ShouldBe(ReportProcessingOutcome.Released);
        job.Status.ShouldBe(ReportJobStatus.Pending);
        job.AttemptCount.ShouldBe(0);
        doubles.Renderer.DuringRender = null;
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        job.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task T_RPT_003_a_RequisitionPdf_UsesTheSubmittedSnapshotAtAsOf_EvenAfterLaterEdits()
    {
        var requisition = await doubles.SubmittedRequisitionAsync("18", "1000");
        var job = await RequestAsync(ReportType.RequisitionPdf, requisition);

        // After the request: withdraw, edit the draft and change master data. The report must not move.
        doubles.Procurement.Clock.UtcNow = ProcurementTestDoubles.Now.AddHours(1);
        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        (await doubles.Procurement.Transitions().Handle(new WithdrawRequisitionCommand(requisition.Id, requisition.Version), default)).IsSuccess.ShouldBeTrue();
        (await doubles.Procurement.Drafts().Handle(new RemoveRequisitionLineCommand(requisition.Id, requisition.Version, 1), default)).IsSuccess.ShouldBeTrue();

        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var document = doubles.Renderer.Documents.Single();
        document.Title.ShouldBe("Requisição de compra");
        document.Subtitle.ShouldBe(requisition.Number);
        Field(document, "Status na posição").ShouldBe("Submetida");
        Field(document, "Conteúdo").ShouldStartWith("Revisão 1 submetida");
        Field(document, "Moeda").ShouldBe("BRL");
        Field(document, "Fuso horário").ShouldBe("America/Sao_Paulo");
        Field(document, "Posição em (as-of)").ShouldStartWith("01/03/2027 10:00:00");
        Field(document, "Gerado em").ShouldStartWith("01/03/2027 11:00:00");
        Field(document, "Modelo").ShouldBe("requisition-pdf.v1");
        var lines = document.Sections.Single(section => section.Heading == "Itens").Table!;
        lines.Rows.Single()[1].ShouldBe("Laptop 14 inch");
        lines.Totals![^1].ShouldBe("18.000,00 BRL");
        var history = document.Sections.Single(section => section.Heading == "Histórico até a posição").Table!;
        history.Rows.Select(row => row[2]).ShouldBe(["Rascunho", "Submetida"]); // the withdrawal is after the as-of instant
    }

    [Fact]
    public async Task T_RPT_003_b_UnsubmittedDraft_ChangedAfterTheRequest_FailsWithSourceChanged()
    {
        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        var created = await doubles.Procurement.Drafts().Handle(new CreateRequisitionDraftCommand(
            doubles.Procurement.CostCenter.Id, "BRL", "Draft for a report snapshot test", ProcurementTestDoubles.Today.AddDays(10)), default);
        var draft = doubles.Procurement.Requisitions.Items.Single(item => item.Id == created.Value.Id);
        var job = await RequestAsync(ReportType.RequisitionPdf, draft);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        Field(doubles.Renderer.Documents.Single(), "Submetida em").ShouldBe("Não submetida");

        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        var second = await RequestAsync(ReportType.RequisitionPdf, draft, reuseAllowed: true);
        (await doubles.Procurement.Drafts().Handle(new UpdateRequisitionDraftCommand(draft.Id, draft.Version, doubles.Procurement.CostCenter.Id, "BRL",
            "Draft changed after the report request", ProcurementTestDoubles.Today.AddDays(11)), default)).IsSuccess.ShouldBeTrue();
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        second.FailureCode.ShouldBe(ReportFailureCodes.SourceChanged);
        job.Status.ShouldBe(ReportJobStatus.Ready);
    }

    [Fact]
    public async Task T_RPT_003_c_QuoteComparisonPdf_RendersThePinnedComparisonWithSnapshotNames()
    {
        var (rfq, suppliers) = await doubles.ClosedRfqAsync();
        var job = await RequestAsync(ReportType.QuoteComparisonPdf, rfq: rfq.Id, actor: doubles.Buyer);
        doubles.Procurement.Suppliers.Items[suppliers[1].SupplierId] = suppliers[1] with { LegalName = "Renamed after close Ltda" };
        doubles.Procurement.Suppliers.SetStatus(suppliers[1].SupplierId, "Suspended"); // master data changes later

        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var document = doubles.Renderer.Documents.Single();
        document.Subtitle.ShouldBe($"{rfq.Number} · rodada 1");
        var ranking = document.Sections.Single(section => section.Heading == "Classificação");
        ranking.Table!.Rows.Select(row => row[0]).ShouldBe(["1", "2", "3"]);
        ranking.Table.Rows[0][1].ShouldBe("Supplier 1");
        ranking.Table.Rows[0][6].ShouldBe("16.200,00 BRL");
        ranking.Notes!.Last().ShouldBe("Recomendação (consultiva, 1º lugar): Supplier 1");
        Field(document, "Moeda base").ShouldBe("BRL");
        Field(document, "Política de cotação").ShouldBe("SourcingPolicy v1");
        Field(document, "Comparação").ShouldStartWith("nº 1 · Completa");
        document.Sections.Single(section => section.Heading == "Seleção submetida").EmptyMessage.ShouldNotBeNull();
        job.Status.ShouldBe(ReportJobStatus.Ready);
    }

    [Fact]
    public async Task T_RPT_003_d_BudgetXlsx_ReconstructsAmountsAtAsOf_IgnoringLaterChanges()
    {
        var requisition = await doubles.SubmittedRequisitionAsync("10", "1000"); // reserves 10,000.00 BRL
        var job = await RequestAsync(ReportType.BudgetConsumptionXlsx, actor: doubles.Controller);

        // After the as-of instant: the amount changes and the requisition is cancelled (reservation released).
        doubles.Procurement.Clock.UtcNow = ProcurementTestDoubles.Now.AddHours(2);
        doubles.Procurement.Allocation.ChangeAmount(250_000m, "Later reinforcement", null, doubles.Procurement.Clock.UtcNow);
        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        (await doubles.Procurement.Transitions().Handle(new CancelRequisitionCommand(requisition.Id, requisition.Version, "No longer needed after review"), default))
            .IsSuccess.ShouldBeTrue();

        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var workbook = doubles.Renderer.Workbooks.Single();
        workbook.Sheets.Select(sheet => sheet.Name).ShouldBe(["Resumo", "Reservas", "Movimentos"]);
        var summary = workbook.Sheets[0];
        var row = summary.Rows.Single();
        row[0].Text.ShouldBe("CC-OPS");
        row[3].Number.ShouldBe(100_000m);   // amount at as-of, not 250,000.00
        row[4].Number.ShouldBe(10_000m);    // still reserved at as-of
        row[6].Number.ShouldBe(90_000m);    // CALC-003
        row[7].Number.ShouldBe(0.1m);
        summary.Totals![3].Number.ShouldBe(100_000m);
        summary.Context.Single(field => field.Label == "Moeda").Value.ShouldBe("BRL");
        summary.Context.Single(field => field.Label == "Período fiscal").Value.ShouldStartWith("FY27");
        workbook.Sheets[1].Rows.Single()[5].Text.ShouldBe("Reservado");
        var movements = workbook.Sheets[2].Rows.Select(movement => movement[4].Text).ToArray();
        movements.Count(operation => operation == "Reservado").ShouldBe(1);
        movements.Count(operation => operation == "Valor alterado")
            .ShouldBe(doubles.Procurement.Allocation.AmountHistory.Count(change => change.AtUtc <= ProcurementTestDoubles.Now));
        movements.ShouldNotContain("Liberado");

        // The live aggregate moved on; the report did not.
        doubles.Procurement.Allocation.Amount.ShouldBe(250_000m);
        doubles.Procurement.Allocation.ReservedAmount.ShouldBe(0m);
        var asOfNow = BudgetConsumptionXlsxBuilder.AsOf(doubles.Procurement.Allocation, doubles.Procurement.Clock.UtcNow);
        asOfNow.Amount.ShouldBe(250_000m);
        asOfNow.Reserved.ShouldBe(0m);
        job.Status.ShouldBe(ReportJobStatus.Ready);
    }

    [Fact]
    public async Task T_RPT_003_e_SupplierQuotesXlsx_ListsRevisionsInTheWindow_WithMetricsAndUserTextMarked()
    {
        var (rfq, suppliers) = await doubles.ClosedRfqAsync();
        var job = await RequestAsync(ReportType.SupplierQuotesXlsx, actor: doubles.Buyer, from: new DateOnly(2027, 3, 1), to: new DateOnly(2027, 3, 1));

        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        var workbook = doubles.Renderer.Workbooks.Single();
        var quotes = workbook.Sheets.Single(sheet => sheet.Name == "Cotacoes");
        quotes.Rows.Count.ShouldBe(3);
        quotes.Rows.ShouldAllBe(row => row[0].Text == rfq.Number);
        var paymentTerms = quotes.Rows[0][17];
        paymentTerms.UserControlled.ShouldBeTrue();
        paymentTerms.Text.ShouldStartWith("=HYPERLINK");
        quotes.Rows.Select(row => row[20].Number).OrderBy(rank => rank).ShouldBe([1m, 2m, 3m]);
        var performance = workbook.Sheets.Single(sheet => sheet.Name == "Desempenho");
        performance.Rows.Count.ShouldBe(3);
        performance.Rows.ShouldAllBe(row => row[2].Number == 1m && row[3].Number == 1m && row[4].Number == 1m);
        performance.Rows.Single(row => row[0].Text == "Supplier 1")[7].Number.ShouldBe(1m);
        quotes.Context.Single(field => field.Label == "Janela de recebimento").Value.ShouldBe("01/03/2027 a 01/03/2027 (datas locais)");

        // A window without quotes yields an empty but valid workbook; a supplier filter keeps only that supplier.
        var other = await RequestAsync(ReportType.SupplierQuotesXlsx, actor: doubles.Buyer, from: new DateOnly(2027, 1, 1), to: new DateOnly(2027, 1, 31));
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        doubles.Renderer.Workbooks.Last().Sheets[0].Rows.ShouldBeEmpty();
        var filtered = await RequestAsync(ReportType.SupplierQuotesXlsx, actor: doubles.Buyer, from: new DateOnly(2027, 3, 1), to: new DateOnly(2027, 3, 2),
            supplier: suppliers[0].SupplierId);
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Ready);
        doubles.Renderer.Workbooks.Last().Sheets[1].Rows.Single()[0].Text.ShouldBe("Supplier 0");
        new[] { job, other, filtered }.ShouldAllBe(item => item.Status == ReportJobStatus.Ready);
    }

    [Fact]
    public async Task T_RPT_003_f_OversizedSourcesFailPermanently_AndMissingSourcesToo()
    {
        var job = await RequestAsync(ReportType.QuoteComparisonPdf, rfq: (await doubles.ClosedRfqAsync()).Rfq.Id, actor: doubles.Buyer);
        doubles.Procurement.Rfqs.Items.Clear();
        (await doubles.Processor().ProcessNextAsync("w", default)).Outcome.ShouldBe(ReportProcessingOutcome.Failed);
        job.FailureCode.ShouldBe(ReportFailureCodes.SourceNotFound);

        var builder = new BudgetConsumptionXlsxBuilder(doubles.Sources);
        var budgetJob = await RequestAsync(ReportType.BudgetConsumptionXlsx, actor: doubles.Controller);
        var tiny = ReportPolicy.V1 with { MaxRows = 0 };
        var context = new ReportBuildContext(budgetJob, ReportParameters.Parse(budgetJob.ParametersJson), ProcurementTestDoubles.Now,
            TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"), tiny);
        (await Should.ThrowAsync<ReportSourceException>(() => builder.BuildAsync(context, default))).FailureCode.ShouldBe(ReportFailureCodes.TooLarge);
        Should.Throw<ArgumentException>(() => new ReportSourceException("free text", "x"));
    }

    [Fact]
    public async Task T_RPT_006_b_MaintenanceExpiresReadyJobsAfterRetention_AndDeletesTheirOutputs()
    {
        var job = await RequestAsync(ReportType.RequisitionPdf);
        await doubles.Processor().ProcessNextAsync("w", default);
        var file = doubles.Files.Items.Single();

        doubles.Procurement.Clock.UtcNow = job.ExpiresAtUtc!.Value.AddSeconds(-1);
        (await doubles.Maintenance().RunAsync(10, default)).ShouldBe(new ReportMaintenanceResult(0, 0, 0));

        doubles.Procurement.Clock.UtcNow = job.ExpiresAtUtc.Value.AddMinutes(1);
        doubles.Storage.Unavailable = true;
        var deferred = await doubles.Maintenance().RunAsync(10, default);
        deferred.ExpiredJobs.ShouldBe(1);
        deferred.FailedDeletions.ShouldBe(1);
        file.Status.ShouldBe(StoredFileStatus.Available);

        doubles.Storage.Unavailable = false;
        (await doubles.Maintenance().RunAsync(10, default)).DeletedFiles.ShouldBe(1);
        job.Status.ShouldBe(ReportJobStatus.Expired);
        file.Status.ShouldBe(StoredFileStatus.Deleted);
        doubles.Storage.Objects.ShouldBeEmpty();
        doubles.Procurement.Audit.Records.Select(record => record.Action).ShouldContain(ReportMaintenance.ExpiredAction);
        doubles.Procurement.Audit.Records.Select(record => record.Action).ShouldContain(ReportMaintenance.FileExpiredAction);
    }

    [Fact]
    public void T_RPT_004_a_FormattingUsesTheOrganizationLocaleAndSafeFileNames()
    {
        ReportText.Money(1234567.5m, "BRL").ShouldBe("1.234.567,50 BRL");
        ReportText.Decimal(2.5m).ShouldBe("2,5");
        ReportText.Decimal(3m).ShouldBe("3");
        ReportText.Rate(5.4321m).ShouldBe("5,432100");
        ReportText.Date(new DateOnly(2027, 3, 1)).ShouldBe("01/03/2027");
        ReportText.FileName(ReportType.RequisitionPdf, "requisição", "REQ/2027:..\\x").ShouldBe("requisicao-REQ-2027-..-x.pdf");
        ReportText.FileName(ReportType.SupplierQuotesXlsx, "", "  ").ShouldBe("relatorio.xlsx");
        ReportText.Status("Unknown").ShouldBe("Unknown");
        new ReportTable([new("A")], [["1", "2"]]).Invoking(table => table.Validate()).ShouldThrow<ArgumentException>();
        new WorkbookSheet("bad/name", [], [new("A")], []).Invoking(sheet => sheet.Validate()).ShouldThrow<ArgumentException>();
        new WorkbookSheet("ok", [], [new("A")], [[WorkbookCell.Integer(1), WorkbookCell.Empty]]).Invoking(sheet => sheet.Validate()).ShouldThrow<ArgumentException>();
    }

    private async Task<ReportJob> RequestAsync(
        ReportType type, PurchaseRequisition? requisition = null, Guid? rfq = null, ProcurementTestDoubles.Actor? actor = null,
        DateOnly? from = null, DateOnly? to = null, Guid? supplier = null, bool reuseAllowed = false)
    {
        if (type == ReportType.RequisitionPdf && requisition is null)
            requisition = await doubles.SubmittedRequisitionAsync();
        doubles.Procurement.ActAs(actor ?? doubles.Procurement.Requester);
        var command = type switch
        {
            ReportType.RequisitionPdf => new RequestReportCommand(type.ToString(), RequisitionId: requisition!.Id),
            ReportType.QuoteComparisonPdf => new RequestReportCommand(type.ToString(), RfqId: rfq),
            ReportType.BudgetConsumptionXlsx => new RequestReportCommand(type.ToString(), FiscalPeriodId: doubles.Procurement.Period.Id),
            _ => new RequestReportCommand(type.ToString(), From: from, To: to, SupplierId: supplier)
        };
        var result = await doubles.Handlers().Handle(command, default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        if (!reuseAllowed)
            result.Value.Reused.ShouldBeFalse();
        return doubles.Jobs.Items.Single(job => job.Id == result.Value.Job.Id);
    }

    private static string Field(ReportDocument document, string label) =>
        document.Context.Concat(document.Sections.SelectMany(section => section.Fields ?? [])).First(field => field.Label == label).Value;
}

internal static class InvokingExtensions
{
    public static Action Invoking<T>(this T subject, Action<T> action) => () => action(subject);

    public static void ShouldThrow<TException>(this Action action) where TException : Exception => Should.Throw<TException>(action);
}
