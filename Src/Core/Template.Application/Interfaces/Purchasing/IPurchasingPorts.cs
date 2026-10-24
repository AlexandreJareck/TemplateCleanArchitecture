#nullable enable
using Template.Application.Common.Paging;
using Template.Application.Messaging.Procurement;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;

namespace Template.Application.Interfaces.Purchasing;

/// <summary>
/// What an actor may read (PUR-011): purchasing roles and auditors read every order with prices
/// (<c>purchasing.orders.read</c>); receivers read issued orders without prices (<c>receiving.receipts.create</c> or
/// <c>receiving.receipts.read</c>); a requester reads the order of an own requisition without supplier contact data.
/// </summary>
public sealed record PurchaseOrderVisibility(Guid? UserId, bool ReadAll, bool ReadIssued)
{
    public bool CanRead(PurchaseOrder order) =>
        ReadAll || (ReadIssued && order.Status != PurchaseOrderStatus.Draft) || (UserId is { } user && order.RequesterId == user);
}

public sealed record PurchaseOrderFilter(
    PurchaseOrderVisibility Visibility,
    IReadOnlyCollection<PurchaseOrderStatus>? Statuses = null,
    Guid? SupplierId = null,
    Guid? RequisitionId = null,
    string? Number = null,
    IReadOnlyCollection<PurchaseOrderDocumentStatus>? DocumentStatuses = null,
    IReadOnlyCollection<PurchaseOrderNotificationStatus>? NotificationStatuses = null);

public interface IPurchaseOrderRepository
{
    /// <summary>PUR-001: one order per approved award (business key, D32-1).</summary>
    public const string AwardUniqueKey = "IX_PurchasingOrders_AwardId";

    public const string NumberUniqueKey = "IX_PurchasingOrders_Number";

    public static readonly IReadOnlyList<string> SortFields = ["number", "status", "createdAt", "updatedAt", "issuedAt", "total"];

    void Add(PurchaseOrder order);

    /// <summary>Tracked aggregate with lines and history, or <c>null</c>.</summary>
    Task<PurchaseOrder?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the order row lock inside the active unit-of-work transaction (serializes receipts, corrections, issue and
    /// cancellation of one order, RCV-003) and returns the tracked aggregate.
    /// </summary>
    Task<PurchaseOrder?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<PurchaseOrder?> FindByAwardAsync(Guid awardId, CancellationToken cancellationToken);

    Task<PurchaseOrder?> FindByDocumentJobAsync(Guid reportJobId, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Untracked orders with lines.</summary>
    Task<PagedResult<PurchaseOrder>> ListAsync(PurchaseOrderFilter filter, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>
/// PUR-005 / SP-2: gapless <c>PO-{yyyy}-{nnnnnn}</c>. The per-year counter row is incremented with
/// <c>UPDATE … RETURNING</c> inside the issue transaction, so its row lock serializes issues and a rollback returns
/// the number; never a PostgreSQL sequence.
/// </summary>
public interface IPurchaseOrderNumberAllocator
{
    Task<long> NextAsync(int year, CancellationToken cancellationToken);
}

public sealed record ReceiptFilter(
    Guid? PurchaseOrderId = null,
    Guid? ReceiverId = null,
    bool? HasDiscrepancy = null,
    string? Number = null,
    DateOnly? ReceivedFrom = null,
    DateOnly? ReceivedTo = null);

public interface IReceiptRepository
{
    public static readonly IReadOnlyList<string> SortFields = ["number", "receivedOn", "postedAt"];

    void Add(Receipt receipt);

    Task<Receipt?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Untracked receipts with lines and corrections.</summary>
    Task<PagedResult<Receipt>> ListAsync(ReceiptFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<Receipt>> ListForOrderAsync(Guid purchaseOrderId, CancellationToken cancellationToken);
}

/// <summary>Receipt numbers <c>RCV-{yyyy}-{nnnnnn}</c> from a per-year counter in its own statement (gaps allowed).</summary>
public interface IReceiptNumberAllocator
{
    Task<long> NextAsync(int year, CancellationToken cancellationToken);
}

/// <summary>
/// Inbox entry acquired inside the caller's unit-of-work transaction (INT-003), so a Worker effect that changes
/// aggregates commits atomically with its Inbox row: a redelivered event finds the completed entry and changes nothing.
/// </summary>
public interface ITransactionalInbox
{
    /// <summary><c>false</c> when the (consumer, event) pair was already processed.</summary>
    Task<bool> TryAcquireAsync(string consumerName, IntegrationEventEnvelope envelope, DateTime receivedAtUtc, CancellationToken cancellationToken);
}

/// <summary>Safe supplier-notification failure (D32-5): the delivery channel refused or was unreachable.</summary>
public sealed class SupplierNotificationException(string failureCode, string message) : Exception(message)
{
    public string FailureCode { get; } = failureCode;
}

/// <summary>One purchase-order notification for a supplier: IDs, number and document only; contact data stays in the adapter.</summary>
public sealed record SupplierOrderNotification(
    Guid EventId, Guid PurchaseOrderId, string Number, Guid SupplierId, Guid DocumentFileId, int Attempt, string Template, DateTime RequestedAtUtc);

/// <summary>
/// Supplier notification channel of an issued order (PUR-007). The Phase 32 adapter is local: it records the delivery
/// once per (event, supplier, template) and never contacts a real supplier; e-mail delivery is Phase 33.
/// </summary>
public interface ISupplierOrderNotifier
{
    Task NotifyAsync(SupplierOrderNotification notification, CancellationToken cancellationToken);
}
