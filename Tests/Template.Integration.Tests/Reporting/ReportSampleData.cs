using Shouldly;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Application.Features.Reporting;

namespace Template.Integration.Tests.Reporting;

/// <summary>
/// Synthetic aggregates for the report adapters: a submitted requisition with accented and markup-like text, a budget
/// period with an allocation and reservation, and a closed RFQ with three quotes (one in USD with a persisted rate).
/// Never real people, suppliers or prices.
/// </summary>
internal static class ReportSampleData
{
    public static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    public const string TimeZone = "America/Sao_Paulo";

    public static PurchaseRequisition SubmittedRequisition(int lines = 3, string purpose = "Aquisição de notebooks para a equipe de suporte <script>alert(1)</script>")
    {
        var costCenter = new CostCenterSnapshot(Guid.NewGuid(), "CC-OPS", "Operações & Suporte", Guid.NewGuid(), true);
        var requisition = PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000042", Guid.NewGuid(), costCenter, "BRL", true, purpose,
            new DateOnly(2027, 4, 15), null, Now.AddHours(-2), "sample").Requisition!;
        var category = new CategorySnapshot(Guid.NewGuid(), "TI", "Tecnologia", true);
        var unit = new UnitSnapshot(Guid.NewGuid(), "UN", "Unidade", 0, true);
        for (var index = 1; index <= lines; index++)
        {
            requisition.AddLine(new RequisitionLineInput(null, index == 1 ? "=HYPERLINK(\"http://example.invalid\") notebook 14\"" : $"Item de teste nº {index} — ação",
                RequisitionLineKind.Good, category, unit, index, new Money(1234.5m + index, "BRL")), RequisitionPolicy.V1, Now.AddHours(-2)).Error.ShouldBeNull();
        }

        requisition.AddAttachment(Guid.NewGuid(), "orçamento <fornecedor> & cia.pdf", "application/pdf", 2048, null, Now.AddHours(-2)).Error.ShouldBeNull();
        requisition.Submit(new SubmissionContext(null, Now.AddHours(-1), new DateOnly(2027, 3, 1), true, costCenter,
            new Dictionary<Guid, bool> { [category.Id] = true, [unit.Id] = true }, "sample", RequisitionPolicy.V1), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())
            .ShouldBeNull();
        return requisition;
    }

    public static (FiscalPeriod Period, BudgetAllocation Allocation) Budget(Guid costCenterId)
    {
        var period = FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Exercício 2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), null, Now.AddDays(-30)).Period!;
        period.Open(Now.AddDays(-30));
        var allocation = BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(costCenterId, true), "BRL", true, 150_000m, null, Now.AddDays(-30)).Allocation!;
        allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(12_345.67m, "BRL"), period, new DateOnly(2027, 2, 1), null, Now.AddDays(-20), "sample")
            .Error.ShouldBeNull();
        allocation.ChangeAmount(175_000m, "=1+1 reforço aprovado", null, Now.AddDays(-10)).ShouldBeNull();
        return (period, allocation);
    }

    public static RequestForQuotation ClosedRfq()
    {
        var rfq = RequestForQuotation.Create(Guid.NewGuid(), "RFQ-2027-000007", 1, SourcingMethod.Competitive,
            new RequisitionSourcingSnapshot(Guid.NewGuid(), "REQ-2027-000042", 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BRL", 18_000m,
                new DateOnly(2027, 4, 1), [new RfqLineSnapshot(1, null, "Notebook 14\" <i>ação</i>", "Good", Guid.NewGuid(), "TI", Guid.NewGuid(), "UN", 18m, 1000m, 18_000m)]),
            "BRL", null, SourcingPolicy.V1, null, Now.AddDays(-5), "sample").Rfq!;
        var suppliers = new[]
        {
            new SupplierSnapshot(Guid.NewGuid(), "Fornecedor Ágil Ltda", "BR", "11222333000181", "Active", true),
            new SupplierSnapshot(Guid.NewGuid(), "@Comercial =Sul S.A.", "BR", "11444777000161", "Active", true),
            new SupplierSnapshot(Guid.NewGuid(), "Global Parts Inc.", "US", "US-98-7654321", "Active", true)
        };
        foreach (var supplier in suppliers)
            rfq.Invite(supplier, null, Now.AddDays(-5), "sample").Error.ShouldBeNull();
        rfq.Publish(Now.AddDays(-1), suppliers.ToDictionary(supplier => supplier.SupplierId, _ => true), null, SourcingPolicy.V1, null, Now.AddDays(-4), "sample")
            .Error.ShouldBeNull();
        (string Currency, decimal Price)[] offers = [("BRL", 950m), ("BRL", 900m), ("USD", 170m)];
        for (var index = 0; index < suppliers.Length; index++)
            rfq.EnterQuote(suppliers[index].SupplierId, new QuoteInput(offers[index].Currency, true, [new QuoteLineInput(1, 18m, offers[index].Price)], 0m, 0m, 0m,
                new DateOnly(2027, 4, 30), 5 + index, "-30 dias", index == 1 ? "+VAT @ cmd|' /C calc'!A0" : null, QuoteSource.Email, Now.AddDays(-2), []),
                new DateOnly(2027, 2, 28), null, Now.AddDays(-2), "sample").Error.ShouldBeNull();
        rfq.Close(null, suppliers.ToDictionary(supplier => supplier.SupplierId, _ => true),
            [new ExchangeRateInput("USD", 5.4321m, ExchangeRateSource.BcbPtax, new DateOnly(2027, 2, 26), Now.AddDays(-1), "BCB PTAX")],
            new DateOnly(2027, 2, 28), SourcingPolicy.V1, null, Now.AddDays(-1), "sample").Error.ShouldBeNull();
        return rfq;
    }

    public static ReportJob Job(ReportType type, ReportParameters parameters, Guid? requester = null)
    {
        var json = parameters.ToCanonicalJson();
        var job = ReportJob.Request(Guid.NewGuid(), type, requester ?? Guid.NewGuid(), json, ReportParameters.Fingerprint(type, json), Now, TimeZone, ReportPolicy.V1, Now).Job!;
        job.ClearDomainEvents();
        return job;
    }
}
