using System;
using Template.Domain.Common;

namespace Template.Domain.Budgeting;

/// <summary>Fiscal period lifecycle (BUD-001): <c>Planned → Open → Closed</c>; <c>Closed</c> is final.</summary>
public enum FiscalPeriodStatus
{
    Planned,
    Open,
    Closed
}

/// <summary>
/// Budget calendar unit (docs/product/domain-model.md §3 Budgeting). Codes are unique and immutable; local-date
/// ranges never overlap (BUD-001, also guarded by a PostgreSQL exclusion constraint), so at most one <c>Open</c>
/// period contains a date. A period closes only when no <c>Reserved</c> reservation remains (BUD-011).
/// </summary>
public sealed class FiscalPeriod : VersionedAggregateRoot
{
    private FiscalPeriod()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }

    /// <summary>First local date (organization time zone, B23-5), inclusive.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Last local date, inclusive.</summary>
    public DateOnly EndDate { get; private set; }

    public FiscalPeriodStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }

    public static (FiscalPeriod? Period, DomainError? Error) Create(
        Guid id, string? code, string? name, DateOnly startDate, DateOnly endDate, Guid? createdBy, DateTime nowUtc)
    {
        MasterDataRules.EnsureId(id, nameof(id));
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedCode = MasterDataRules.NormalizeCode(code);
        if (!MasterDataRules.IsOrganizationCode(normalizedCode))
            return (null, BudgetErrors.InvalidCode);
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return (null, BudgetErrors.InvalidName);
        if (endDate < startDate)
            return (null, BudgetErrors.InvalidDateRange);

        return (new FiscalPeriod
        {
            Id = id,
            Code = normalizedCode!,
            Name = normalizedName,
            StartDate = startDate,
            EndDate = endDate,
            Status = FiscalPeriodStatus.Planned,
            CreatedAtUtc = nowUtc,
            CreatedBy = createdBy,
            UpdatedAtUtc = nowUtc
        }, null);
    }

    /// <summary>The name may change until the period closes; dates only while <c>Planned</c>.</summary>
    public DomainError? Update(string? name, DateOnly startDate, DateOnly endDate, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        var normalizedName = MasterDataRules.NormalizeName(name);
        if (normalizedName is null)
            return BudgetErrors.InvalidName;
        if (endDate < startDate)
            return BudgetErrors.InvalidDateRange;
        if (Status == FiscalPeriodStatus.Closed)
            return DomainError.InvalidTransition(Status.ToString(), "A closed fiscal period cannot change.", "BUD-001");
        var datesChange = startDate != StartDate || endDate != EndDate;
        if (datesChange && Status != FiscalPeriodStatus.Planned)
            return DomainError.InvalidTransition(Status.ToString(), "Dates can change only while the period is Planned.", "BUD-001");
        if (!datesChange && normalizedName == Name)
            return null;

        Name = normalizedName;
        StartDate = startDate;
        EndDate = endDate;
        Touch(nowUtc);
        return null;
    }

    /// <summary><c>Planned → Open</c> (BUD-001).</summary>
    public DomainError? Open(DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != FiscalPeriodStatus.Planned)
            return DomainError.InvalidTransition(Status.ToString(), "Only a Planned fiscal period can be opened.", "BUD-001");

        Status = FiscalPeriodStatus.Open;
        OpenedAtUtc = nowUtc;
        Touch(nowUtc);
        return null;
    }

    /// <summary>
    /// <c>Open → Closed</c>. <paramref name="hasReservedReservations"/> is read by the use case under a lock that also
    /// serializes concurrent reservations in this period (BUD-011); committed reservations remain as history.
    /// </summary>
    public DomainError? Close(bool hasReservedReservations, DateTime nowUtc)
    {
        MasterDataRules.EnsureUtc(nowUtc, nameof(nowUtc));
        if (Status != FiscalPeriodStatus.Open)
            return DomainError.InvalidTransition(Status.ToString(), "Only an Open fiscal period can be closed.", "BUD-011");
        if (hasReservedReservations)
            return BudgetErrors.PeriodHasOpenReservations;

        Status = FiscalPeriodStatus.Closed;
        ClosedAtUtc = nowUtc;
        Touch(nowUtc);
        return null;
    }

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>BUD-004: reservations use an <c>Open</c> period containing the submission local date.</summary>
    public bool AcceptsReservationsOn(DateOnly date) => Status == FiscalPeriodStatus.Open && Contains(date);

    public bool Overlaps(DateOnly startDate, DateOnly endDate) => startDate <= EndDate && endDate >= StartDate;

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        IncrementVersion();
    }
}
