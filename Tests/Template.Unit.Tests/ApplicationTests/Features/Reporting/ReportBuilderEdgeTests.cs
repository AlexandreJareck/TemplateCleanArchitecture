using Shouldly;
using Template.Application.Features.Reporting;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Files;
using Template.Application.Reporting;
using Template.Application.Services.Files;
using Template.Application.Services.Reporting;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Unit.Tests.ApplicationTests.Features.Procurement;

namespace Template.Unit.Tests.ApplicationTests.Features.Reporting;

/// <summary>
/// Builder and document-model guards: missing or not-yet-existing sources, draft content with lines, row limits,
/// quotes withdrawn after the as-of instant, invitations without a quote, content-model validation and file signature
/// checks. All data is synthetic.
/// </summary>
public sealed class ReportBuilderEdgeTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private readonly ReportingTestDoubles doubles = new();

    [Fact]
    public async Task T_RPT_003_j_RequisitionPdf_MissingOrNotYetExisting_AndDraftWithLines()
    {
        var builder = new RequisitionPdfBuilder(doubles.Procurement.Requisitions);
        var missing = Context(ReportType.RequisitionPdf, new ReportParameters(RequisitionId: Guid.NewGuid(), RequisitionVersion: 1));
        (await Should.ThrowAsync<ReportSourceException>(() => builder.BuildAsync(missing, default))).FailureCode.ShouldBe(ReportFailureCodes.SourceNotFound);

        doubles.Procurement.ActAs(doubles.Procurement.Requester);
        var created = await doubles.Procurement.Drafts().Handle(new CreateRequisitionDraftCommand(
            doubles.Procurement.CostCenter.Id, "BRL", "Draft with lines for the report", ProcurementTestDoubles.Today.AddDays(10)), default);
        var draft = doubles.Procurement.Requisitions.Items.Single(item => item.Id == created.Value.Id);
        (await doubles.Procurement.Drafts().Handle(new AddRequisitionLineCommand(draft.Id, draft.Version, new RequisitionLineRequest(
            null, "Monitor 24 inch", RequisitionLineKind.Good, doubles.Procurement.Category.Id, doubles.Procurement.Unit.Id, "2",
            new Template.Application.Features.Catalog.MoneyInput("800", "BRL"))), default)).IsSuccess.ShouldBeTrue();

        var before = Context(ReportType.RequisitionPdf, new ReportParameters(RequisitionId: draft.Id, RequisitionVersion: draft.Version), ProcurementTestDoubles.Now.AddDays(-1));
        (await Should.ThrowAsync<ReportSourceException>(() => builder.BuildAsync(before, default))).FailureCode.ShouldBe(ReportFailureCodes.SourceNotFound);

        var content = await builder.BuildAsync(Context(ReportType.RequisitionPdf, new ReportParameters(RequisitionId: draft.Id, RequisitionVersion: draft.Version)), default);
        var lines = content.Pdf!.Sections.Single(section => section.Heading == "Itens").Table!;
        lines.Rows.Single()[1].ShouldBe("Monitor 24 inch");
        lines.Totals![^1].ShouldBe("1.600,00 BRL");
    }

    [Fact]
    public async Task T_RPT_003_k_BudgetAndSupplierBuilders_MissingSourcesAndRowLimits()
    {
        var budget = new BudgetConsumptionXlsxBuilder(doubles.Sources);
        (await Should.ThrowAsync<ReportSourceException>(() => budget.BuildAsync(
            Context(ReportType.BudgetConsumptionXlsx, new ReportParameters(FiscalPeriodId: Guid.NewGuid())), default))).FailureCode.ShouldBe(ReportFailureCodes.SourceNotFound);
        await doubles.SubmittedRequisitionAsync("1", "100"); // one reservation and its movement
        var tight = Context(ReportType.BudgetConsumptionXlsx, new ReportParameters(FiscalPeriodId: doubles.Procurement.Period.Id), policy: ReportPolicy.V1 with { MaxRows = 2 });
        (await Should.ThrowAsync<ReportSourceException>(() => budget.BuildAsync(tight, default))).FailureCode.ShouldBe(ReportFailureCodes.TooLarge);

        await doubles.ClosedRfqAsync();
        var quotes = new SupplierQuotesXlsxBuilder(doubles.Sources);
        var window = new ReportParameters(From: new DateOnly(2027, 3, 1), To: new DateOnly(2027, 3, 1));
        (await Should.ThrowAsync<ReportSourceException>(() => quotes.BuildAsync(
            Context(ReportType.SupplierQuotesXlsx, window, policy: ReportPolicy.V1 with { MaxRows = 0 }), default))).FailureCode.ShouldBe(ReportFailureCodes.TooLarge);
        (await Should.ThrowAsync<ReportSourceException>(() => quotes.BuildAsync(
            Context(ReportType.SupplierQuotesXlsx, window, policy: ReportPolicy.V1 with { MaxRows = 3 }), default))).FailureCode.ShouldBe(ReportFailureCodes.TooLarge);
    }

    [Fact]
    public async Task T_RPT_003_l_SupplierQuotes_UninvitedResponsesAndWithdrawalsAfterAsOf()
    {
        var requisition = await doubles.SubmittedRequisitionAsync();
        doubles.Procurement.ActAs(doubles.Buyer);
        (await doubles.Procurement.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, null), default)).IsSuccess.ShouldBeTrue();
        var rfq = doubles.Procurement.Rfqs.Items.Single(item => item.RequisitionId == requisition.Id);
        var suppliers = Enumerable.Range(0, 3).Select(index => doubles.Procurement.Suppliers.AddActive($"Bidder {index}")).ToArray();
        foreach (var supplier in suppliers)
            (await doubles.Procurement.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, supplier.SupplierId), default)).IsSuccess.ShouldBeTrue();
        (await doubles.Procurement.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(3), null), default)).IsSuccess.ShouldBeTrue();
        (await doubles.Procurement.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, suppliers[0].SupplierId, ReportingTestDoubles.Quote(900m)), default))
            .IsSuccess.ShouldBeTrue();
        var asOf = ProcurementTestDoubles.Now.AddMinutes(1);
        doubles.Procurement.Clock.UtcNow = ProcurementTestDoubles.Now.AddHours(1);
        var quoteId = rfq.Quotes.Single().Id;
        (await doubles.Procurement.QuoteCommands().Handle(new WithdrawQuoteCommand(rfq.Id, rfq.Version, quoteId, "Supplier withdrew the synthetic offer"), default))
            .IsSuccess.ShouldBeTrue();

        var content = await new SupplierQuotesXlsxBuilder(doubles.Sources).BuildAsync(
            Context(ReportType.SupplierQuotesXlsx, new ReportParameters(From: new DateOnly(2027, 3, 1), To: new DateOnly(2027, 3, 1)), asOf), default);
        var rows = content.Workbook!.Sheets[0].Rows;
        rows.Single()[6].Text.ShouldBe("Vigente"); // withdrawn only after the as-of instant
        rows.Single()[19].Text.ShouldBe(string.Empty); // never compared yet
        var performance = content.Workbook.Sheets[1].Rows;
        performance.Count.ShouldBe(3);
        performance.Count(row => row[3].Number == 0m).ShouldBe(2); // invited without a response
        performance.ShouldAllBe(row => row[6].Kind == WorkbookCellKind.Text); // no ranking yet
    }

    [Fact]
    public void T_RPT_008_c_ContentModelValidation_RejectsInconsistentShapes()
    {
        Should.Throw<ArgumentException>(() => new ReportTable([], []).Validate());
        Should.Throw<ArgumentException>(() => new ReportTable([new("A")], [["1"]], ["1", "2"]).Validate());
        new ReportTable([new("A")], [["1"]], ["t"]).Validate().Rows.Count.ShouldBe(1);
        Should.Throw<ArgumentException>(() => new WorkbookSheet("", [], [new("A")], []).Validate());
        Should.Throw<ArgumentException>(() => new WorkbookSheet(new string('x', 32), [], [new("A")], []).Validate());
        Should.Throw<ArgumentException>(() => new WorkbookSheet("ok", [], [], []).Validate());
        Should.Throw<ArgumentException>(() => new WorkbookSheet("ok", [], [new("A")], [], [WorkbookCell.Empty, WorkbookCell.Empty]).Validate());
        new WorkbookSheet("ok", [], [new("A")], [[WorkbookCell.Integer(1)]], [WorkbookCell.Integer(1)]).Validate().Name.ShouldBe("ok");
        WorkbookCell.Rate(1.5m).Kind.ShouldBe(WorkbookCellKind.Rate);
        WorkbookCell.Decimal(1.5m).Kind.ShouldBe(WorkbookCellKind.Decimal);
        WorkbookCell.LocalDateTime(new DateTime(2027, 3, 1, 10, 0, 0, DateTimeKind.Utc)).Instant!.Value.Kind.ShouldBe(DateTimeKind.Unspecified);
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, true)]
    [InlineData("image/png", new byte[] { 0x89, 0x50 }, false)]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }, true)]
    [InlineData("text/csv", new byte[] { 0x61, 0x2C }, false)]
    public async Task T_FIL_002_h_SignatureSniffing(string mediaType, byte[] bytes, bool expected) =>
        (await FileContent.MatchesAsync(new MemoryStream(bytes), mediaType, default)).ShouldBe(expected);

    [Fact]
    public async Task T_FIL_002_i_ReadBackMeasurement_OfMissingAndStoredObjects()
    {
        var storage = new ReportingTestDoubles.FakeStorage();
        (await FileContent.MeasureAsync(storage, Guid.NewGuid(), default)).ShouldBeNull();
        var id = Guid.NewGuid();
        var stored = await storage.WriteAsync(id, new MemoryStream(new byte[100_000]), "application/pdf", 200_000, default);
        (await FileContent.MeasureAsync(storage, id, default)).ShouldBe(stored);
        await Should.ThrowAsync<FileContentTooLargeException>(() => storage.WriteAsync(Guid.NewGuid(), new MemoryStream(new byte[10]), "application/pdf", 5, default));
    }

    private static ReportBuildContext Context(ReportType type, ReportParameters parameters, DateTime? asOf = null, ReportPolicy? policy = null)
    {
        var json = parameters.ToCanonicalJson();
        var at = asOf ?? ProcurementTestDoubles.Now;
        var job = ReportJob.Request(Guid.NewGuid(), type, Guid.NewGuid(), json, ReportParameters.Fingerprint(type, json), at, Zone.Id, ReportPolicy.V1,
            at.AddMinutes(5)).Job!;
        return new ReportBuildContext(job, parameters, at.AddMinutes(5), Zone, policy ?? ReportPolicy.V1);
    }
}
