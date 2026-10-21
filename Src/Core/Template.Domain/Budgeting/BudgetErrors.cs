using System.Collections.Generic;
using System.Globalization;
using Template.Domain.Common;

namespace Template.Domain.Budgeting;

/// <summary>Stable rejections of the Budgeting module (BUD-*).</summary>
public static class BudgetErrors
{
    public const string PeriodOverlapCode = "budget.period-overlap";
    public const string PeriodCodeDuplicateCode = "budget.period-code-duplicate";
    public const string PeriodClosedCode = "budget.period-closed";
    public const string PeriodHasOpenReservationsCode = "budget.period-has-open-reservations";
    public const string AllocationDuplicateCode = "budget.allocation-duplicate";
    public const string BelowReservedCode = "budget.below-reserved";
    public const string NoOpenAllocationCode = "budget.no-open-allocation";
    public const string InsufficientCode = "budget.insufficient";
    public const string ReservationExistsCode = "budget.reservation-exists";
    public const string AllocationHasOpenReservationsCode = "budget.allocation-has-open-reservations";

    public static readonly DomainError InvalidCode = Validation("code", "The code must match ^[A-Z0-9-]{2,20}$.", "BUD-001");

    public static readonly DomainError InvalidName = Validation("name", $"The name must have 1 to {MasterDataRules.NameMaxLength} characters.");

    public static readonly DomainError InvalidDateRange = Validation("endDate", "The end date must be on or after the start date.", "BUD-001");

    public static readonly DomainError PeriodClosed = new(
        PeriodClosedCode, DomainErrorKind.BusinessRule, "The fiscal period is closed; it accepts no new allocation or reservation.", "BUD-001");

    public static readonly DomainError PeriodHasOpenReservations = new(
        PeriodHasOpenReservationsCode, DomainErrorKind.Conflict,
        "The fiscal period still has Reserved reservations; release or commit them first.", "BUD-011");

    public static readonly DomainError AllocationHasOpenReservations = new(
        AllocationHasOpenReservationsCode, DomainErrorKind.Conflict,
        "The allocation still has Reserved reservations; release or commit them first.", "BUD-011");

    public static readonly DomainError NoOpenAllocation = new(
        NoOpenAllocationCode, DomainErrorKind.BusinessRule,
        "There is no active allocation for the cost center and currency in an Open period containing the submission date.", "BUD-004");

    public static readonly DomainError ReservationExists = new(
        ReservationExistsCode, DomainErrorKind.Conflict, "The requisition already owns an active reservation.", "BUD-008");

    public static DomainError InvalidAmount(string field) =>
        Validation(field, "The amount must be greater than zero with at most the currency minor units.", "BUD-002");

    public static DomainError BelowReserved(decimal reservedAndCommitted) => new(
        BelowReservedCode, DomainErrorKind.BusinessRule,
        "The new amount is below the reserved and committed total; availability would become negative.", "BUD-003",
        Details: new Dictionary<string, string> { ["reservedAndCommitted"] = reservedAndCommitted.ToString(NumberFormatInfo.InvariantInfo) });

    public static DomainError Insufficient(Money available, Money requested) => new(
        InsufficientCode, DomainErrorKind.BusinessRule, "The available budget is lower than the requested amount.", "BUD-005",
        Details: new Dictionary<string, string>
        {
            ["available"] = available.Amount.ToString(NumberFormatInfo.InvariantInfo),
            ["requested"] = requested.Amount.ToString(NumberFormatInfo.InvariantInfo),
            ["currency"] = requested.Currency
        });

    public static DomainError Validation(string field, string message, string? ruleId = null) =>
        new("validation", DomainErrorKind.Validation, message, ruleId, field);
}
