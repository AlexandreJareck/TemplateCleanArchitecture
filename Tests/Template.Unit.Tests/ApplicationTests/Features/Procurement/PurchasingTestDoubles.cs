using Microsoft.Extensions.Logging.Abstractions;
using Template.Application.Common.Paging;
using Template.Application.Features.Purchasing;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Messaging.Procurement;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;
using Template.Unit.Tests.ApplicationTests.Features.MasterData;
using Template.Unit.Tests.ApplicationTests.Features.Reporting;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Phase 32 port fakes: purchase orders and receipts kept in memory, the gapless order counter, the transactional Inbox
/// and the supplier notifier. Synthetic data only.
/// </summary>
internal sealed partial class ProcurementTestDoubles
{
    public FakeOrders Orders { get; } = new();
    public FakeOrderNumbers OrderNumbers { get; } = new();
    public FakeReceipts Receipts { get; } = new();
    public FakeOrderNumbers ReceiptNumbers { get; } = new();
    public FakeInbox Inbox { get; } = new();
    public FakeNotifier Notifier { get; } = new();
    public MasterDataTestDoubles.FakeSuppliers SupplierEntities { get; } = new();
    public ReportingTestDoubles.FakeJobs ReportJobs { get; } = new();
    public ReportingTestDoubles.FakeFiles Files { get; } = new();
    public ReportingTestDoubles.FakeStorage Storage { get; } = new();

    public PurchaseOrderHandlers PurchaseOrders()
    {
        var writer = NewWriter();
        return new(Orders, OrderNumbers, Requisitions, Allocations, Rfqs, Approvals, Approvers, SupplierEntities, ReportJobs, Files, Storage,
            new PurchasingAccess(writer), writer);
    }

    public ReceiptHandlers ReceiptHandlers()
    {
        var writer = NewWriter();
        return new(Receipts, ReceiptNumbers, Orders, Requisitions, new PurchasingAccess(writer), writer);
    }

    public PurchasingEventHandler PurchasingEvents() =>
        new(Inbox, Orders, Requisitions, Rfqs, Approvals, SupplierEntities, References, ReportJobs, Notifier, NewWriter(),
            NullLogger<PurchasingEventHandler>.Instance);

    internal sealed class FakeOrders : IPurchaseOrderRepository
    {
        public List<PurchaseOrder> Items { get; } = [];
        public int UpdateLocks { get; private set; }
        public long? ConcurrentVersion { get; set; }

        public void Add(PurchaseOrder order) => Items.Add(order);

        public Task<PurchaseOrder?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        /// <summary>Runs once when the next row lock is taken: simulates a change committed by another request meanwhile.</summary>
        public Action<PurchaseOrder>? OnLock { get; set; }

        public Task<PurchaseOrder?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
        {
            UpdateLocks++;
            var order = Items.SingleOrDefault(item => item.Id == id);
            if (order is not null && OnLock is { } concurrent)
            {
                OnLock = null;
                concurrent(order);
            }

            return Task.FromResult(order);
        }

        public Task<PurchaseOrder?> FindByAwardAsync(Guid awardId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.AwardId == awardId));

        public Task<PurchaseOrder?> FindByDocumentJobAsync(Guid reportJobId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.DocumentJobId == reportJobId));

        public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ConcurrentVersion ?? Items.Where(item => item.Id == id).Select(item => (long?)item.Version).SingleOrDefault());

        public Task<PagedResult<PurchaseOrder>> ListAsync(PurchaseOrderFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            var visible = Items.Where(item => filter.Visibility.CanRead(item)
                && (filter.Statuses is null || filter.Statuses.Contains(item.Status))
                && (filter.DocumentStatuses is null || filter.DocumentStatuses.Contains(item.DocumentStatus))
                && (filter.NotificationStatuses is null || filter.NotificationStatuses.Contains(item.NotificationStatus))
                && (filter.SupplierId is null || item.SupplierId == filter.SupplierId)
                && (filter.RequisitionId is null || item.RequisitionId == filter.RequisitionId)
                && (filter.Number is null || (item.Number ?? string.Empty).Contains(filter.Number, StringComparison.Ordinal)
                    || item.RequisitionNumber.Contains(filter.Number, StringComparison.Ordinal))).ToArray();
            return Task.FromResult(new PagedResult<PurchaseOrder>(visible.Skip(page.Skip).Take(page.PageSize).ToArray(), page.Page, page.PageSize, visible.Length));
        }
    }

    /// <summary>Counter that returns its value only when the caller commits (rolled-back numbers are reused, like SP-2).</summary>
    internal sealed class FakeOrderNumbers : IPurchaseOrderNumberAllocator, IReceiptNumberAllocator
    {
        public long Last { get; set; }
        public int Calls { get; private set; }

        public Task<long> NextAsync(int year, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(++Last);
        }
    }

    internal sealed class FakeReceipts : IReceiptRepository
    {
        public List<Receipt> Items { get; } = [];

        public void Add(Receipt receipt) => Items.Add(receipt);

        public Task<Receipt?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(item => item.Id == id).Select(item => (long?)item.Version).SingleOrDefault());

        public Task<PagedResult<Receipt>> ListAsync(ReceiptFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            var matches = Items.Where(item => (filter.PurchaseOrderId is null || item.PurchaseOrderId == filter.PurchaseOrderId)
                && (filter.HasDiscrepancy is null || item.HasDiscrepancy == filter.HasDiscrepancy)
                && (filter.ReceivedFrom is null || item.ReceivedOn >= filter.ReceivedFrom)
                && (filter.ReceivedTo is null || item.ReceivedOn <= filter.ReceivedTo)
                && (filter.Number is null || item.Number.Contains(filter.Number, StringComparison.Ordinal))).ToArray();
            return Task.FromResult(new PagedResult<Receipt>(matches, page.Page, page.PageSize, matches.Length));
        }

        public Task<IReadOnlyList<Receipt>> ListForOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Receipt>>(Items.Where(item => item.PurchaseOrderId == purchaseOrderId).ToArray());
    }

    internal sealed class FakeInbox : ITransactionalInbox
    {
        public HashSet<(string Consumer, Guid EventId)> Entries { get; } = [];

        public Task<bool> TryAcquireAsync(string consumerName, IntegrationEventEnvelope envelope, DateTime receivedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Entries.Add((consumerName, envelope.EventId)));
    }

    /// <summary>Records deliveries; <see cref="FailureCode"/> makes the channel refuse.</summary>
    internal sealed class FakeNotifier : ISupplierOrderNotifier
    {
        public List<SupplierOrderNotification> Sent { get; } = [];
        public string? FailureCode { get; set; }

        public Task NotifyAsync(SupplierOrderNotification notification, CancellationToken cancellationToken)
        {
            if (FailureCode is { } code)
                throw new SupplierNotificationException(code, "synthetic channel refusal");
            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }
}
