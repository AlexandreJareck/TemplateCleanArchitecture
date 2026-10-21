using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Common;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>Fiscal-period and allocation use cases against port fakes (BUD-001..003, BUD-011, BUD-012, AUD-001).</summary>
public sealed class BudgetHandlerTests
{
    private const string Reason = "Quarterly budget revision approved";
    private readonly ProcurementTestDoubles doubles = new();

    public BudgetHandlerTests() => doubles.ActAs(doubles.Requester);

    private FiscalPeriodCommandHandlers Periods => new(doubles.Periods, doubles.Allocations, doubles.NewWriter());
    private BudgetAllocationCommandHandlers Allocations => new(doubles.Allocations, doubles.Periods, doubles.CostCenters, doubles.NewWriter());
    private BudgetAllocationQueryHandlers AllocationQueries => new(doubles.Allocations, doubles.Periods, doubles.NewWriter());

    [Fact]
    public async Task T_BUD_001_b_DuplicateCode_AndOverlap_AreConflicts_AndAValidPeriodIsAudited()
    {
        (await Periods.Handle(new CreateFiscalPeriodCommand("fy27", "Again", new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31)), default))
            .Error!.Code.ShouldBe(ProblemCodes.BudgetPeriodCodeDuplicate);
        (await Periods.Handle(new CreateFiscalPeriodCommand("FY27B", "Overlap", new DateOnly(2027, 12, 31), new DateOnly(2028, 3, 31)), default))
            .Error!.Code.ShouldBe(ProblemCodes.BudgetPeriodOverlap);

        var created = await Periods.Handle(new CreateFiscalPeriodCommand("FY28", "Fiscal year 2028", new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31)), default);

        created.Value.Status.ShouldBe("Planned");
        doubles.Audit.Records.Single().Action.ShouldBe(BudgetMoney.PeriodCreated);
    }

    [Fact]
    public async Task ConcurrentOverlap_DetectedByTheExclusionConstraint_IsStillAConflict()
    {
        doubles.UnitOfWork.UniqueViolation = IFiscalPeriodRepository.NoOverlapKey;

        var result = await Periods.Handle(new CreateFiscalPeriodCommand("FY29", "Fiscal year 2029", new DateOnly(2029, 1, 1), new DateOnly(2029, 12, 31)), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetPeriodOverlap);
    }

    [Fact]
    public async Task T_BUD_011_a_PeriodWithReservedFunds_CannotClose_UnderTheRowLock()
    {
        doubles.Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(10m, "BRL"), doubles.Period, ProcurementTestDoubles.Today, null,
            ProcurementTestDoubles.Now, "trace");

        var result = await Periods.Handle(new ChangeFiscalPeriodStatusCommand(doubles.Period.Id, doubles.Period.Version, FiscalPeriodTransition.Close), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetPeriodHasOpenReservations);
        doubles.Periods.UpdateLocks.ShouldBe(1);
        (doubles.UnitOfWork.Begins, doubles.UnitOfWork.Rollbacks).ShouldBe((1, 1));
        doubles.Period.Status.ShouldBe(FiscalPeriodStatus.Open);
    }

    [Fact]
    public async Task PeriodTransitions_OpenThenClose_AreAudited()
    {
        var created = await Periods.Handle(new CreateFiscalPeriodCommand("FY28", "Fiscal year 2028", new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31)), default);
        var opened = await Periods.Handle(new ChangeFiscalPeriodStatusCommand(created.Value.Id, 1, FiscalPeriodTransition.Open), default);
        var closed = await Periods.Handle(new ChangeFiscalPeriodStatusCommand(created.Value.Id, 2, FiscalPeriodTransition.Close), default);

        (opened.Value.Status, closed.Value.Status).ShouldBe(("Open", "Closed"));
        doubles.Audit.Records.Select(record => record.Action).ShouldBe([BudgetMoney.PeriodCreated, BudgetMoney.PeriodOpened, BudgetMoney.PeriodClosed]);
    }

    [Fact]
    public async Task T_BUD_002_a_DuplicateAllocation_IsAConflict()
    {
        var result = await Allocations.Handle(new CreateBudgetAllocationCommand(doubles.Period.Id, doubles.CostCenter.Id, "BRL", "50.00"), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetAllocationDuplicate);
    }

    [Fact]
    public async Task CreateAllocation_ParsesTheWireAmount_AndAuditsAllowListedDetails()
    {
        var bad = await Allocations.Handle(new CreateBudgetAllocationCommand(doubles.Period.Id, doubles.CostCenter.Id, "USD", "1.000,00"), default);
        var created = await Allocations.Handle(new CreateBudgetAllocationCommand(doubles.Period.Id, doubles.CostCenter.Id, "USD", "2500.50"), default);

        bad.Error!.FieldErrors!.Keys.ShouldContain("amount");
        created.Value.Available.ShouldBe(new Template.Application.Features.Catalog.MoneyView("2500.50", "USD"));
        created.Value.AmountHistory!.Single().NewAmount.Amount.ShouldBe("2500.50");
        doubles.Audit.Records.Single().Details.Keys.ShouldBe(["fiscalPeriodId", "costCenterId", "currency", "amount"], ignoreOrder: true);
    }

    [Fact]
    public async Task T_BUD_003_a_AmountBelowReserved_Is422_AndAValidChangeAppendsHistory()
    {
        doubles.Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(400m, "BRL"), doubles.Period, ProcurementTestDoubles.Today, null,
            ProcurementTestDoubles.Now, "trace");
        var version = doubles.Allocation.Version;

        var below = await Allocations.Handle(new ChangeBudgetAllocationAmountCommand(doubles.Allocation.Id, version, "399.99", Reason), default);
        var ok = await Allocations.Handle(new ChangeBudgetAllocationAmountCommand(doubles.Allocation.Id, version, "800", Reason), default);

        below.Error!.Code.ShouldBe(ProblemCodes.BudgetBelowReserved);
        ok.Value.Amount.Amount.ShouldBe("800.00");
        ok.Value.Available.Amount.ShouldBe("400.00");
        ok.Value.AmountHistory!.Count.ShouldBe(2);
        var audit = doubles.Audit.Records.Single();
        (audit.Action, audit.Reason, audit.Details["previousAmount"]).ShouldBe((BudgetMoney.AllocationAmountChanged, Reason, "1000"));
        doubles.Allocations.UpdateLocks.ShouldBe(2);
    }

    [Fact]
    public async Task AllocationClose_RequiresNoReservedFunds()
    {
        var closed = await Allocations.Handle(new CloseBudgetAllocationCommand(doubles.Allocation.Id, doubles.Allocation.Version, Reason), default);

        closed.Value.Status.ShouldBe("Closed");
        (await Allocations.Handle(new CloseBudgetAllocationCommand(doubles.Allocation.Id, doubles.Allocation.Version, Reason), default))
            .Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
    }

    [Fact]
    public async Task Availability_ReportsNoOpenAllocation_WithoutError()
    {
        var none = await AllocationQueries.Handle(new GetBudgetAvailabilityQuery(doubles.CostCenter.Id, "EUR", null), default);
        var some = await AllocationQueries.Handle(new GetBudgetAvailabilityQuery(doubles.CostCenter.Id, "BRL", null), default);
        var outside = await AllocationQueries.Handle(new GetBudgetAvailabilityQuery(doubles.CostCenter.Id, "BRL", new DateOnly(2030, 1, 1)), default);

        none.Value.HasOpenAllocation.ShouldBeFalse();
        none.Value.FiscalPeriodCode.ShouldBe("FY27");
        some.Value.Available!.Amount.ShouldBe("1000.00");
        some.Value.AsOfDate.ShouldBe(ProcurementTestDoubles.Today);
        outside.Value.FiscalPeriodId.ShouldBeNull();
    }

    [Fact]
    public async Task Lists_ValidateFilters()
    {
        var periods = new FiscalPeriodQueryHandlers(doubles.Periods);

        (await periods.Handle(new ListFiscalPeriodsQuery(Status: "Open,Bogus"), default)).Error!.FieldErrors!.Keys.ShouldContain("status");
        (await periods.Handle(new ListFiscalPeriodsQuery(Status: "1"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await periods.Handle(new ListFiscalPeriodsQuery(Status: "open"), default)).Value.Items.Count.ShouldBe(1);
        (await AllocationQueries.Handle(new ListBudgetAllocationsQuery(Status: "Frozen"), default)).Error!.FieldErrors!.Keys.ShouldContain("status");
        (await AllocationQueries.Handle(new ListBudgetAllocationsQuery(Currency: "EURO"), default)).Error!.FieldErrors!.Keys.ShouldContain("currency");
        (await AllocationQueries.Handle(new ListBudgetReservationsQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
    }
}
