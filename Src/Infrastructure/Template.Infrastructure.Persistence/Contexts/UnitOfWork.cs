using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Template.Application.Common.Concurrency;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Sourcing;
using Template.Application.Interfaces.Suppliers;

namespace Template.Infrastructure.Persistence.Contexts;

public sealed class UnitOfWork(ApplicationDbContext dbContext) : IUnitOfWork
{
    /// <summary>
    /// Unique indexes that guard aggregate concurrency in addition to the version token: two rotations of one session
    /// family insert the same (family, sequence) pair, and PostgreSQL may report that before the version check.
    /// </summary>
    private static readonly HashSet<string> ConcurrencyGuardConstraints = new(StringComparer.Ordinal)
    {
        "IX_AccessRefreshTokens_FamilyId_Sequence",
        "IX_SupplierStatusChanges_SupplierId_Sequence",
        // Phase 27: history sequences of the budget and requisition aggregates, and BUD-008 (one active reservation
        // per requisition) when two writers race on different allocations.
        "IX_BudgetAllocationAmountChanges_AllocationId_Sequence",
        "IX_BudgetReservationChanges_ReservationId_Sequence",
        "IX_PurchaseRequisitionStateChanges_RequisitionId_Sequence",
        "IX_PurchaseRequisitionLines_RequisitionId_LineNumber",
        "IX_PurchaseRequisitionSubmissions_RequisitionId_Revision",
        IBudgetAllocationRepository.ActiveReservationUniqueKey,
        // Phase 29: sourcing history, invitation, revision, comparison and award guards of the RFQ aggregate.
        "IX_SourcingRfqHistory_RfqId_Sequence",
        "IX_SourcingRfqInvitations_RfqId_Sequence",
        "IX_SourcingRfqInvitations_RfqId_SupplierId_Active",
        "IX_SourcingQuotes_RfqId_SupplierId",
        "IX_SourcingQuoteRevisions_QuoteId_RevisionNumber",
        "IX_SourcingComparisons_RfqId_Sequence",
        "IX_SourcingAwards_RfqId_Pending",
        "IX_SourcingRfqs_RequisitionId_RoundNumber",
        "IX_ApprovalHistory_InstanceId_Sequence",
        "IX_ApprovalSteps_InstanceId_Level"
    };

    /// <summary>
    /// Business-key unique indexes (ORG-001, CAT-001, SUP-001; RPT-001 one active report job per request fingerprint). A violation means a concurrent duplicate that raced past
    /// the use-case pre-check; it surfaces as <see cref="UniqueConstraintViolationException"/> so the use case answers
    /// with its duplicate code. Any other violation (primary keys, append-only audit IDs) keeps the original exception.
    /// </summary>
    private static readonly HashSet<string> BusinessKeyConstraints = new(StringComparer.Ordinal)
    {
        IDepartmentRepository.CodeUniqueKey,
        ICostCenterRepository.CodeUniqueKey,
        ICategoryRepository.CodeUniqueKey,
        IUnitOfMeasureRepository.CodeUniqueKey,
        ICatalogItemRepository.CodeUniqueKey,
        ISupplierRepository.TaxIdUniqueKey,
        IFiscalPeriodRepository.CodeUniqueKey,
        IFiscalPeriodRepository.NoOverlapKey,
        IBudgetAllocationRepository.UniqueKey,
        IRequestForQuotationRepository.ActiveRfqUniqueKey,
        Template.Application.Interfaces.Reporting.IReportJobRepository.ActiveUniqueKey,
        Template.Application.Interfaces.Approvals.IApprovalInstanceRepository.AwardUniqueKey,
        Template.Application.Interfaces.Approvals.IApprovalPolicyRepository.VersionUniqueKey
    };

    private IDbContextTransaction? transaction;

    /// <summary>EF InMemory has no transactions; a logical scope keeps the same call sequence (functional tests only).</summary>
    private bool logicalTransaction;

    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken) > 0;
        }
        catch (DbUpdateConcurrencyException error)
        {
            throw new ConcurrencyConflictException("A versioned aggregate changed after it was read.", error);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } constraint
        } && ConcurrencyGuardConstraints.Contains(constraint))
        {
            throw new ConcurrencyConflictException("A concurrent change of the same aggregate won.", error);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ExclusionViolation, ConstraintName: { } constraint
        } && BusinessKeyConstraints.Contains(constraint))
        {
            throw new UniqueConstraintViolationException(constraint, error);
        }
        catch (Exception error) when (IsTransientConflict(error))
        {
            // Phase 27: concurrent inserts checked by the period exclusion constraint (or rows locked in opposite
            // order) can deadlock; PostgreSQL aborts one transaction, which then lost the race like a version conflict.
            throw new ConcurrencyConflictException("A concurrent transaction won a deadlock or serialization race.", error);
        }
    }

    private static bool IsTransientConflict(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure })
                return true;
        }

        return false;
    }

    public void DiscardChanges() => dbContext.ChangeTracker.Clear();

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (transaction is not null || logicalTransaction)
            throw new InvalidOperationException("A unit-of-work transaction is already active.");

        if (!dbContext.Database.IsRelational())
        {
            logicalTransaction = true;
            return;
        }

        transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task<bool> CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (logicalTransaction)
        {
            logicalTransaction = false;
            return await SaveChangesAsync(cancellationToken);
        }

        var activeTransaction = transaction
            ?? throw new InvalidOperationException("No unit-of-work transaction is active.");

        try
        {
            var changed = await SaveChangesAsync(cancellationToken);
            await activeTransaction.CommitAsync(cancellationToken);
            return changed;
        }
        catch
        {
            await activeTransaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (logicalTransaction)
        {
            logicalTransaction = false;
            dbContext.ChangeTracker.Clear();
            return;
        }

        var activeTransaction = transaction
            ?? throw new InvalidOperationException("No unit-of-work transaction is active.");

        try
        {
            await activeTransaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        logicalTransaction = false;
        if (transaction is null)
            return;

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            await DisposeTransactionAsync();
        }
    }

    private async ValueTask DisposeTransactionAsync()
    {
        if (transaction is null)
            return;

        await transaction.DisposeAsync();
        transaction = null;
    }
}
