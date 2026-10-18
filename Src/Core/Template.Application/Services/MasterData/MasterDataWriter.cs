#nullable enable
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Auditing;
using Template.Application.Wrappers;
using Template.Domain.Auditing;

namespace Template.Application.Services.MasterData;

/// <summary>
/// Shared write path of the Phase 25 master-data use cases: the actor, audit records built with the injected clock
/// and request correlation (AUD-001, AUD-003), and a commit that turns a lost optimistic-concurrency race into 412
/// and a unique-index race into the module's duplicate error. The audit record and the change share one commit.
/// </summary>
public sealed class MasterDataWriter(
    IUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock,
    IRequestContext requestContext,
    IAuthenticatedUserService currentUser)
{
    public DateTime UtcNow => clock.UtcNow;

    /// <summary>The authenticated user, or <c>null</c> (system).</summary>
    public Guid? ActorId => Guid.TryParse(currentUser.UserId, out var id) && id != Guid.Empty ? id : null;

    public void Audit(
        string action,
        string resourceType,
        Guid resourceId,
        string? fromState = null,
        string? toState = null,
        string? reason = null,
        IEnumerable<string>? ruleIds = null,
        IReadOnlyDictionary<string, string>? details = null,
        DateTime? occurredAtUtc = null) =>
        auditTrail.Append(AuditRecord.Create(
            Guid.NewGuid(),
            occurredAtUtc ?? clock.UtcNow,
            ActorId,
            action,
            resourceType,
            resourceId.ToString(),
            requestContext.CorrelationId,
            fromState,
            toState,
            ruleIds,
            reason: reason,
            details: details));

    /// <summary>
    /// Commits. Returns <c>null</c> on success, 412 <c>concurrency.version-mismatch</c> (with the current version read
    /// by <paramref name="currentVersion"/> when available) on a concurrency conflict, or the result of
    /// <paramref name="uniqueViolation"/> for a violated unique index (rethrown when it returns <c>null</c>).
    /// </summary>
    public async Task<ApplicationError?> CommitAsync(
        Func<CancellationToken, Task<long?>> currentVersion,
        Func<string, ApplicationError?>? uniqueViolation,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            var version = await currentVersion(cancellationToken);
            return version is null
                ? new ApplicationError(ProblemCodes.VersionMismatch, ErrorCategory.PreconditionFailed, "The resource changed since it was read.")
                : VersionPrecondition.Check(-1, version.Value);
        }
        catch (UniqueConstraintViolationException violation) when (uniqueViolation?.Invoke(violation.ConstraintName) is not null)
        {
            unitOfWork.DiscardChanges();
            return uniqueViolation(violation.ConstraintName);
        }
    }
}
