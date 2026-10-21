#nullable enable
using System.Globalization;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Catalog;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;

namespace Template.Application.Features.Budgeting;

public sealed record FiscalPeriodView(
    Guid Id, string Code, string Name, DateOnly StartDate, DateOnly EndDate, string Status, DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? OpenedAt, DateTime? ClosedAt, string Version)
{
    public static FiscalPeriodView From(FiscalPeriod period) => new(
        period.Id, period.Code, period.Name, period.StartDate, period.EndDate, period.Status.ToString(), period.CreatedAtUtc,
        period.UpdatedAtUtc, period.OpenedAtUtc, period.ClosedAtUtc, VersionPrecondition.Format(period.Version));
}

public sealed record AllocationAmountChangeView(int Sequence, MoneyView PreviousAmount, MoneyView NewAmount, string? Reason, Guid? ActorId, DateTime At);

/// <summary>
/// Allocation with server-computed totals (CALC-003): <c>available = amount − reserved − committed</c>. The amount
/// history is included on the detail representation only.
/// </summary>
public sealed record BudgetAllocationView(
    Guid Id,
    Guid FiscalPeriodId,
    Guid CostCenterId,
    string Currency,
    MoneyView Amount,
    MoneyView Reserved,
    MoneyView Committed,
    MoneyView Available,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Version,
    IReadOnlyList<AllocationAmountChangeView>? AmountHistory)
{
    public static BudgetAllocationView From(BudgetAllocation allocation, bool includeHistory = false) => new(
        allocation.Id, allocation.FiscalPeriodId, allocation.CostCenterId, allocation.Currency,
        BudgetMoney.Of(allocation.Amount, allocation.Currency), BudgetMoney.Of(allocation.ReservedAmount, allocation.Currency),
        BudgetMoney.Of(allocation.CommittedAmount, allocation.Currency), BudgetMoney.Of(allocation.AvailableAmount, allocation.Currency),
        allocation.Status.ToString(), allocation.CreatedAtUtc, allocation.UpdatedAtUtc, VersionPrecondition.Format(allocation.Version),
        includeHistory
            ? allocation.AmountHistory.OrderBy(change => change.Sequence)
                .Select(change => new AllocationAmountChangeView(
                    change.Sequence, BudgetMoney.Of(change.PreviousAmount, allocation.Currency), BudgetMoney.Of(change.NewAmount, allocation.Currency),
                    change.Reason, change.ActorId, change.AtUtc))
                .ToArray()
            : null);
}

public sealed record BudgetReservationChangeView(
    int Sequence, string Operation, string? From, string To, MoneyView Amount, string? Reason, Guid? ActorId, DateTime At);

public sealed record BudgetReservationView(
    Guid Id,
    Guid AllocationId,
    Guid RequisitionId,
    int RequisitionRevision,
    MoneyView Amount,
    string Status,
    string? ReleaseReason,
    DateTime CreatedAt,
    DateTime? ReleasedAt,
    IReadOnlyList<BudgetReservationChangeView> History)
{
    public static BudgetReservationView From(BudgetReservation reservation) => new(
        reservation.Id, reservation.AllocationId, reservation.RequisitionId, reservation.RequisitionRevision,
        BudgetMoney.Of(reservation.Amount, reservation.Currency), reservation.Status.ToString(), reservation.ReleaseReason?.ToString(),
        reservation.CreatedAtUtc, reservation.ReleasedAtUtc,
        reservation.Changes.OrderBy(change => change.Sequence)
            .Select(change => new BudgetReservationChangeView(
                change.Sequence, change.Operation.ToString(), change.From?.ToString(), change.To.ToString(),
                BudgetMoney.Of(change.Amount, change.Currency), change.Reason?.ToString(), change.ActorId, change.AtUtc))
            .ToArray());
}

/// <summary>
/// Availability of one cost center and currency on a local date (BUD-012). <see cref="HasOpenAllocation"/> is
/// <c>false</c> when no <c>Open</c> period or no active allocation exists; the amounts are then <c>null</c>.
/// </summary>
public sealed record BudgetAvailabilityView(
    Guid CostCenterId,
    string Currency,
    DateOnly AsOfDate,
    bool HasOpenAllocation,
    Guid? FiscalPeriodId,
    string? FiscalPeriodCode,
    Guid? AllocationId,
    MoneyView? Amount,
    MoneyView? Reserved,
    MoneyView? Committed,
    MoneyView? Available)
{
    public static BudgetAvailabilityView None(Guid costCenterId, string currency, DateOnly date, FiscalPeriod? period) =>
        new(costCenterId, currency, date, false, period?.Id, period?.Code, null, null, null, null, null);

    public static BudgetAvailabilityView From(BudgetAllocation allocation, FiscalPeriod period, DateOnly date) =>
        new(allocation.CostCenterId, allocation.Currency, date, allocation.Status == BudgetAllocationStatus.Active, period.Id, period.Code,
            allocation.Id, BudgetMoney.Of(allocation.Amount, allocation.Currency), BudgetMoney.Of(allocation.ReservedAmount, allocation.Currency),
            BudgetMoney.Of(allocation.CommittedAmount, allocation.Currency), BudgetMoney.Of(allocation.AvailableAmount, allocation.Currency));
}

/// <summary>Audit actions, money formatting and failures of the Budgeting module (BUD-*).</summary>
public static class BudgetMoney
{
    public const string PeriodResource = "fiscal-period";
    public const string AllocationResource = "budget-allocation";
    public const string ReservationResource = "budget-reservation";

    public const string PeriodCreated = "budget.period.created";
    public const string PeriodUpdated = "budget.period.updated";
    public const string PeriodOpened = "budget.period.opened";
    public const string PeriodClosed = "budget.period.closed";
    public const string AllocationCreated = "budget.allocation.created";
    public const string AllocationAmountChanged = "budget.allocation.amount-changed";
    public const string AllocationClosed = "budget.allocation.closed";
    public const string ReservationCreated = "budget.reservation.created";
    public const string ReservationReleased = "budget.reservation.released";

    public static readonly ApplicationError PeriodCodeDuplicate = new(
        ProblemCodes.BudgetPeriodCodeDuplicate, ErrorCategory.Conflict, "Another fiscal period already uses this code.", "BUD-001");

    public static readonly ApplicationError PeriodOverlap = new(
        ProblemCodes.BudgetPeriodOverlap, ErrorCategory.Conflict, "The date range overlaps another fiscal period.", "BUD-001");

    public static readonly ApplicationError AllocationDuplicate = new(
        ProblemCodes.BudgetAllocationDuplicate, ErrorCategory.Conflict,
        "An allocation already exists for this fiscal period, cost center and currency.", "BUD-002");

    /// <summary>Wire money (amount as an invariant string, api-guidelines §2), always with the currency minor units.</summary>
    public static MoneyView Of(decimal amount, string currency) =>
        new(decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture), currency);

    /// <summary>Parses a wire decimal (invariant culture; signs, exponents and thousands separators are rejected).</summary>
    public static bool TryParse(string? value, out decimal amount) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
}
