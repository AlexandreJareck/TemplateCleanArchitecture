using System;
using System.Collections.Generic;
using System.Linq;
using Template.Domain.Common;

namespace Template.Domain.Budgeting;

public enum BudgetAllocationStatus
{
    Active,
    Closed
}

/// <summary>
/// Budget of one cost center in one currency for one fiscal period (BUD-002), owning its reservations (BUD-005..008).
/// <see cref="ReservedAmount"/> and <see cref="CommittedAmount"/> are maintained by the behavior methods, so the
/// availability (CALC-003) is exact even when Persistence loads only the reservations of the requisition being changed;
/// a database check constraint additionally keeps it non-negative. Every change increments the version.
/// </summary>
public sealed class BudgetAllocation : VersionedAggregateRoot
{
    private readonly List<BudgetReservation> reservations = [];
    private readonly List<BudgetAllocationAmountChange> amountHistory = [];
    private readonly DomainEventBuffer events = new();

    private BudgetAllocation()
    {
        Currency = string.Empty;
    }

    public Guid FiscalPeriodId { get; private set; }
    public Guid CostCenterId { get; private set; }
    public string Currency { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Σ amounts of reservations in <c>Reserved</c>.</summary>
    public decimal ReservedAmount { get; private set; }

    /// <summary>Σ amounts of reservations in <c>Committed</c>.</summary>
    public decimal CommittedAmount { get; private set; }

    public BudgetAllocationStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>CALC-003: amount − (reserved + committed); never negative (BUD-005).</summary>
    public decimal AvailableAmount => Amount - ReservedAmount - CommittedAmount;

    public Money Available => new(AvailableAmount, Currency);

    /// <summary>Loaded reservations (Persistence may load only the requisition's reservations; totals stay exact).</summary>
    public IReadOnlyCollection<BudgetReservation> Reservations => reservations;

    public IReadOnlyCollection<BudgetAllocationAmountChange> AmountHistory => amountHistory;

    public IReadOnlyList<DomainEvent> DomainEvents => events.Events;

    public void ClearDomainEvents() => events.Clear();

    /// <summary>
    /// Creates an <c>Active</c> allocation (BUD-002). The cost center must be active (ORG-003); the period must not be
    /// closed; the currency must be supported by the organization (checked by the caller, which owns the settings).
    /// </summary>
    public static (BudgetAllocation? Allocation, DomainError? Error) Create(
        Guid id, FiscalPeriod period, MasterDataReference costCenter, string? currency, bool isSupportedCurrency, decimal amount,
        Guid? createdBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(costCenter);
        var code = CurrencyCodes.Normalize(currency);
        if (code is null || !isSupportedCurrency)
            return (null, BudgetErrors.Validation("currency", "The currency must be one of the organization's supported currencies.", "BUD-002"));
        if (!IsValidAmount(amount, code))
            return (null, BudgetErrors.InvalidAmount("amount"));
        if (period.Status == FiscalPeriodStatus.Closed)
            return (null, BudgetErrors.PeriodClosed);
        if (!costCenter.IsActive)
            return (null, new DomainError("organization.inactive", DomainErrorKind.BusinessRule, "The cost center is inactive.", "ORG-003"));

        var allocation = new BudgetAllocation
        {
            Id = id,
            FiscalPeriodId = period.Id,
            CostCenterId = costCenter.Id,
            Currency = code,
            Amount = amount,
            Status = BudgetAllocationStatus.Active,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        };
        allocation.amountHistory.Add(new BudgetAllocationAmountChange(Guid.NewGuid(), id, 1, 0m, amount, null, createdBy, nowUtc));
        return (allocation, null);
    }

    /// <summary>BUD-003: reason required; a decrease never makes availability negative; history is appended.</summary>
    public DomainError? ChangeAmount(decimal newAmount, string? reason, Guid? actorId, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedReason = MasterDataRules.NormalizeReason(reason);
        if (normalizedReason is null)
            return DomainError.ReasonRequired("BUD-003", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (!IsValidAmount(newAmount, Currency))
            return BudgetErrors.InvalidAmount("amount");
        if (Status != BudgetAllocationStatus.Active)
            return DomainError.InvalidTransition(Status.ToString(), "A closed allocation cannot change.", "BUD-003");
        if (newAmount < ReservedAmount + CommittedAmount)
            return BudgetErrors.BelowReserved(ReservedAmount + CommittedAmount);
        if (newAmount == Amount)
            return null;

        amountHistory.Add(new BudgetAllocationAmountChange(
            Guid.NewGuid(), Id, amountHistory.Count + 1, Amount, newAmount, normalizedReason, actorId, nowUtc));
        Amount = newAmount;
        Touch(nowUtc);
        return null;
    }

    /// <summary><c>Active → Closed</c> with reason; requires no <c>Reserved</c> reservation (BUD-011 per allocation).</summary>
    public DomainError? Close(string? reason, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (MasterDataRules.NormalizeReason(reason) is null)
            return DomainError.ReasonRequired("BUD-011", MasterDataRules.ReasonMinLength, MasterDataRules.ReasonMaxLength);
        if (Status != BudgetAllocationStatus.Active)
            return DomainError.InvalidTransition(Status.ToString(), "The allocation is already closed.", "BUD-011");
        if (ReservedAmount > 0)
            return BudgetErrors.AllocationHasOpenReservations;

        Status = BudgetAllocationStatus.Closed;
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// BUD-004..008: reserves the requisition's estimated total. Idempotent per (requisition, revision): repeating the
    /// same reservation returns the existing one without change. A different active reservation of the same requisition
    /// is a conflict (BUD-008); an amount above availability fails with <c>budget.insufficient</c> (BUD-005) and
    /// changes nothing. The caller holds the allocation row lock (BUD-006).
    /// </summary>
    public (BudgetReservation? Reservation, bool Created, DomainError? Error) Reserve(
        Guid reservationId, Guid requisitionId, int requisitionRevision, Money amount, FiscalPeriod period, DateOnly submissionDate,
        Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(reservationId, nameof(reservationId));
        MasterDataRules.EnsureId(requisitionId, nameof(requisitionId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentNullException.ThrowIfNull(period);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (period.Id != FiscalPeriodId)
            throw new ArgumentException("The period does not own this allocation.", nameof(period));

        var active = reservations.FirstOrDefault(reservation => reservation.RequisitionId == requisitionId && reservation.IsActive);
        if (active is not null)
            return active.RequisitionRevision == requisitionRevision && active.Amount == amount.Amount && active.Currency == amount.Currency
                ? (active, false, null)
                : (null, false, BudgetErrors.ReservationExists);

        if (Status != BudgetAllocationStatus.Active || !period.AcceptsReservationsOn(submissionDate)
            || !string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
            return (null, false, BudgetErrors.NoOpenAllocation);
        if (amount.Amount <= 0 || !CalculationRules.HasMinorUnitPrecision(amount.Amount, Currency))
            return (null, false, BudgetErrors.InvalidAmount("estimatedTotal"));
        if (amount.Amount > AvailableAmount)
            return (null, false, BudgetErrors.Insufficient(Available, amount));

        var reservation = new BudgetReservation(reservationId, Id, requisitionId, requisitionRevision, amount.Amount, Currency, nowUtc);
        reservation.Record(BudgetReservationOperation.Reserved, null, BudgetReservationStatus.Reserved, null, actorId, nowUtc, correlationId);
        reservations.Add(reservation);
        ReservedAmount += amount.Amount;
        events.Raise(new BudgetReservationCreated(nowUtc, reservation.Id, requisitionId, Id, amount));
        Touch(nowUtc);
        return (reservation, true, null);
    }

    /// <summary>
    /// D23-4 / BUD-010 path: <c>Reserved → Committed</c> on final approval (consumed by Phase 31). Idempotent: an
    /// already committed reservation is returned unchanged; there is nothing to commit for a released one.
    /// </summary>
    public (BudgetReservation? Reservation, bool Changed, DomainError? Error) Commit(Guid requisitionId, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var reservation = reservations.FirstOrDefault(candidate => candidate.RequisitionId == requisitionId && candidate.IsActive);
        if (reservation is null)
            return (null, false, DomainError.InvalidTransition(BudgetReservationStatus.Released.ToString(), "The requisition has no active reservation.", "BUD-010"));
        if (reservation.Status == BudgetReservationStatus.Committed)
            return (reservation, false, null);

        reservation.MarkCommitted(nowUtc);
        reservation.Record(BudgetReservationOperation.Committed, BudgetReservationStatus.Reserved, BudgetReservationStatus.Committed, null, actorId, nowUtc, correlationId);
        ReservedAmount -= reservation.Amount;
        CommittedAmount += reservation.Amount;
        events.Raise(new BudgetReservationCommitted(nowUtc, reservation.Id, requisitionId, new Money(reservation.Amount, Currency)));
        Touch(nowUtc);
        return (reservation, true, null);
    }

    /// <summary>
    /// BUD-007: releases the requisition's active reservation exactly once and records the reason code. Releasing again
    /// (or when nothing is active) is a no-op that returns <c>Changed = false</c>; availability never moves twice.
    /// </summary>
    public (BudgetReservation? Reservation, bool Changed) Release(
        Guid requisitionId, BudgetReleaseReason reason, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        var reservation = reservations.FirstOrDefault(candidate => candidate.RequisitionId == requisitionId && candidate.IsActive);
        if (reservation is null)
            return (reservations.Where(candidate => candidate.RequisitionId == requisitionId).OrderBy(candidate => candidate.CreatedAtUtc).LastOrDefault(), false);

        var from = reservation.Status;
        if (from == BudgetReservationStatus.Reserved)
            ReservedAmount -= reservation.Amount;
        else
            CommittedAmount -= reservation.Amount;
        reservation.MarkReleased(reason, nowUtc);
        reservation.Record(BudgetReservationOperation.Released, from, BudgetReservationStatus.Released, reason, actorId, nowUtc, correlationId);
        events.Raise(new BudgetReservationReleased(nowUtc, reservation.Id, requisitionId, new Money(reservation.Amount, Currency), reason));
        Touch(nowUtc);
        return (reservation, true);
    }

    /// <summary>
    /// BUD-009 (Phase 29): on award submission the requisition's <c>Reserved</c> reservation is adjusted to the award
    /// amount in requisition currency (CALC-007). An increase must fit the available budget (BUD-005, the caller holds
    /// the allocation row lock, BUD-006); a decrease returns the difference. Idempotent per award (BUD-007).
    /// </summary>
    public (BudgetReservation? Reservation, bool Changed, DomainError? Error) AdjustForAward(
        Guid requisitionId, Money amount, Guid awardId, Guid? actorId, DateTime nowUtc, string correlationId)
    {
        MasterDataRules.EnsureId(awardId, nameof(awardId));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var reservation = reservations.FirstOrDefault(candidate => candidate.RequisitionId == requisitionId && candidate.IsActive);
        if (reservation is not { Status: BudgetReservationStatus.Reserved })
            return (null, false, DomainError.InvalidTransition(
                (reservation?.Status ?? BudgetReservationStatus.Released).ToString(), "Only a Reserved reservation is adjusted to an award.", "BUD-009"));
        if (reservation.LastAwardId == awardId)
            return (reservation, false, null);
        if (!string.Equals(amount.Currency, Currency, StringComparison.Ordinal))
            throw new ArgumentException("The award amount must be in the allocation currency (D23-17).", nameof(amount));
        if (!IsValidAmount(amount.Amount, Currency))
            return (null, false, BudgetErrors.InvalidAmount("amount"));

        var previous = reservation.Amount;
        var increase = amount.Amount - previous;
        if (increase > AvailableAmount)
            return (null, false, BudgetErrors.Insufficient(Available, new Money(increase, Currency)) with { RuleId = "BUD-009" });

        ReservedAmount += increase;
        reservation.AdjustTo(amount.Amount, awardId, nowUtc);
        reservation.Record(BudgetReservationOperation.Adjusted, BudgetReservationStatus.Reserved, BudgetReservationStatus.Reserved, null, actorId, nowUtc,
            correlationId, awardId);
        events.Raise(new BudgetReservationAdjusted(nowUtc, reservation.Id, requisitionId, new Money(previous, Currency), amount, awardId));
        Touch(nowUtc);
        return (reservation, true, null);
    }

    private static bool IsValidAmount(decimal amount, string currency) =>
        amount > 0 && amount <= CalculationRules.MaxAmount && CalculationRules.HasMinorUnitPrecision(amount, currency);

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}

/// <summary><c>procurement.budget.reservation-created</c> (event catalog, Phase 28 Outbox).</summary>
public sealed record BudgetReservationCreated(DateTime OccurredAtUtc, Guid ReservationId, Guid RequisitionId, Guid AllocationId, Money Amount)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.budget.reservation-created";
}

/// <summary><c>procurement.budget.reservation-committed</c>.</summary>
public sealed record BudgetReservationCommitted(DateTime OccurredAtUtc, Guid ReservationId, Guid RequisitionId, Money Amount)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.budget.reservation-committed";
}

/// <summary><c>procurement.budget.reservation-released</c> with the BUD-007 reason code.</summary>
public sealed record BudgetReservationReleased(DateTime OccurredAtUtc, Guid ReservationId, Guid RequisitionId, Money ReleasedAmount, BudgetReleaseReason Reason)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.budget.reservation-released";
}

/// <summary><c>procurement.budget.reservation-adjusted</c> (Phase 29, BUD-009).</summary>
public sealed record BudgetReservationAdjusted(DateTime OccurredAtUtc, Guid ReservationId, Guid RequisitionId, Money PreviousAmount, Money NewAmount, Guid AwardId)
    : DomainEvent(OccurredAtUtc)
{
    public override string EventType => "procurement.budget.reservation-adjusted";
}
