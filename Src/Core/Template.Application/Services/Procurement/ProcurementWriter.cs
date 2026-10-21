#nullable enable
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Auditing;
using Template.Application.Interfaces.Organization;
using Template.Application.Logging;
using Template.Application.Wrappers;
using Template.Domain.Auditing;
using Template.Domain.Common;
using Template.Domain.Organization;

namespace Template.Application.Services.Procurement;

/// <summary>
/// Shared write path of the Phase 27 procurement use cases: actor and effective permissions, the organization calendar
/// (B23-5), audit records in the same commit as the change (AUD-001, AUD-003), an optional unit-of-work transaction
/// that holds the budget row locks (BUD-006) and a commit that turns a lost optimistic race into 412. After a
/// successful commit the aggregates' domain events become audit-safe structured logs and metric increments.
/// </summary>
public sealed class ProcurementWriter(
    IUnitOfWork unitOfWork,
    IAuditTrail auditTrail,
    IClock clock,
    IRequestContext requestContext,
    IAuthenticatedUserService currentUser,
    IPermissionReader permissionReader,
    IOrganizationSettingsRepository organizationSettings,
    ILogger<ProcurementWriter> logger)
{
    public const string MeterName = "Template.Procurement";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DomainEventCounter = Meter.CreateCounter<long>(
        "procurement.domain_events", description: "Procurement domain events recorded after a successful commit.");
    private static readonly Counter<long> ConflictCounter = Meter.CreateCounter<long>(
        "procurement.concurrency_conflicts", description: "Procurement writes rejected by optimistic concurrency.");

    private IReadOnlySet<string>? permissions;
    private bool inTransaction;

    public DateTime UtcNow => clock.UtcNow;

    public string CorrelationId => requestContext.CorrelationId;

    /// <summary>The authenticated user, or <c>null</c> (system).</summary>
    public Guid? ActorId => Guid.TryParse(currentUser.UserId, out var id) && id != Guid.Empty ? id : null;

    /// <summary>Effective permissions of the actor (empty for the system actor), read once per use case.</summary>
    public async Task<IReadOnlySet<string>> PermissionsAsync(CancellationToken cancellationToken) =>
        permissions ??= ActorId is { } actor
            ? await permissionReader.GetEffectivePermissionsAsync(actor, cancellationToken)
            : new HashSet<string>();

    public async Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        (await PermissionsAsync(cancellationToken)).Contains(permission);

    /// <summary>Organization settings (base currency, supported currencies, time zone); seeded at startup.</summary>
    public async Task<OrganizationSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await organizationSettings.GetAsync(cancellationToken)
        ?? throw new InvalidOperationException("The organization settings have not been initialized.");

    /// <summary>Today's local date in the organization time zone (B23-5).</summary>
    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken) =>
        LocalDate(clock.UtcNow, (await SettingsAsync(cancellationToken)).TimeZone);

    public static DateOnly LocalDate(DateTime utc, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)));

    public static ApplicationError Forbidden(string message = "The actor may not perform this operation on the resource.") =>
        new(ProblemCodes.Forbidden, ErrorCategory.Forbidden, message);

    public static ApplicationError NotFound(string resource) =>
        new(ProblemCodes.NotFound, ErrorCategory.NotFound, $"The {resource} was not found.");

    public void Audit(
        string action,
        string resourceType,
        Guid resourceId,
        string? fromState = null,
        string? toState = null,
        string? reason = null,
        IEnumerable<string>? ruleIds = null,
        IReadOnlyDictionary<string, string>? details = null) =>
        auditTrail.Append(AuditRecord.Create(
            Guid.NewGuid(), clock.UtcNow, ActorId, action, resourceType, resourceId.ToString(), requestContext.CorrelationId,
            fromState, toState, ruleIds, reason: reason, details: details));

    /// <summary>Starts the transaction that holds row locks until <see cref="CommitAsync"/> or <see cref="RollbackAsync"/>.</summary>
    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        inTransaction = true;
    }

    /// <summary>Rolls back the active transaction (if any) and forgets tracked changes; the aggregates stay unchanged in storage.</summary>
    public async Task RollbackAsync()
    {
        if (inTransaction)
        {
            inTransaction = false;
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }

        unitOfWork.DiscardChanges();
    }

    /// <summary>Rolls back and returns <paramref name="error"/>, for a rejection found while holding locks.</summary>
    public async Task<ApplicationError> FailAsync(ApplicationError error)
    {
        await RollbackAsync();
        return error;
    }

    /// <summary>
    /// Commits (the transaction when one is active). Returns <c>null</c> on success, 412 with the current version read
    /// by <paramref name="currentVersion"/> on a concurrency conflict (including a concurrency-guard index), or the
    /// result of <paramref name="uniqueViolation"/> for a business-key index (rethrown when it returns <c>null</c>).
    /// </summary>
    public async Task<ApplicationError?> CommitAsync(
        Func<CancellationToken, Task<long?>> currentVersion,
        Func<string, ApplicationError?>? uniqueViolation,
        CancellationToken cancellationToken)
    {
        try
        {
            if (inTransaction)
            {
                inTransaction = false;
                await unitOfWork.CommitTransactionAsync(cancellationToken);
            }
            else
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return null;
        }
        catch (ConcurrencyConflictException)
        {
            unitOfWork.DiscardChanges();
            ConflictCounter.Add(1);
            logger.LogInformation(LogEvents.ProcurementConcurrencyConflict, "Procurement write lost an optimistic-concurrency race");
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

    /// <summary>Turns committed domain events into structured logs (IDs, types and amounts only) and metric increments.</summary>
    public void Record(IEnumerable<DomainEvent> domainEvents)
    {
        foreach (var domainEvent in domainEvents)
        {
            DomainEventCounter.Add(1, new KeyValuePair<string, object?>("event_type", domainEvent.EventType));
            logger.LogInformation(LogEvents.ProcurementEventRecorded, "Procurement event {EventType} recorded", domainEvent.EventType);
        }
    }

    /// <summary>Audit-safe log of a budget rejection (rule and currency only, never the requisition content).</summary>
    public void LogBudgetRejection(string code, string currency) =>
        logger.LogInformation(LogEvents.BudgetReservationRejected, "Budget reservation rejected with {Code} in {Currency}", code, currency);
}
