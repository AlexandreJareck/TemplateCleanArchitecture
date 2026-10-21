using Shouldly;
using Template.Domain.Budgeting;
using Template.Domain.Common;

namespace Template.Unit.Tests.DomainTests.Budgeting;

/// <summary>CALC-001..003 and BUD-001..011 rules of the shared kernel, fiscal periods and allocations, without mocks.</summary>
public sealed class BudgetingTests
{
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2027, 3, 1);
    private const string Reason = "Budget revision approved by finance";
    private const string Correlation = "trace-budget-tests";

    [Fact]
    public void T_CALC_001_a_MoneyArithmetic_RequiresTheSameCurrency()
    {
        var brl = new Money(10m, "BRL");

        brl.Add(new Money(2.5m, "BRL")).ShouldBe(new Money(12.5m, "BRL"));
        brl.Subtract(new Money(2.5m, "BRL")).ShouldBe(new Money(7.5m, "BRL"));
        Should.Throw<InvalidOperationException>(() => brl.Add(new Money(1m, "USD")));
        Should.Throw<InvalidOperationException>(() => brl.CompareTo(new Money(1m, "USD")));
    }

    [Theory]
    [InlineData("0.125", "0.13")]
    [InlineData("0.135", "0.14")]
    [InlineData("-0.125", "-0.13")]
    [InlineData("10", "10")]
    public void T_CALC_001_b_RoundsToMinorUnits_AwayFromZero(string amount, string expected)
    {
        CalculationRules.RoundMoney(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "BRL")
            .ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void T_CALC_001_c_And_T_CALC_002_a_LineTotal_KeepsFourDecimalUnitPrice_AndRoundsTheTotal()
    {
        var unitPrice = new Money(33.3333m, "BRL");

        CalculationRules.LineTotal(3m, unitPrice).ShouldBe(new Money(100.00m, "BRL"));
        unitPrice.Amount.ShouldBe(33.3333m);
        CalculationRules.LineTotal(0.5m, new Money(0.25m, "USD")).ShouldBe(new Money(0.13m, "USD"));
    }

    [Fact]
    public void T_BUD_001_a_PeriodLifecycle_PlannedOpenClosed_AndClosedIsFinal()
    {
        var period = NewPeriod();
        period.Status.ShouldBe(FiscalPeriodStatus.Planned);
        period.Close(false, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);

        period.Open(Now).ShouldBeNull();
        period.Status.ShouldBe(FiscalPeriodStatus.Open);
        period.Version.ShouldBe(2);
        period.Open(Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        period.Update("Renamed", period.StartDate, period.EndDate.AddDays(1), Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        period.Update("Renamed", period.StartDate, period.EndDate, Now).ShouldBeNull();

        period.Close(false, Now).ShouldBeNull();
        period.Status.ShouldBe(FiscalPeriodStatus.Closed);
        period.Open(Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        period.Update("Again", period.StartDate, period.EndDate, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Theory]
    [InlineData("FY", true)]
    [InlineData("fy-2027", true)]
    [InlineData("F", false)]
    [InlineData("FY_2027", false)]
    public void T_BUD_001_a_PeriodCodesFollowTheOrganizationPattern(string code, bool valid)
    {
        var (period, error) = FiscalPeriod.Create(Guid.NewGuid(), code, "Fiscal year", Today, Today.AddDays(30), null, Now);

        (period is not null).ShouldBe(valid);
        if (!valid)
            error!.RuleId.ShouldBe("BUD-001");
    }

    [Fact]
    public void T_BUD_001_a_RangesMustBeOrdered_AndOverlapIsInclusive()
    {
        FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal year", Today, Today.AddDays(-1), null, Now).Error!.Field.ShouldBe("endDate");
        var period = NewPeriod();

        period.Overlaps(period.EndDate, period.EndDate.AddDays(10)).ShouldBeTrue();
        period.Overlaps(period.EndDate.AddDays(1), period.EndDate.AddDays(10)).ShouldBeFalse();
        period.Contains(period.StartDate).ShouldBeTrue();
        period.AcceptsReservationsOn(period.StartDate).ShouldBeFalse(); // Planned
    }

    [Fact]
    public void T_BUD_011_a_PeriodWithReservedReservations_CannotClose()
    {
        var period = OpenPeriod();

        period.Close(true, Now)!.Code.ShouldBe(BudgetErrors.PeriodHasOpenReservationsCode);
        period.Status.ShouldBe(FiscalPeriodStatus.Open);
    }

    [Fact]
    public void T_BUD_002_a_Allocation_RequiresPositiveMinorUnitAmount_SupportedCurrency_ActiveCostCenter_AndOpenablePeriod()
    {
        var period = OpenPeriod();
        var costCenter = new MasterDataReference(Guid.NewGuid(), true);

        BudgetAllocation.Create(Guid.NewGuid(), period, costCenter, "BRL", true, 0m, null, Now).Error!.Field.ShouldBe("amount");
        BudgetAllocation.Create(Guid.NewGuid(), period, costCenter, "BRL", true, 10.001m, null, Now).Error!.Field.ShouldBe("amount");
        BudgetAllocation.Create(Guid.NewGuid(), period, costCenter, "JPY", false, 10m, null, Now).Error!.Field.ShouldBe("currency");
        BudgetAllocation.Create(Guid.NewGuid(), period, costCenter with { IsActive = false }, "BRL", true, 10m, null, Now).Error!.RuleId.ShouldBe("ORG-003");

        var (allocation, error) = BudgetAllocation.Create(Guid.NewGuid(), period, costCenter, "brl", true, 1000m, null, Now);
        error.ShouldBeNull();
        allocation!.Currency.ShouldBe("BRL");
        allocation.AvailableAmount.ShouldBe(1000m);
        allocation.AmountHistory.Single().NewAmount.ShouldBe(1000m);

        period.Close(false, Now);
        BudgetAllocation.Create(Guid.NewGuid(), period, costCenter, "BRL", true, 10m, null, Now).Error!.Code.ShouldBe(BudgetErrors.PeriodClosedCode);
    }

    [Fact]
    public void T_BUD_003_a_AmountChange_RequiresReason_AppendsHistory_AndNeverGoesBelowReservedAndCommitted()
    {
        var (period, allocation) = NewAllocation(1000m);
        Reserve(allocation, period, Guid.NewGuid(), 600m);

        allocation.ChangeAmount(900m, "short", null, Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        allocation.ChangeAmount(599.99m, Reason, null, Now)!.Code.ShouldBe(BudgetErrors.BelowReservedCode);
        allocation.Amount.ShouldBe(1000m);

        allocation.ChangeAmount(600m, Reason, null, Now).ShouldBeNull();
        allocation.AvailableAmount.ShouldBe(0m);
        allocation.AmountHistory.Count.ShouldBe(2);
        allocation.AmountHistory.Last().PreviousAmount.ShouldBe(1000m);
        allocation.AmountHistory.Last().Reason.ShouldBe(Reason);

        var version = allocation.Version;
        allocation.ChangeAmount(600m, Reason, null, Now).ShouldBeNull();
        allocation.Version.ShouldBe(version); // unchanged amount: no history, no version
    }

    [Fact]
    public void T_BUD_004_a_Reservation_NeedsAnOpenPeriodContainingTheDate_TheAllocationCurrency_AndAnActiveAllocation()
    {
        var (period, allocation) = NewAllocation(1000m);
        var requisition = Guid.NewGuid();

        allocation.Reserve(Guid.NewGuid(), requisition, 1, new Money(10m, "USD"), period, Today, null, Now, Correlation).Error!.Code
            .ShouldBe(BudgetErrors.NoOpenAllocationCode);
        allocation.Reserve(Guid.NewGuid(), requisition, 1, new Money(10m, "BRL"), period, period.EndDate.AddDays(1), null, Now, Correlation).Error!.Code
            .ShouldBe(BudgetErrors.NoOpenAllocationCode);

        allocation.Close(Reason, Now).ShouldBeNull();
        allocation.Reserve(Guid.NewGuid(), requisition, 1, new Money(10m, "BRL"), period, Today, null, Now, Correlation).Error!.Code
            .ShouldBe(BudgetErrors.NoOpenAllocationCode);
        allocation.ReservedAmount.ShouldBe(0m);
    }

    [Fact]
    public void T_BUD_005_a_ReservationAtExactAvailability_Succeeds_AndOneCentAboveFails()
    {
        var (period, allocation) = NewAllocation(100m);

        var above = allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(100.01m, "BRL"), period, Today, null, Now, Correlation);
        above.Error!.Code.ShouldBe(BudgetErrors.InsufficientCode);
        above.Error.Details!["available"].ShouldBe("100");
        allocation.ReservedAmount.ShouldBe(0m);
        allocation.Version.ShouldBe(1);

        var exact = allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(100m, "BRL"), period, Today, null, Now, Correlation);
        exact.Error.ShouldBeNull();
        exact.Created.ShouldBeTrue();
        allocation.AvailableAmount.ShouldBe(0m);
        allocation.DomainEvents.Single().EventType.ShouldBe("procurement.budget.reservation-created");
    }

    [Fact]
    public void T_BUD_007_a_And_T_BUD_008_a_ReserveIsIdempotentPerRevision_SecondActiveReservationConflicts_ReleaseHappensOnce()
    {
        var (period, allocation) = NewAllocation(1000m);
        var requisition = Guid.NewGuid();
        var first = Reserve(allocation, period, requisition, 300m);

        var repeat = allocation.Reserve(Guid.NewGuid(), requisition, 1, new Money(300m, "BRL"), period, Today, null, Now, Correlation);
        repeat.Created.ShouldBeFalse();
        repeat.Reservation.ShouldBeSameAs(first);
        allocation.Reserve(Guid.NewGuid(), requisition, 2, new Money(300m, "BRL"), period, Today, null, Now, Correlation).Error!.Code
            .ShouldBe(BudgetErrors.ReservationExistsCode);
        allocation.ReservedAmount.ShouldBe(300m);

        var released = allocation.Release(requisition, BudgetReleaseReason.Cancelled, null, Now, Correlation);
        released.Changed.ShouldBeTrue();
        released.Reservation!.ReleaseReason.ShouldBe(BudgetReleaseReason.Cancelled);
        allocation.AvailableAmount.ShouldBe(1000m);
        var version = allocation.Version;

        var again = allocation.Release(requisition, BudgetReleaseReason.Rejected, null, Now, Correlation);
        again.Changed.ShouldBeFalse();
        again.Reservation!.ReleaseReason.ShouldBe(BudgetReleaseReason.Cancelled);
        allocation.AvailableAmount.ShouldBe(1000m);
        allocation.Version.ShouldBe(version);
        first.Changes.Select(change => change.Operation).ShouldBe([BudgetReservationOperation.Reserved, BudgetReservationOperation.Released]);

        allocation.Release(Guid.NewGuid(), BudgetReleaseReason.Expired, null, Now, Correlation).Changed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(BudgetReleaseReason.Withdrawn)]
    [InlineData(BudgetReleaseReason.Cancelled)]
    [InlineData(BudgetReleaseReason.Rejected)]
    [InlineData(BudgetReleaseReason.Expired)]
    public void T_BUD_007_a_EveryReleaseReason_RestoresAvailabilityExactlyOnce(BudgetReleaseReason reason)
    {
        var (period, allocation) = NewAllocation(500m);
        var requisition = Guid.NewGuid();
        Reserve(allocation, period, requisition, 200m);

        allocation.Release(requisition, reason, null, Now, Correlation).Changed.ShouldBeTrue();
        allocation.Release(requisition, reason, null, Now, Correlation).Changed.ShouldBeFalse();

        allocation.AvailableAmount.ShouldBe(500m);
        allocation.DomainEvents.OfType<BudgetReservationReleased>().Single().Reason.ShouldBe(reason);
    }

    [Fact]
    public void T_CALC_003_a_Availability_CountsReservedAndCommitted_NotReleased()
    {
        var (period, allocation) = NewAllocation(1000m);
        var reserved = Guid.NewGuid();
        var committed = Guid.NewGuid();
        var released = Guid.NewGuid();
        Reserve(allocation, period, reserved, 100m);
        Reserve(allocation, period, committed, 200m);
        Reserve(allocation, period, released, 300m);

        allocation.Commit(committed, null, Now, Correlation).Changed.ShouldBeTrue();
        allocation.Commit(committed, null, Now, Correlation).Changed.ShouldBeFalse(); // idempotent
        allocation.Release(released, BudgetReleaseReason.Withdrawn, null, Now, Correlation);

        allocation.ReservedAmount.ShouldBe(100m);
        allocation.CommittedAmount.ShouldBe(200m);
        allocation.AvailableAmount.ShouldBe(700m);
        allocation.Close(Reason, Now)!.Code.ShouldBe(BudgetErrors.AllocationHasOpenReservationsCode);

        allocation.Release(committed, BudgetReleaseReason.OrderCancelled, null, Now, Correlation).Changed.ShouldBeTrue();
        allocation.CommittedAmount.ShouldBe(0m);
        allocation.Commit(released, null, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void ProgrammingErrors_Throw()
    {
        var (period, allocation) = NewAllocation(10m);
        var other = OpenPeriod();

        Should.Throw<ArgumentException>(() => allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(1m, "BRL"), other, Today, null, Now, Correlation));
        Should.Throw<ArgumentException>(() => allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(1m, "BRL"), period, Today, null, DateTime.Now, Correlation));
        Should.Throw<ArgumentException>(() => allocation.Release(Guid.NewGuid(), BudgetReleaseReason.Cancelled, null, Now, " "));
        Should.Throw<ArgumentOutOfRangeException>(() => allocation.Release(Guid.NewGuid(), (BudgetReleaseReason)99, null, Now, Correlation));
    }

    internal static FiscalPeriod NewPeriod() =>
        FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal year 2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), null, Now).Period!;

    internal static FiscalPeriod OpenPeriod()
    {
        var period = NewPeriod();
        period.Open(Now);
        return period;
    }

    private static (FiscalPeriod Period, BudgetAllocation Allocation) NewAllocation(decimal amount)
    {
        var period = OpenPeriod();
        var allocation = BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(Guid.NewGuid(), true), "BRL", true, amount, null, Now).Allocation!;
        return (period, allocation);
    }

    private static BudgetReservation Reserve(BudgetAllocation allocation, FiscalPeriod period, Guid requisition, decimal amount)
    {
        var result = allocation.Reserve(Guid.NewGuid(), requisition, 1, new Money(amount, "BRL"), period, Today, null, Now, Correlation);
        result.Error.ShouldBeNull();
        return result.Reservation!;
    }
}
