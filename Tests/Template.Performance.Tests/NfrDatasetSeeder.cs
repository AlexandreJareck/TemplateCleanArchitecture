using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Template.Application.Demo;
using Template.Domain.Approvals;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Contexts.Configurations;

namespace Template.Performance.Tests;

/// <summary>Sizes of the synthetic dataset named by docs/product/non-functional-requirements.md §4.</summary>
public sealed record NfrVolumes(
    int CostCenters = 50,
    int Suppliers = 1_000,
    int CatalogItems = 5_000,
    int Requisitions = 20_000,
    int Rfqs = 5_000,
    int QuotesPerRfq = 3,
    int PurchaseOrders = 4_000,
    int Receipts = 8_000,
    int CommandSamples = 30,
    int XlsxReservations = 4_990);

/// <summary>What the measurements need from the seeded data: sample IDs and command fixtures.</summary>
public sealed class SeededDataset
{
    public List<Guid> RequisitionIds { get; } = [];
    public List<Guid> RfqIds { get; } = [];
    public List<Guid> PurchaseOrderIds { get; } = [];
    public List<Guid> ReceiptIds { get; } = [];
    public List<Guid> SupplierIds { get; } = [];
    public List<Guid> CostCenterIds { get; } = [];
    public List<Guid> SubmitFixtures { get; } = [];
    public List<(Guid RfqId, Guid RevisionId, Guid RequisitionId)> AwardFixtures { get; } = [];
    public List<Guid> IssueFixtures { get; } = [];
    public List<Guid> ReceiveFixtures { get; } = [];
    public Guid XlsxCostCenterId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public Dictionary<string, long> Counts { get; } = [];
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Seeds the NFR dataset through the domain aggregates (valid states, real indexes), in batches with a fresh context
/// each. It runs on top of the demo seed and continues its numbering; all data is synthetic.
/// </summary>
public static class NfrDatasetSeeder
{
    private const string Correlation = "nfr-performance-seed";
    private const string ApprovalCompleted = "procurement.approval.completed";
    private static readonly Guid DemoFiscalPeriodId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private const int Batch = 400;

    public static async Task<SeededDataset> SeedAsync(Func<ApplicationDbContext> contextFactory, NfrVolumes volumes)
    {
        var clock = Stopwatch.StartNew();
        var dataset = new SeededDataset { FiscalPeriodId = DemoFiscalPeriodId };
        var nowUtc = DateTime.UtcNow;
        var year = nowUtc.Year;
        var start = nowUtc.Date.AddDays(-25).AddHours(12);
        var actor = DemoDataIds.Administrator;

        // 1. Master data and allocations (one context).
        var costCenters = new List<CostCenterSnapshot>();
        var categories = new List<CategorySnapshot>();
        var units = new List<UnitSnapshot>();
        var catalog = new List<(Guid Id, Guid CategoryId, Guid UnitId)>();
        var suppliers = new List<SupplierSnapshot>();
        await using (var context = contextFactory())
        {
            var period = await context.Set<FiscalPeriod>().SingleAsync(item => item.Id == DemoFiscalPeriodId);
            var departments = Enumerable.Range(1, 5)
                .Select(index => Require(Department.Create(Guid.NewGuid(), $"PD{index:D2}", $"Departamento sintético {index}", DemoDataIds.DepartmentHead, actor, start)))
                .ToArray();
            context.AddRange(departments);
            for (var index = 0; index < volumes.CostCenters; index++)
            {
                var department = departments[index % departments.Length];
                var costCenter = Require(CostCenter.Create(Guid.NewGuid(), $"PC{index:D3}", $"Centro sintético {index:D3}", department,
                    DemoDataIds.CostCenterManager, actor, start));
                context.Add(costCenter);
                costCenters.Add(new CostCenterSnapshot(costCenter.Id, costCenter.Code, costCenter.Name, department.Id, true));
                var amount = index == 0 ? 500_000_000m : 50_000_000m;
                context.Add(Require(BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(costCenter.Id, true), "BRL", true, amount, actor, start)));
            }

            var categoryEntities = Enumerable.Range(1, 20)
                .Select(index => Require(Category.Create(Guid.NewGuid(), $"PCAT{index:D2}", $"Categoria sintética {index}", actor, start)))
                .ToArray();
            var unitEntities = new[]
            {
                Require(UnitOfMeasure.Create(Guid.NewGuid(), "PUN", "Unidade sintética", 0, actor, start)),
                Require(UnitOfMeasure.Create(Guid.NewGuid(), "PCX", "Caixa sintética", 0, actor, start)),
                Require(UnitOfMeasure.Create(Guid.NewGuid(), "PLC", "Licença sintética", 0, actor, start))
            };
            context.AddRange(categoryEntities);
            context.AddRange(unitEntities);
            categories.AddRange(categoryEntities.Select(item => new CategorySnapshot(item.Id, item.Code, item.Name, true)));
            units.AddRange(unitEntities.Select(item => new UnitSnapshot(item.Id, item.Code, item.Name, 0, true)));
            for (var index = 0; index < volumes.CatalogItems; index++)
            {
                var category = categoryEntities[index % categoryEntities.Length];
                var unit = unitEntities[index % unitEntities.Length];
                var item = Require(CatalogItem.Create(Guid.NewGuid(), $"PITEM{index:D5}", $"Item sintético {index:D5}", CatalogItemKind.Good,
                    category, unit, 100m + index % 900, "BRL", actor, start));
                context.Add(item);
                catalog.Add((item.Id, category.Id, unit.Id));
            }

            for (var index = 0; index < volumes.Suppliers; index++)
            {
                var taxId = Cnpj(90_000_000_0001L + index);
                var supplier = Require(Supplier.CreateDraft(Guid.NewGuid(), $"Fornecedor Sintético {index:D4} Ltda", $"Sintético {index:D4}", "BR", taxId, actor, start));
                // Active suppliers only can win an award (SRC-016); activation needs contact, address and category (SUP-002).
                Require(supplier.ReplaceContacts([new(null, "Contato sintético", $"contato{index:D4}@example.invalid", "+55 11 0000-0000", "Vendas")], start));
                Require(supplier.ReplaceAddresses([new(null, new AddressValues(SupplierAddressType.Headquarters, "01000-000", "Avenida Sintética", "1", null,
                    "Centro", "São Paulo", "SP", "BR"))], start));
                Require(supplier.SetCategories([new MasterDataReference(categoryEntities[index % categoryEntities.Length].Id, true)], start));
                Require(supplier.Activate(DemoDataIds.ProcurementManager, start));
                context.Add(supplier);
                suppliers.Add(new SupplierSnapshot(supplier.Id, supplier.LegalName, "BR", taxId, "Active", true));
            }

            await context.SaveChangesAsync();
        }

        dataset.CostCenterIds.AddRange(costCenters.Select(item => item.Id));
        dataset.SupplierIds.AddRange(suppliers.Select(item => item.SupplierId));
        dataset.XlsxCostCenterId = costCenters[0].Id;

        // 2. Numbering continues after the demo seed.
        long requisitionNumber, rfqNumber = 0, orderNumber = 0, receiptNumber = 0;
        await using (var context = contextFactory())
            requisitionNumber = (await context.Set<RequisitionNumberCounter>().Where(counter => counter.Year == year).Select(counter => (long?)counter.LastValue).SingleOrDefaultAsync()) ?? 0;

        // 3. Requisition chains, one batch per context. Index plan (out of volumes.Requisitions):
        //    [0, xlsx)            submitted on the XLSX cost center (budget-consumption workbook ≈ 10,000 rows)
        //    [xlsx, xlsx+rfqs)    submitted, in sourcing, closed RFQ with three quotes; the first `orders` are awarded,
        //                         approved, committed and ordered; the rest stay closed (award fixtures come from them)
        //    then submitted only, then drafts (submit fixtures are the last drafts, owned by demo.requester)
        var submittedTotal = volumes.Requisitions * 3 / 5;
        var rfqStart = volumes.XlsxReservations;
        var rfqEnd = rfqStart + volumes.Rfqs;
        var orderEnd = rfqStart + volumes.PurchaseOrders;
        var receiptsLeft = volumes.Receipts;
        for (var batchStart = 0; batchStart < volumes.Requisitions; batchStart += Batch)
        {
            await using var context = contextFactory();
            context.ChangeTracker.AutoDetectChangesEnabled = true;
            var period = await context.Set<FiscalPeriod>().SingleAsync(item => item.Id == DemoFiscalPeriodId);
            var approvalPolicy = await context.Set<ApprovalPolicy>().Include(item => item.Levels).OrderBy(item => item.Version).FirstAsync();
            var allocations = await context.Set<BudgetAllocation>()
                .Where(item => item.FiscalPeriodId == DemoFiscalPeriodId && dataset.CostCenterIds.Contains(item.CostCenterId))
                .ToDictionaryAsync(item => item.CostCenterId);
            var batchEnd = Math.Min(batchStart + Batch, volumes.Requisitions);
            for (var index = batchStart; index < batchEnd; index++)
            {
                var at = start.AddSeconds(index * 45);
                var isDraft = index >= submittedTotal;
                var isSubmitFixture = index >= volumes.Requisitions - volumes.CommandSamples;
                var costCenter = index < volumes.XlsxReservations || isSubmitFixture ? costCenters[0] : costCenters[1 + index % (costCenters.Count - 1)];
                var requester = isSubmitFixture ? DemoDataIds.Requester : DemoDataIds.Requester;
                var number = $"REQ-{year}-{++requisitionNumber:D6}";
                var requisition = Require(PurchaseRequisition.CreateDraft(Guid.NewGuid(), number, requester, costCenter, "BRL", true,
                    $"Necessidade sintética {index:D5}", DateOnly.FromDateTime(at).AddDays(60), requester, at, Correlation));
                var lineCount = index % 9 + 1;
                var lines = new List<(int Number, CategorySnapshot Category, UnitSnapshot Unit, decimal Quantity, decimal Price, Guid? Item)>();
                for (var line = 0; line < lineCount; line++)
                {
                    var item = catalog[(index * 7 + line) % catalog.Count];
                    var category = categories.First(candidate => candidate.Id == item.CategoryId);
                    var unit = units.First(candidate => candidate.Id == item.UnitId);
                    var quantity = 1 + (index + line) % 10;
                    var price = 100m + (index * 13 + line * 37) % 2_000;
                    Require(requisition.AddLine(new RequisitionLineInput(null, $"Linha sintética {line + 1}", RequisitionLineKind.Good, category, unit, quantity,
                        new Money(price, "BRL")), RequisitionPolicy.V1, at));
                    lines.Add((line + 1, category, unit, quantity, price, null));
                }

                context.Add(requisition);
                dataset.RequisitionIds.Add(requisition.Id);
                if (isDraft)
                {
                    if (isSubmitFixture)
                        dataset.SubmitFixtures.Add(requisition.Id);
                    continue;
                }

                // Submission and reservation (REQ-005, BUD-004..008).
                var submitAt = at.AddMinutes(5);
                var today = DateOnly.FromDateTime(submitAt);
                var allocation = allocations[costCenter.Id];
                var reservationId = Guid.NewGuid();
                var states = lines.SelectMany(line => new[] { line.Category.Id, line.Unit.Id }).Distinct().ToDictionary(id => id, _ => true);
                Require(requisition.Submit(new SubmissionContext(requester, submitAt, today, true, costCenter, states, Correlation, RequisitionPolicy.V1),
                    Guid.NewGuid(), reservationId, allocation.Id));
                Require(allocation.Reserve(reservationId, requisition.Id, requisition.Revision, requisition.EstimatedTotalMoney, period, today, requester, submitAt, Correlation));
                if (index < rfqStart || index >= rfqEnd)
                    continue;

                // Sourcing: three invitations, publish, three quotes, close (SRC-001..015).
                var rfqAt = submitAt.AddHours(1);
                var snapshot = new RequisitionSourcingSnapshot(requisition.Id, requisition.Number, requisition.Revision, requester, costCenter.Id,
                    costCenter.DepartmentId, "BRL", requisition.EstimatedTotal, requisition.NeededBy!.Value,
                    lines.Select(line => new RfqLineSnapshot(line.Number, null, $"Linha sintética {line.Number}", "Good", line.Category.Id, line.Category.Code,
                        line.Unit.Id, line.Unit.Code, line.Quantity, line.Price, line.Quantity * line.Price)).ToArray());
                var rfq = Require(RequestForQuotation.Create(Guid.NewGuid(), $"RFQ-{year}-{++rfqNumber:D6}", 1, SourcingMethod.Competitive, snapshot, "BRL", null,
                    SourcingPolicy.V1, DemoDataIds.Buyer, rfqAt, Correlation));
                Require(requisition.StartSourcing(rfq.Id, DemoDataIds.Buyer, rfqAt, Correlation));
                var invited = Enumerable.Range(0, volumes.QuotesPerRfq).Select(offset => suppliers[(index + offset * 331) % suppliers.Count]).ToArray();
                foreach (var supplier in invited)
                    Require(rfq.Invite(supplier, DemoDataIds.Buyer, rfqAt, Correlation));
                var publishAt = rfqAt.AddMinutes(30);
                var deadline = publishAt.AddDays(2);
                var active = invited.ToDictionary(supplier => supplier.SupplierId, _ => true);
                Require(rfq.Publish(deadline, active, null, SourcingPolicy.V1, DemoDataIds.Buyer, publishAt, Correlation));
                var deadlineDate = DateOnly.FromDateTime(deadline);
                for (var offset = 0; offset < invited.Length; offset++)
                {
                    var factor = 0.90m + offset * 0.05m;
                    var quote = new QuoteInput("BRL", true,
                        lines.Select(line => new QuoteLineInput(line.Number, line.Quantity, decimal.Round(line.Price * factor, 2))).ToArray(),
                        0m, 0m, 0m, deadlineDate.AddDays(30), 5 + offset, "30 dias", null, QuoteSource.Email, publishAt.AddHours(1 + offset), []);
                    Require(rfq.EnterQuote(invited[offset].SupplierId, quote, deadlineDate, DemoDataIds.Buyer, publishAt.AddHours(2 + offset), Correlation));
                }

                var comparison = Require(rfq.Close(null, active, [], deadlineDate, SourcingPolicy.V1, DemoDataIds.Buyer, deadline, Correlation));
                context.Add(rfq);
                dataset.RfqIds.Add(rfq.Id);
                var recommended = comparison.RecommendedQuoteRevisionId!.Value;
                if (index >= orderEnd)
                {
                    if (dataset.AwardFixtures.Count < volumes.CommandSamples)
                        dataset.AwardFixtures.Add((rfq.Id, recommended, requisition.Id));
                    continue;
                }

                // Award, approval outcome and commitment (SRC-016..019, APR-013, BUD-010).
                var awardAt = deadline.AddHours(1);
                var award = Require(rfq.SubmitAward(Guid.NewGuid(), recommended, active, deadlineDate, [], "Menor preço", SourcingPolicy.V1, DemoDataIds.Buyer,
                    awardAt, Correlation));
                var winner = comparison.Entries.Single(entry => entry.QuoteRevisionId == recommended);
                var supplierSnapshot = invited.Single(supplier => supplier.SupplierId == winner.SupplierId);
                Require(requisition.SubmitAward(award.Id, winner.SupplierId, supplierSnapshot.LegalName, new Money(winner.RawTotal, "BRL"), DemoDataIds.Buyer, awardAt,
                    Correlation));
                var approvedAt = awardAt.AddHours(2);
                var approvalInstanceId = Guid.NewGuid();
                var orderIndexForApproval = index - rfqStart;
                if (orderIndexForApproval >= volumes.PurchaseOrders - volumes.CommandSamples)
                {
                    // Issue fixtures: issuing reads the award's approval instance (PUR-003), so it is a real approved one.
                    var amount = new Money(winner.RawTotal, "BRL");
                    var instance = Require(ApprovalInstance.Start(approvalInstanceId, new ApprovalStartFacts(award.Id, rfq.Id, requisition.Id, requisition.Number,
                        requester, DemoDataIds.Buyer, costCenter.Id, costCenter.DepartmentId, winner.SupplierId, amount, amount), approvalPolicy, "1.0",
                        awardAt.AddMinutes(10), Correlation));
                    var decideAt = awardAt.AddMinutes(20);
                    for (var level = 0; level < 5 && instance.Status == ApprovalStatus.Pending && instance.CurrentStep is { } step; level++)
                    {
                        var approver = Guid.NewGuid(); // a distinct approver per level (APR-007)
                        Require(instance.AssignCurrentStep(ApprovalAssignee.ForUser(approver), "performance-seed", decideAt, Correlation));
                        Require(instance.Decide(step.Level, new ApprovalActor(approver, new HashSet<string>(), true), ApprovalDecision.Approved, null, Guid.NewGuid(),
                            decideAt.AddMinutes(1), Correlation));
                        decideAt = decideAt.AddMinutes(5);
                        if (instance.CurrentStep is { } next && next.Level == step.Level)
                            break;
                    }

                    Require(instance.Finalize(decideAt, Correlation));
                    instance.Status.ShouldBeApproved();
                    context.Add(instance);
                }

                Require(rfq.ApproveAward(award.Id, approvedAt, Correlation, ApprovalCompleted));
                Require(requisition.ApproveAward(award.Id, approvedAt, Correlation, ApprovalCompleted));
                Require(allocation.Commit(requisition.Id, DemoDataIds.Buyer, approvedAt, Correlation));

                // Purchase order (PUR-001..006) and receipts (RCV-001..008).
                var factorWinner = 0.90m + Array.FindIndex(invited, supplier => supplier.SupplierId == winner.SupplierId) * 0.05m;
                var orderLines = lines.Select(line => new PurchaseOrderLineInput(line.Number, null, $"Linha sintética {line.Number}", "Good", line.Category.Id,
                    line.Unit.Id, line.Unit.Code, 0, line.Quantity, decimal.Round(line.Price * factorWinner, 2), deadlineDate.AddDays(10))).ToArray();
                var total = orderLines.Sum(line => CalculationRules.LineTotal(line.Quantity, new Money(line.UnitPrice, "BRL")).Amount);
                var draftAt = approvedAt.AddHours(1);
                var order = PurchaseOrder.CreateDraft(Guid.NewGuid(), new PurchaseOrderDraftInput(award.Id, approvalInstanceId, rfq.Id, rfq.Number, recommended,
                    requisition.Id, requisition.Number, requester, costCenter.Id, costCenter.DepartmentId,
                    new PurchaseOrderSupplierSnapshot(supplierSnapshot.SupplierId, supplierSnapshot.LegalName, null, "BR", supplierSnapshot.TaxId,
                        "Avenida Sintética 1", "Contato sintético", "contato@example.invalid", "+55 11 0000-0000"),
                    DemoDataIds.Buyer, "Comprador sintético", "BRL", 0m, 0m, 0m, total, "30 dias", deadlineDate.AddDays(30), 5, "BRL", 1m, "BRL", 1m, total,
                    orderLines), draftAt, Correlation);
                requisition.LinkPurchaseOrder(order.Id, draftAt);
                Require(order.UpdateDraft("Rua Sintética 100, São Paulo - SP", null, null, DateOnly.FromDateTime(draftAt), DemoDataIds.Buyer, draftAt, Correlation));
                context.Add(order);
                dataset.PurchaseOrderIds.Add(order.Id);
                var orderIndex = index - rfqStart;
                if (orderIndex >= volumes.PurchaseOrders - volumes.CommandSamples)
                {
                    dataset.IssueFixtures.Add(order.Id);
                    continue;
                }

                var issueAt = draftAt.AddHours(2);
                var issueDate = DateOnly.FromDateTime(issueAt);
                Require(order.Issue($"PO-{year}-{++orderNumber:D6}", issueDate, Guid.NewGuid(), true, true, true, DemoDataIds.Buyer, issueAt, Correlation));
                Require(requisition.MarkOrdered(order.Id, DemoDataIds.Buyer, issueAt, Correlation));
                if (orderIndex >= volumes.PurchaseOrders - 2 * volumes.CommandSamples)
                {
                    dataset.ReceiveFixtures.Add(order.Id);
                    continue;
                }

                // Two receipts per order; the first orders take a third one until the receipt volume is reached.
                var ordersWithReceipts = volumes.PurchaseOrders - 2 * volumes.CommandSamples;
                var parts = orderIndex < (volumes.Receipts - 2 * ordersWithReceipts) * 2 ? 3 : 2; // single-unit lines yield fewer receipts
                if (receiptsLeft < parts)
                    continue;
                for (var part = 0; part < parts; part++)
                {
                    var receivedAt = issueAt.AddDays(1 + part);
                    var receiptLines = orderLines.Select(line =>
                    {
                        var quantity = line.Quantity;
                        var share = part < parts - 1 ? Math.Floor(quantity / parts) : quantity - Math.Floor(quantity / parts) * (parts - 1);
                        return new ReceiptLineInput(line.RfqLineNumber, share, share, 0m, null, null);
                    }).Where(line => line.Received > 0).ToArray();
                    if (receiptLines.Length == 0)
                        continue;
                    var posting = new ReceiptPosting(order.Id, order.Number!, orderLines.Select(line => new ReceiptOrderLine(line.RfqLineNumber, line.Description,
                        line.UnitCode, 0)).ToArray(), issueDate, DateOnly.FromDateTime(receivedAt), DemoDataIds.Receiver, DateOnly.FromDateTime(receivedAt),
                        "Doca sintética", $"NF-{receiptNumber + 1}", null, receiptLines, []);
                    var receipt = Require(Receipt.Post(Guid.NewGuid(), $"RCV-{year}-{++receiptNumber:D6}", posting, ReceivingPolicy.V1, receivedAt, Correlation));
                    Require(order.ApplyReceipt(receipt.Id, receiptLines.Select(line => new OrderReceiptLine(line.PoLineNumber, line.Received, line.Accepted,
                        line.Rejected)).ToArray(), 0m, DemoDataIds.Receiver, receivedAt, Correlation));
                    context.Add(receipt);
                    dataset.ReceiptIds.Add(receipt.Id);
                    receiptsLeft--;
                }
            }

            await context.SaveChangesAsync();
        }

        // 4. Counters, so commands measured over HTTP allocate the next free numbers.
        await using (var context = contextFactory())
        {
            await SetCounterAsync(context.Set<RequisitionNumberCounter>(), year, requisitionNumber, (y, v) => new RequisitionNumberCounter { Year = y, LastValue = v });
            await SetCounterAsync(context.Set<RfqNumberCounter>(), year, rfqNumber, (y, v) => new RfqNumberCounter { Year = y, LastValue = v });
            await SetCounterAsync(context.Set<PurchaseOrderNumberCounter>(), year, orderNumber, (y, v) => new PurchaseOrderNumberCounter { Year = y, LastValue = v });
            await SetCounterAsync(context.Set<ReceiptNumberCounter>(), year, receiptNumber, (y, v) => new ReceiptNumberCounter { Year = y, LastValue = v });
            await context.SaveChangesAsync();

            foreach (var (name, table) in new[]
            {
                ("costCenters", "OrganizationCostCenters"), ("suppliers", "Suppliers"), ("catalogItems", "CatalogItems"),
                ("requisitions", "PurchaseRequisitions"), ("requisitionLines", "PurchaseRequisitionLines"), ("reservations", "BudgetReservations"),
                ("rfqs", "SourcingRfqs"), ("quotes", "SourcingQuotes"), ("purchaseOrders", "PurchasingOrders"), ("receipts", "ReceivingReceipts")
            })
                dataset.Counts[name] = await CountAsync(context, table);
        }

        dataset.Duration = clock.Elapsed;
        return dataset;
    }

    private static async Task SetCounterAsync<T>(DbSet<T> set, int year, long value, Func<int, long, T> create) where T : class
    {
        var existing = await set.ToListAsync();
        foreach (var counter in existing)
            set.Remove(counter);
        set.Add(create(year, value));
    }

    private static async Task<long> CountAsync(ApplicationDbContext context, string table)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM \"{table}\"";
        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
        catch (Npgsql.PostgresException)
        {
            return -1; // table name differs; reported as unknown rather than failing the seed
        }
    }

    /// <summary>A valid numeric CNPJ (two mod-11 check digits) from a 12-digit base.</summary>
    internal static string Cnpj(long baseNumber)
    {
        var digits = baseNumber.ToString("D12", CultureInfo.InvariantCulture).Select(character => character - '0').ToList();
        int[] first = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] second = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        digits.Add(CheckDigit(digits, first));
        digits.Add(CheckDigit(digits, second));
        return string.Concat(digits);
    }

    private static int CheckDigit(IReadOnlyList<int> digits, int[] weights)
    {
        var remainder = weights.Select((weight, index) => weight * digits[index]).Sum() % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static T Require<T>((T? Value, DomainError? Error) result) where T : class =>
        result.Error is null ? result.Value! : throw new InvalidOperationException($"Seed step failed: {result.Error.Code} {result.Error.Message}");

    private static T Require<T>((T? Value, bool Flag, DomainError? Error) result) where T : class =>
        result.Error is null ? result.Value! : throw new InvalidOperationException($"Seed step failed: {result.Error.Code} {result.Error.Message}");

    private static void Require((bool Changed, DomainError? Error) result)
    {
        if (result.Error is not null)
            throw new InvalidOperationException($"Seed step failed: {result.Error.Code} {result.Error.Message}");
    }

    private static void Require((OrderCompletionChange Change, DomainError? Error) result)
    {
        if (result.Error is not null)
            throw new InvalidOperationException($"Seed step failed: {result.Error.Code} {result.Error.Message}");
    }

    private static void ShouldBeApproved(this ApprovalStatus status)
    {
        if (status != ApprovalStatus.Approved)
            throw new InvalidOperationException($"Seed approval ended {status}, not Approved.");
    }

    private static void Require((ApprovalDecisionResult? Result, DomainError? Error) result)
    {
        if (result.Error is not null)
            throw new InvalidOperationException($"Seed step failed: {result.Error.Code} {result.Error.Message}");
    }

    private static void Require(DomainError? error)
    {
        if (error is not null)
            throw new InvalidOperationException($"Seed step failed: {error.Code} {error.Message}");
    }
}
