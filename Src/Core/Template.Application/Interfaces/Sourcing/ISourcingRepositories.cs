#nullable enable
using Template.Application.Common.Paging;
using Template.Domain.Sourcing;

namespace Template.Application.Interfaces.Sourcing;

/// <summary>RFQ list filters (D27-12 style: <see cref="Number"/> matches the RFQ or requisition number only).</summary>
public sealed record RfqFilter(
    IReadOnlyCollection<RfqStatus>? Statuses = null,
    Guid? RequisitionId = null,
    string? Number = null,
    Guid? SupplierId = null);

public interface IRequestForQuotationRepository
{
    /// <summary>SRC-001 partial unique index: one non-terminal RFQ per requisition.</summary>
    public const string ActiveRfqUniqueKey = "IX_SourcingRfqs_RequisitionId_Active";

    public const string NumberUniqueKey = "IX_SourcingRfqs_Number";

    public static readonly IReadOnlyList<string> SortFields = ["number", "status", "createdAt", "updatedAt", "deadline", "roundNumber"];

    void Add(RequestForQuotation rfq);

    /// <summary>Tracked aggregate with every child collection, or <c>null</c>.</summary>
    Task<RequestForQuotation?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked non-terminal RFQ of the requisition (SRC-001), or <c>null</c>.</summary>
    Task<RequestForQuotation?> FindActiveByRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken);

    Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Number of rounds already created for the requisition (SRC-001 round number).</summary>
    Task<int> CountRoundsAsync(Guid requisitionId, CancellationToken cancellationToken);

    /// <summary>Untracked RFQs without children.</summary>
    Task<PagedResult<RequestForQuotation>> ListAsync(RfqFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>SRC-012 sweep: <c>Published</c> RFQs whose deadline is at or before <paramref name="nowUtc"/>.</summary>
    Task<IReadOnlyList<Guid>> ListDueForCloseAsync(DateTime nowUtc, int max, CancellationToken cancellationToken);
}

/// <summary>Allocates RFQ numbers <c>RFQ-{yyyy}-{nnnnnn}</c> from a per-year counter (gaps are allowed).</summary>
public interface IRfqNumberAllocator
{
    Task<long> NextAsync(int year, CancellationToken cancellationToken);
}

/// <summary>Current supplier identity and status for invitations, publish, close and award checks (SUP-003, SRC-003).</summary>
public interface ISupplierSnapshotReader
{
    /// <summary>Snapshots of the known suppliers among <paramref name="ids"/>; unknown IDs are omitted.</summary>
    Task<IReadOnlyDictionary<Guid, SupplierSnapshot>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
