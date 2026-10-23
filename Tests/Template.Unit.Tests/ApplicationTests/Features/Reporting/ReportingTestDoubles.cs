using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Features.Catalog;
using Template.Application.Features.Reporting;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Interfaces.Files;
using Template.Application.Interfaces.Reporting;
using Template.Application.Reporting;
using Template.Application.Security;
using Template.Application.Services.Reporting;
using Template.Domain.Budgeting;
using Template.Domain.Files;
using Template.Domain.Organization;
using Template.Domain.Reporting;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Unit.Tests.ApplicationTests.Features.Procurement;

namespace Template.Unit.Tests.ApplicationTests.Features.Reporting;

/// <summary>
/// Port fakes of the Phase 30 report use cases on top of <see cref="ProcurementTestDoubles"/>: job repository and
/// queue, stored-file metadata, an in-memory storage that hashes like the adapters, a source reader over the procurement
/// fakes and renderers that emit minimal signed outputs. All data is synthetic.
/// </summary>
internal sealed class ReportingTestDoubles
{
    public ReportingTestDoubles()
    {
        Procurement.Permissions.Grant(Buyer.Id, SeededRoles.PermissionsByRole[SeededRoles.Buyer]);
        Procurement.Permissions.Grant(Controller.Id, SeededRoles.PermissionsByRole[SeededRoles.BudgetController]);
        Procurement.Permissions.Grant(Auditor.Id, SeededRoles.PermissionsByRole[SeededRoles.Auditor]);
        Procurement.Allocation.ChangeAmount(100_000m, "Synthetic budget increase for report tests", null, ProcurementTestDoubles.Now);
    }

    public ProcurementTestDoubles Procurement { get; } = new();
    public ProcurementTestDoubles.Actor Buyer { get; } = new();
    public ProcurementTestDoubles.Actor Controller { get; } = new();
    public ProcurementTestDoubles.Actor Auditor { get; } = new();
    public FakeJobs Jobs { get; } = new();
    public FakeFiles Files { get; } = new();
    public FakeStorage Storage { get; } = new();
    public FakeRenderer Renderer { get; } = new();
    public FakeSources Sources => new(Procurement);
    public MalwareScanVerdict ScanVerdict { get; set; } = MalwareScanVerdict.NotScanned;

    public ReportJobHandlers Handlers()
    {
        var writer = Procurement.NewWriter();
        return new ReportJobHandlers(Jobs, Files, Storage, Procurement.Requisitions, Procurement.Rfqs, Sources,
            new RequisitionAccess(writer, Procurement.Scope), new ReportAccess(writer), writer);
    }

    public ReportJobProcessor Processor()
    {
        var writer = Procurement.NewWriter();
        IReportBuilder[] builders =
        [
            new RequisitionPdfBuilder(Procurement.Requisitions), new QuoteComparisonPdfBuilder(Procurement.Rfqs),
            new BudgetConsumptionXlsxBuilder(Sources), new SupplierQuotesXlsxBuilder(Sources)
        ];
        return new ReportJobProcessor(Jobs, Jobs, Files, Storage, new FakeScanner(this), builders, Renderer, Renderer, writer,
            NullLogger<ReportJobProcessor>.Instance);
    }

    public ReportMaintenance Maintenance() =>
        new(Jobs, Jobs, Files, Storage, Procurement.NewWriter(), NullLogger<ReportMaintenance>.Instance);

    /// <summary>A requisition submitted by the seeded requester (lines 18 × <paramref name="price"/> BRL).</summary>
    public async Task<PurchaseRequisition> SubmittedRequisitionAsync(string quantity = "18", string price = "1000")
    {
        Procurement.ActAs(Procurement.Requester);
        var created = await Procurement.Drafts().Handle(new CreateRequisitionDraftCommand(
            Procurement.CostCenter.Id, "BRL", "Replacement laptops for the support team", ProcurementTestDoubles.Today.AddDays(10)), default);
        created.IsSuccess.ShouldBeTrue(created.Error?.Message);
        var draft = Procurement.Requisitions.Items.Single(item => item.Id == created.Value.Id);
        (await Procurement.Drafts().Handle(new AddRequisitionLineCommand(draft.Id, draft.Version, new RequisitionLineRequest(
            null, "Laptop 14 inch", RequisitionLineKind.Good, Procurement.Category.Id, Procurement.Unit.Id, quantity, new MoneyInput(price, "BRL"))), default))
            .IsSuccess.ShouldBeTrue();
        (await Procurement.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default)).IsSuccess.ShouldBeTrue();
        return draft;
    }

    /// <summary>A closed competitive RFQ with three BRL quotes (unit prices 950, 900, 980) and a complete comparison.</summary>
    public async Task<(RequestForQuotation Rfq, SupplierSnapshot[] Suppliers)> ClosedRfqAsync(string? firstCurrency = null)
    {
        var requisition = await SubmittedRequisitionAsync();
        Procurement.ActAs(Buyer);
        (await Procurement.RfqCommands().Handle(new CreateRfqCommand(requisition.Id, null), default)).IsSuccess.ShouldBeTrue();
        var rfq = Procurement.Rfqs.Items.Single(item => item.RequisitionId == requisition.Id);
        var suppliers = Enumerable.Range(0, 3).Select(index => Procurement.Suppliers.AddActive($"Supplier {index}")).ToArray();
        foreach (var supplier in suppliers)
            (await Procurement.RfqCommands().Handle(new InviteSupplierCommand(rfq.Id, rfq.Version, supplier.SupplierId), default)).IsSuccess.ShouldBeTrue();
        (await Procurement.RfqCommands().Handle(new PublishRfqCommand(rfq.Id, rfq.Version, ProcurementTestDoubles.Now.AddDays(3), null), default)).IsSuccess.ShouldBeTrue();
        decimal[] prices = [950m, 900m, 980m];
        for (var index = 0; index < suppliers.Length; index++)
            (await Procurement.QuoteCommands().Handle(new EnterQuoteCommand(rfq.Id, rfq.Version, suppliers[index].SupplierId, Quote(prices[index], index == 0 ? firstCurrency ?? "BRL" : "BRL")), default))
                .IsSuccess.ShouldBeTrue();
        Procurement.Clock.UtcNow = ProcurementTestDoubles.Now.AddDays(3);
        (await Procurement.RfqCommands().Handle(new CloseRfqCommand(rfq.Id, rfq.Version, null), default)).IsSuccess.ShouldBeTrue();
        return (rfq, suppliers);
    }

    public static QuoteRequest Quote(decimal unitPrice, string currency = "BRL") => new(
        currency, [new QuoteLineRequest(1, "18", unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture))],
        null, null, "0", new DateOnly(2027, 4, 30), 7, "=HYPERLINK(\"http://example.invalid\")", "@SUM(A1:A2)", QuoteSource.Email, ProcurementTestDoubles.Now, []);

    internal sealed class FakeJobs : IReportJobRepository, IReportJobQueue
    {
        public List<ReportJob> Items { get; } = [];

        public void Add(ReportJob job) => Items.Add(job);

        public Task<ReportJob?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(job => job.Id == id));

        public Task<ReportJob?> FindActiveAsync(Guid requestedBy, ReportType type, string parametersHash, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(job => job.RequestedBy == requestedBy && job.ReportType == type && job.ParametersHash == parametersHash && job.IsActive));

        public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(job => job.Id == id)?.Version);

        public Task<PagedResult<ReportJob>> ListAsync(ReportJobFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            var matches = Items.Where(job => (filter.RequestedBy is null || job.RequestedBy == filter.RequestedBy)
                && (filter.Statuses is not { Count: > 0 } || filter.Statuses.Contains(job.Status))
                && (filter.Types is not { Count: > 0 } || filter.Types.Contains(job.ReportType))).ToArray();
            return Task.FromResult(new PagedResult<ReportJob>(matches.Skip(page.Skip).Take(page.PageSize).ToArray(), page.Page, page.PageSize, matches.Length));
        }

        public Task<ReportJob?> LockNextDueAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(job => job.IsClaimable(nowUtc)).OrderBy(job => job.NextAttemptAtUtc).ThenBy(job => job.RequestedAtUtc).FirstOrDefault());

        public Task<IReadOnlyList<Guid>> ListExpiredReadyAsync(DateTime nowUtc, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Items.Where(job => job.Status == ReportJobStatus.Ready && job.ExpiresAtUtc <= nowUtc).Select(job => job.Id).Take(max).ToArray());
    }

    internal sealed class FakeFiles : IStoredFileRepository
    {
        public List<StoredFile> Items { get; } = [];

        public void Add(StoredFile file) => Items.Add(file);

        public Task<StoredFile?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(file => file.Id == id));

        public Task<IReadOnlyList<StoredFile>> ListCleanupCandidatesAsync(FileRetentionPolicy policy, DateTime nowUtc, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredFile>>(Items.Where(file => file.IsCleanupCandidate(policy, nowUtc)).Take(max).ToArray());
    }

    /// <summary>Hashing in-memory storage; <see cref="Unavailable"/> simulates an outage and <see cref="CorruptReads"/> a bad read-back.</summary>
    internal sealed class FakeStorage : IFileStorage
    {
        public ConcurrentDictionary<Guid, byte[]> Objects { get; } = new();
        public bool Unavailable { get; set; }
        public bool UnavailableOnRead { get; set; }
        public bool CorruptReads { get; set; }
        public Action? OnWrite { get; set; }

        public async Task<StoredContent> WriteAsync(Guid fileId, Stream content, string mediaType, long maxBytes, CancellationToken cancellationToken)
        {
            if (Unavailable)
                throw new FileStorageUnavailableException("synthetic outage");
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length > maxBytes)
                throw new FileContentTooLargeException(maxBytes);
            if (!Objects.TryAdd(fileId, buffer.ToArray()))
                throw new InvalidOperationException("never overwritten");
            OnWrite?.Invoke();
            return new StoredContent(buffer.Length, Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())));
        }

        public Task<Stream?> OpenReadAsync(Guid fileId, CancellationToken cancellationToken)
        {
            if (Unavailable || UnavailableOnRead)
                throw new FileStorageUnavailableException("synthetic outage");
            if (!Objects.TryGetValue(fileId, out var bytes))
                return Task.FromResult<Stream?>(null);
            return Task.FromResult<Stream?>(new MemoryStream(CorruptReads ? [.. bytes, 0] : bytes));
        }

        public Task<bool> DeleteAsync(Guid fileId, CancellationToken cancellationToken) =>
            Unavailable ? throw new FileStorageUnavailableException("synthetic outage") : Task.FromResult(Objects.TryRemove(fileId, out _));

        public Task<bool> ExistsAsync(Guid fileId, CancellationToken cancellationToken) => Task.FromResult(Objects.ContainsKey(fileId));
    }

    /// <summary>Records the built content and emits a minimal output with the right signature (or a configured failure).</summary>
    internal sealed class FakeRenderer : IPdfRenderer, ISpreadsheetWriter
    {
        public List<ReportDocument> Documents { get; } = [];
        public List<WorkbookDocument> Workbooks { get; } = [];
        public Exception? Failure { get; set; }
        public byte[]? Output { get; set; }
        public Action? DuringRender { get; set; }

        public async Task RenderAsync(ReportDocument document, Stream output, CancellationToken cancellationToken)
        {
            Documents.Add(document);
            await EmitAsync(output, "%PDF-1.7 synthetic"u8.ToArray(), cancellationToken);
        }

        public async Task WriteAsync(WorkbookDocument workbook, Stream output, CancellationToken cancellationToken)
        {
            Workbooks.Add(workbook);
            await EmitAsync(output, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3], cancellationToken);
        }

        private async Task EmitAsync(Stream output, byte[] defaultOutput, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DuringRender?.Invoke();
            if (Failure is { } failure)
                throw failure;
            await output.WriteAsync(Output ?? defaultOutput, cancellationToken);
        }
    }

    private sealed class FakeScanner(ReportingTestDoubles owner) : IMalwareScanner
    {
        public Task<MalwareScanVerdict> ScanAsync(Guid fileId, string mediaType, CancellationToken cancellationToken) => Task.FromResult(owner.ScanVerdict);
    }

    /// <summary><see cref="IReportSourceReader"/> over the procurement fakes (untracked semantics are irrelevant in memory).</summary>
    internal sealed class FakeSources(ProcurementTestDoubles procurement) : IReportSourceReader
    {
        public Task<FiscalPeriod?> FindFiscalPeriodAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(procurement.Periods.Items.SingleOrDefault(period => period.Id == id));

        public Task<IReadOnlyList<BudgetAllocation>> ListAllocationsAsync(Guid fiscalPeriodId, string? currency, Guid? costCenterId, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BudgetAllocation>>(procurement.Allocations.Items
                .Where(allocation => allocation.FiscalPeriodId == fiscalPeriodId && (currency is null || allocation.Currency == currency)
                    && (costCenterId is null || allocation.CostCenterId == costCenterId))
                .Take(max + 1).ToArray());

        public Task<IReadOnlyDictionary<Guid, (string Code, string Name)>> GetCostCenterLabelsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<Guid, (string Code, string Name)> labels = ids
                .Select(id => procurement.CostCenter.Id == id ? procurement.CostCenter : null)
                .OfType<CostCenter>()
                .ToDictionary(costCenter => costCenter.Id, costCenter => (costCenter.Code, costCenter.Name));
            return Task.FromResult(labels);
        }

        public Task<IReadOnlyList<RequestForQuotation>> ListRfqsWithQuotesAsync(DateTime fromUtc, DateTime toUtc, Guid? supplierId, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequestForQuotation>>(procurement.Rfqs.Items
                .Where(rfq => rfq.Quotes.Any(quote => quote.Revisions.Any(revision => revision.ReceivedAtUtc >= fromUtc && revision.ReceivedAtUtc < toUtc))
                    && (supplierId is null || rfq.Invitations.Any(invitation => invitation.SupplierId == supplierId)))
                .Take(max + 1).ToArray());
    }
}
