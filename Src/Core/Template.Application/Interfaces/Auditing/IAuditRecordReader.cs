#nullable enable
using Template.Application.Common.Paging;

namespace Template.Application.Interfaces.Auditing;

/// <summary>Read model of an audit record (AUD-004). Details contain allow-listed keys only (AUD-003).</summary>
public sealed record AuditRecordView(
    Guid Id,
    DateTime OccurredAt,
    Guid? ActorId,
    string Action,
    string ResourceType,
    string ResourceId,
    string? FromState,
    string? ToState,
    IReadOnlyList<string> RuleIds,
    string? Reason,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Details);

/// <summary>Optional filters of the global audit query; every value is matched exactly, dates as [from, to).</summary>
public sealed record AuditRecordFilter(
    string? Action = null,
    Guid? ActorId = null,
    string? ResourceType = null,
    string? ResourceId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public interface IAuditRecordReader
{
    /// <summary>Sort fields accepted by <see cref="ListAsync"/>; <c>id</c> is always appended as the tie-breaker.</summary>
    public static readonly IReadOnlyList<string> SortFields = ["occurredAt", "action", "resourceType"];

    Task<PagedResult<AuditRecordView>> ListAsync(AuditRecordFilter filter, PageRequest page, CancellationToken cancellationToken);
}
