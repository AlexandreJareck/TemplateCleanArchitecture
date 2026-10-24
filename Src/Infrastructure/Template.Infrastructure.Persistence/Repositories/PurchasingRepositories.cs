using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Template.Application.Common.Paging;
using Template.Application.Interfaces.Purchasing;
using Template.Application.Messaging.Procurement;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.Infrastructure.Persistence.Repositories;

public sealed class PurchaseOrderRepository(ApplicationDbContext context) : IPurchaseOrderRepository
{
    private static readonly Dictionary<string, Expression<Func<PurchaseOrder, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["number"] = order => order.Number!,
        ["status"] = order => order.Status,
        ["createdAt"] = order => order.CreatedAtUtc,
        ["updatedAt"] = order => order.UpdatedAtUtc,
        ["issuedAt"] = order => order.IssuedAtUtc!,
        ["total"] = order => order.Total
    };

    public void Add(PurchaseOrder order) => context.Add(order);

    public Task<PurchaseOrder?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(order => order.Id == id, cancellationToken);

    /// <summary>
    /// Locks the order row (RCV-003 serialization), then reads it fresh: an instance tracked before the lock is detached
    /// first so the aggregate reflects every receipt committed before this transaction acquired the lock.
    /// </summary>
    public async Task<PurchaseOrder?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        await RowLocks.LockAsync(context, "PurchasingOrders", id, RowLocks.ForUpdate, cancellationToken);
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(entry => entry.Entity is PurchaseOrder order && order.Id == id
                         || entry.Entity is PurchaseOrderLine line && line.PurchaseOrderId == id
                         || entry.Entity is PurchaseOrderHistoryEntry history && history.PurchaseOrderId == id)
                     .ToArray())
            entry.State = EntityState.Detached;
        return await FindAsync(id, cancellationToken);
    }

    public Task<PurchaseOrder?> FindByAwardAsync(Guid awardId, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(order => order.AwardId == awardId, cancellationToken);

    public Task<PurchaseOrder?> FindByDocumentJobAsync(Guid reportJobId, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(order => order.DocumentJobId == reportJobId, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<PurchaseOrder>().AsNoTracking().Where(order => order.Id == id)
            .Select(order => (long?)order.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<PagedResult<PurchaseOrder>> ListAsync(PurchaseOrderFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = context.Set<PurchaseOrder>().AsNoTracking().Include(order => order.Lines).AsQueryable();
        var visibility = filter.Visibility;
        if (!visibility.ReadAll)
        {
            var readIssued = visibility.ReadIssued;
            var userId = visibility.UserId ?? Guid.Empty;
            query = query.Where(order => (readIssued && order.Status != PurchaseOrderStatus.Draft) || order.RequesterId == userId);
        }

        if (filter.Statuses is { Count: > 0 } statuses)
            query = query.Where(order => statuses.Contains(order.Status));
        if (filter.DocumentStatuses is { Count: > 0 } documents)
            query = query.Where(order => documents.Contains(order.DocumentStatus));
        if (filter.NotificationStatuses is { Count: > 0 } notifications)
            query = query.Where(order => notifications.Contains(order.NotificationStatus));
        if (filter.SupplierId is { } supplierId)
            query = query.Where(order => order.SupplierId == supplierId);
        if (filter.RequisitionId is { } requisitionId)
            query = query.Where(order => order.RequisitionId == requisitionId);
        if (filter.Number is { } number)
            query = query.Where(order => (order.Number != null && order.Number.Contains(number)) || order.RequisitionNumber.Contains(number)
                || order.SupplierLegalName.Contains(number));
        return query.AsSplitQuery().ToPagedAsync(page, SortKeys, order => order.Id, cancellationToken);
    }

    private IQueryable<PurchaseOrder> Full() =>
        context.Set<PurchaseOrder>()
            .Include(order => order.Lines)
            .Include(order => order.History)
            .AsSplitQuery();
}

/// <summary>
/// SP-2 gapless counter: on PostgreSQL an upsert that runs inside the issue transaction (the caller began it), so the
/// counter row lock is held until commit — parallel issues serialize and a rolled-back issue returns its number.
/// Non-relational providers (functional tests) derive the next value from the committed orders of the year.
/// </summary>
public sealed class PurchaseOrderNumberAllocator(ApplicationDbContext context) : IPurchaseOrderNumberAllocator
{
    private static readonly object InMemoryGate = new();

    public async Task<long> NextAsync(int year, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            var prefix = $"PO-{year:0000}-";
            var numbers = await context.Set<PurchaseOrder>().AsNoTracking()
                .Where(order => order.Number != null && order.Number.StartsWith(prefix)).Select(order => order.Number!).ToListAsync(cancellationToken);
            lock (InMemoryGate)
                return numbers.Count == 0 ? 1 : numbers.Max(number => long.Parse(number[prefix.Length..], System.Globalization.CultureInfo.InvariantCulture)) + 1;
        }

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Purchase-order numbers are allocated inside the issue transaction (SP-2).");
        var values = await context.Database.SqlQuery<long>($"""
            INSERT INTO "PurchasingOrderNumberCounters" ("Year", "LastValue") VALUES ({year}, 1)
            ON CONFLICT ("Year") DO UPDATE SET "LastValue" = "PurchasingOrderNumberCounters"."LastValue" + 1
            RETURNING "LastValue" AS "Value"
            """).ToListAsync(cancellationToken);
        return values.Single();
    }
}

public sealed class ReceiptRepository(ApplicationDbContext context) : IReceiptRepository
{
    private static readonly Dictionary<string, Expression<Func<Receipt, object>>> SortKeys = new(StringComparer.Ordinal)
    {
        ["number"] = receipt => receipt.Number,
        ["receivedOn"] = receipt => receipt.ReceivedOn,
        ["postedAt"] = receipt => receipt.PostedAtUtc
    };

    public void Add(Receipt receipt) => context.Add(receipt);

    public Task<Receipt?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Full().SingleOrDefaultAsync(receipt => receipt.Id == id, cancellationToken);

    public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        context.Set<Receipt>().AsNoTracking().Where(receipt => receipt.Id == id)
            .Select(receipt => (long?)receipt.Version).SingleOrDefaultAsync(cancellationToken);

    public Task<PagedResult<Receipt>> ListAsync(ReceiptFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Full().AsNoTracking();
        if (filter.PurchaseOrderId is { } orderId)
            query = query.Where(receipt => receipt.PurchaseOrderId == orderId);
        if (filter.ReceiverId is { } receiverId)
            query = query.Where(receipt => receipt.ReceiverId == receiverId);
        if (filter.HasDiscrepancy is { } discrepancy)
            query = query.Where(receipt => receipt.HasDiscrepancy == discrepancy);
        if (filter.Number is { } number)
            query = query.Where(receipt => receipt.Number.Contains(number) || receipt.PurchaseOrderNumber.Contains(number));
        if (filter.ReceivedFrom is { } from)
            query = query.Where(receipt => receipt.ReceivedOn >= from);
        if (filter.ReceivedTo is { } to)
            query = query.Where(receipt => receipt.ReceivedOn <= to);
        return query.ToPagedAsync(page, SortKeys, receipt => receipt.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<Receipt>> ListForOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken) =>
        await Full().AsNoTracking().Where(receipt => receipt.PurchaseOrderId == purchaseOrderId)
            .OrderBy(receipt => receipt.PostedAtUtc).ThenBy(receipt => receipt.Id).ToListAsync(cancellationToken);

    private IQueryable<Receipt> Full() =>
        context.Set<Receipt>()
            .Include(receipt => receipt.Lines)
            .Include(receipt => receipt.Corrections)
            .Include(receipt => receipt.Attachments)
            .AsSplitQuery();
}

/// <summary>Per-year receipt counter: an atomic upsert in its own statement (gaps allowed). Non-relational providers use a process-wide counter.</summary>
public sealed class ReceiptNumberAllocator(ApplicationDbContext context) : IReceiptNumberAllocator
{
    private static readonly ConcurrentDictionary<int, long> InMemoryCounters = new();

    public async Task<long> NextAsync(int year, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return InMemoryCounters.AddOrUpdate(year, 1, (_, value) => value + 1);

        var values = await context.Database.SqlQuery<long>($"""
            INSERT INTO "ReceivingReceiptNumberCounters" ("Year", "LastValue") VALUES ({year}, 1)
            ON CONFLICT ("Year") DO UPDATE SET "LastValue" = "ReceivingReceiptNumberCounters"."LastValue" + 1
            RETURNING "LastValue" AS "Value"
            """).ToListAsync(cancellationToken);
        return values.Single();
    }
}

/// <summary>
/// Inbox entry inside the caller's transaction (INT-003). On PostgreSQL the insert of an already processed (consumer,
/// event) pair does nothing; a concurrent duplicate waits for the first transaction and then finds the row. The entry is
/// written completed because it commits with the effect.
/// </summary>
public sealed class TransactionalInbox(ApplicationDbContext context) : ITransactionalInbox
{
    public async Task<bool> TryAcquireAsync(string consumerName, IntegrationEventEnvelope envelope, DateTime receivedAtUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(envelope);
        if (!context.Database.IsRelational())
        {
            if (await context.Set<InboxMessage>().FindAsync([consumerName, envelope.EventId], cancellationToken) is not null)
                return false;
            var inbox = new InboxMessage(consumerName, envelope.EventId, envelope.EventType, receivedAtUtc);
            inbox.Complete(receivedAtUtc);
            context.Add(inbox);
            return true;
        }

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The transactional Inbox requires an active unit-of-work transaction.");
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "MessagingInbox" ("ConsumerName", "EventId", "EventType", "ReceivedAtUtc", "CompletedAtUtc")
            VALUES ({{consumerName}}, {{envelope.EventId}}, {{envelope.EventType}}, {{receivedAtUtc}}, {{receivedAtUtc}})
            ON CONFLICT ("ConsumerName", "EventId") DO NOTHING
            """, cancellationToken);
        return inserted == 1;
    }
}

/// <summary>
/// Local supplier-notification adapter (Phase 32, D32-5): records one delivery per (event, supplier, template) in the
/// notification table of the same transaction. No supplier is contacted; contact data is never read. E-mail delivery
/// arrives in Phase 33 behind the same port.
/// </summary>
public sealed class LocalSupplierOrderNotifier(ApplicationDbContext context) : ISupplierOrderNotifier
{
    public Task NotifyAsync(SupplierOrderNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        context.Add(new NotificationDelivery(
            Guid.NewGuid(), ProcurementConsumers.Purchasing, notification.EventId, "procurement.purchase-order.notification-requested",
            "Supplier", notification.SupplierId, notification.Template, nameof(PurchaseOrder), notification.PurchaseOrderId, notification.RequestedAtUtc));
        return Task.CompletedTask;
    }
}
