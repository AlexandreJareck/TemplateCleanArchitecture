#nullable enable
namespace Template.Application.Interfaces.Maintenance;

/// <summary>
/// Removes operational records whose retention window ended (docs/product/non-functional-requirements.md §5). Business
/// records, audit records and history are never touched. Every call deletes at most <c>maxRows</c> rows, oldest first,
/// and returns how many it removed, so a caller repeats it until fewer than <c>maxRows</c> are returned.
/// </summary>
public interface IOperationalDataRetention
{
    /// <summary>API idempotency records whose 24 h lifetime ended at or before <paramref name="nowUtc"/>.</summary>
    Task<int> DeleteExpiredIdempotencyRecordsAsync(DateTime nowUtc, int maxRows, CancellationToken cancellationToken);

    /// <summary>Completed Inbox entries completed before <paramref name="cutoffUtc"/>.</summary>
    Task<int> DeleteCompletedInboxBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken);

    /// <summary>
    /// Session families (with their refresh-token hashes) whose absolute expiry is before <paramref name="cutoffUtc"/>;
    /// revoked or expired families stay until then for reuse detection.
    /// </summary>
    Task<int> DeleteSessionFamiliesExpiredBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken);
}
