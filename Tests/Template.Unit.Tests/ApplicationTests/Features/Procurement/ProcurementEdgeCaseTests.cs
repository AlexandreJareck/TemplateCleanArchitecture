using FluentValidation.TestHelper;
using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Requisitions;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Failure categories and less common paths of the Phase 27 use cases: missing resources (404), stale or absent
/// versions (412/428), validation of filters and wire decimals, no-op changes, deadlock re-evaluation, header and line
/// edits, attachment removal, system expiry and the validators that run in the MediatR pipeline.
/// </summary>
public sealed class ProcurementEdgeCaseTests
{
    private const string Reason = "Synthetic reason for edge cases";
    private const string Purpose = "Replacement laptops for the support team";
    private readonly ProcurementTestDoubles doubles = new();

    public ProcurementEdgeCaseTests() => doubles.ActAs(doubles.Requester);

    private FiscalPeriodCommandHandlers Periods => new(doubles.Periods, doubles.Allocations, doubles.NewWriter());
    private FiscalPeriodQueryHandlers PeriodQueries => new(doubles.Periods);
    private BudgetAllocationCommandHandlers Allocations => new(doubles.Allocations, doubles.Periods, doubles.CostCenters, doubles.NewWriter());
    private BudgetAllocationQueryHandlers AllocationQueries => new(doubles.Allocations, doubles.Periods, doubles.NewWriter());

    [Fact]
    public async Task PeriodCreation_LosingADeadlock_IsReevaluatedAsOverlap()
    {
        doubles.UnitOfWork.ThrowConflict = true;
        doubles.UnitOfWork.BeforeSave = () =>
        {
            doubles.Periods.Items.RemoveAll(period => period.Code == "FY30"); // the losing insert is rolled back
            doubles.Periods.Items.Add(
                FiscalPeriod.Create(Guid.NewGuid(), "WIN", "Winner", new DateOnly(2030, 1, 1), new DateOnly(2030, 12, 31), null, ProcurementTestDoubles.Now).Period!);
        };

        var result = await Periods.Handle(new CreateFiscalPeriodCommand("FY30", "Fiscal 2030", new DateOnly(2030, 1, 1), new DateOnly(2030, 12, 31)), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetPeriodOverlap);
    }

    [Fact]
    public async Task PeriodCreation_LostRaceWithoutOverlap_StaysA412()
    {
        doubles.UnitOfWork.ThrowConflict = true;
        doubles.UnitOfWork.BeforeSave = () => doubles.Periods.Items.RemoveAll(period => period.Code == "FY31");

        var result = await Periods.Handle(new CreateFiscalPeriodCommand("FY31", "Fiscal 2031", new DateOnly(2031, 1, 1), new DateOnly(2031, 12, 31)), default);

        result.Error!.Category.ShouldBe(ErrorCategory.PreconditionFailed);
    }

    [Fact]
    public async Task AllocationCreation_LostRaceAgainstTheSameKey_IsADuplicate()
    {
        var period = FiscalPeriod.Create(Guid.NewGuid(), "FY29", "Fiscal 2029", new DateOnly(2029, 1, 1), new DateOnly(2029, 12, 31), null, ProcurementTestDoubles.Now).Period!;
        doubles.Periods.Items.Add(period);
        doubles.UnitOfWork.ThrowConflict = true;
        doubles.UnitOfWork.BeforeSave = () =>
        {
            doubles.Allocations.Items.RemoveAll(allocation => allocation.FiscalPeriodId == period.Id); // rolled back
            doubles.Allocations.Items.Add(BudgetAllocation.Create(
                Guid.NewGuid(), period, new MasterDataReference(doubles.CostCenter.Id, true), "BRL", true, 1m, null, ProcurementTestDoubles.Now).Allocation!);
        };

        var result = await Allocations.Handle(new CreateBudgetAllocationCommand(period.Id, doubles.CostCenter.Id, "BRL", "10"), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetAllocationDuplicate);
    }

    [Fact]
    public async Task PeriodUpdate_ChangesDatesWhilePlanned_RejectsOverlap_And404_412()
    {
        var created = await Periods.Handle(new CreateFiscalPeriodCommand("FY28", "Fiscal 2028", new DateOnly(2028, 1, 1), new DateOnly(2028, 6, 30)), default);
        var id = created.Value.Id;

        (await Periods.Handle(new UpdateFiscalPeriodCommand(Guid.NewGuid(), 1, "x", new DateOnly(2028, 1, 1), new DateOnly(2028, 2, 1)), default))
            .Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await Periods.Handle(new UpdateFiscalPeriodCommand(id, null, "x", new DateOnly(2028, 1, 1), new DateOnly(2028, 2, 1)), default))
            .Error!.Category.ShouldBe(ErrorCategory.PreconditionRequired);
        (await Periods.Handle(new UpdateFiscalPeriodCommand(id, 1, "Fiscal 2028", new DateOnly(2028, 1, 1), new DateOnly(2028, 6, 30)), default))
            .Value.Version.ShouldBe("1"); // no change
        (await Periods.Handle(new UpdateFiscalPeriodCommand(id, 1, "Fiscal 2028", new DateOnly(2028, 1, 1), new DateOnly(2027, 1, 1)), default))
            .Error!.Category.ShouldBe(ErrorCategory.Validation);
        var updated = await Periods.Handle(new UpdateFiscalPeriodCommand(id, 1, "Fiscal 2028 H1", new DateOnly(2028, 1, 1), new DateOnly(2028, 5, 31)), default);
        (updated.Value.Name, updated.Value.EndDate, updated.Value.Version).ShouldBe(("Fiscal 2028 H1", new DateOnly(2028, 5, 31), "2"));
        (await Periods.Handle(new UpdateFiscalPeriodCommand(id, 2, "Overlap", new DateOnly(2027, 12, 1), new DateOnly(2028, 6, 30)), default))
            .Error!.Code.ShouldBe(ProblemCodes.BudgetPeriodOverlap);
    }

    [Fact]
    public async Task PeriodTransitions_ReportMissingStaleAndInvalidStates()
    {
        var missing = Guid.NewGuid();
        (await Periods.Handle(new ChangeFiscalPeriodStatusCommand(missing, 1, FiscalPeriodTransition.Open), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await Periods.Handle(new ChangeFiscalPeriodStatusCommand(missing, 1, FiscalPeriodTransition.Close), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await Periods.Handle(new ChangeFiscalPeriodStatusCommand(doubles.Period.Id, 99, FiscalPeriodTransition.Open), default)).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        (await Periods.Handle(new ChangeFiscalPeriodStatusCommand(doubles.Period.Id, 99, FiscalPeriodTransition.Close), default)).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        (await Periods.Handle(new ChangeFiscalPeriodStatusCommand(doubles.Period.Id, doubles.Period.Version, FiscalPeriodTransition.Open), default))
            .Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
    }

    [Fact]
    public async Task PeriodQueries_GetAndList()
    {
        (await PeriodQueries.Handle(new GetFiscalPeriodQuery(doubles.Period.Id), default)).Value.Status.ShouldBe("Open");
        (await PeriodQueries.Handle(new GetFiscalPeriodQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await PeriodQueries.Handle(new ListFiscalPeriodsQuery(Sort: "bogus"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await PeriodQueries.Handle(new ListFiscalPeriodsQuery(Q: new string('q', 101)), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await PeriodQueries.Handle(new ListFiscalPeriodsQuery(Q: "fy"), default)).Value.TotalItems.ShouldBe(1);
    }

    [Fact]
    public async Task AllocationCommands_RejectUnknownReferences_UnsupportedCurrency_AndBadAmounts()
    {
        (await Allocations.Handle(new CreateBudgetAllocationCommand(Guid.NewGuid(), doubles.CostCenter.Id, "BRL", "1"), default)).Error!.FieldErrors!.Keys.ShouldContain("fiscalPeriodId");
        (await Allocations.Handle(new CreateBudgetAllocationCommand(doubles.Period.Id, Guid.NewGuid(), "BRL", "1"), default)).Error!.FieldErrors!.Keys.ShouldContain("costCenterId");
        (await Allocations.Handle(new CreateBudgetAllocationCommand(doubles.Period.Id, doubles.CostCenter.Id, "JPY", "1"), default)).Error!.FieldErrors!.Keys.ShouldContain("currency");
        (await Allocations.Handle(new ChangeBudgetAllocationAmountCommand(doubles.Allocation.Id, 1, "abc", Reason), default)).Error!.FieldErrors!.Keys.ShouldContain("amount");
        (await Allocations.Handle(new ChangeBudgetAllocationAmountCommand(Guid.NewGuid(), 1, "1", Reason), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await Allocations.Handle(new ChangeBudgetAllocationAmountCommand(doubles.Allocation.Id, 7, "1", Reason), default)).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        (await Allocations.Handle(new ChangeBudgetAllocationAmountCommand(doubles.Allocation.Id, 1, "1000", Reason), default)).Value.Version.ShouldBe("1"); // unchanged
        (await Allocations.Handle(new CloseBudgetAllocationCommand(Guid.NewGuid(), 1, Reason), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await Allocations.Handle(new CloseBudgetAllocationCommand(doubles.Allocation.Id, 9, Reason), default)).Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        doubles.Audit.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task AllocationQueries_DetailFiltersReservationsAndAvailability()
    {
        doubles.Allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(10m, "BRL"), doubles.Period, ProcurementTestDoubles.Today, null, ProcurementTestDoubles.Now, "trace");

        (await AllocationQueries.Handle(new GetBudgetAllocationQuery(doubles.Allocation.Id), default)).Value.AmountHistory!.Count.ShouldBe(1);
        (await AllocationQueries.Handle(new GetBudgetAllocationQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await AllocationQueries.Handle(new ListBudgetAllocationsQuery(Status: "active", Currency: "brl"), default)).Value.Items.Single().AmountHistory.ShouldBeNull();
        (await AllocationQueries.Handle(new ListBudgetAllocationsQuery(Sort: "nope"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await AllocationQueries.Handle(new ListBudgetReservationsQuery(doubles.Allocation.Id, Status: "Reserved"), default)).Value.Items.Single().History.Count.ShouldBe(1);
        (await AllocationQueries.Handle(new ListBudgetReservationsQuery(doubles.Allocation.Id, Status: "Bogus"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await AllocationQueries.Handle(new ListBudgetReservationsQuery(doubles.Allocation.Id, Sort: "x"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await AllocationQueries.Handle(new GetBudgetAvailabilityQuery(doubles.CostCenter.Id, "EURO", null), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
    }

    [Fact]
    public void Validators_RejectMissingFields()
    {
        new CreateFiscalPeriodCommandValidator().TestValidate(new CreateFiscalPeriodCommand(null, null, null, null)).ShouldHaveValidationErrorFor(command => command.Code);
        new UpdateFiscalPeriodCommandValidator().TestValidate(new UpdateFiscalPeriodCommand(Guid.NewGuid(), 1, "", null, null)).ShouldHaveValidationErrorFor(command => command.StartDate);
        new CreateBudgetAllocationCommandValidator().TestValidate(new CreateBudgetAllocationCommand(Guid.Empty, Guid.Empty, "BR", null)).ShouldHaveValidationErrorFor(command => command.Currency);
        new ChangeBudgetAllocationAmountCommandValidator().TestValidate(new ChangeBudgetAllocationAmountCommand(Guid.NewGuid(), 1, "", Reason)).ShouldHaveValidationErrorFor(command => command.Amount);
        new GetBudgetAvailabilityQueryValidator().TestValidate(new GetBudgetAvailabilityQuery(Guid.Empty, null, null)).ShouldHaveValidationErrorFor(query => query.CostCenterId);
        new CreateRequisitionDraftCommandValidator().TestValidate(new CreateRequisitionDraftCommand(Guid.Empty, null, null, null)).ShouldHaveValidationErrorFor(command => command.Currency);
        new UpdateRequisitionDraftCommandValidator().TestValidate(new UpdateRequisitionDraftCommand(Guid.NewGuid(), 1, Guid.Empty, "BRL", null, null)).ShouldHaveValidationErrorFor(command => command.CostCenterId);
        var freeLine = new RequisitionLineRequest(null, null, null, null, null, null, null);
        var lineResult = new AddRequisitionLineCommandValidator().TestValidate(new AddRequisitionLineCommand(Guid.NewGuid(), 1, freeLine));
        lineResult.Errors.Select(error => error.PropertyName).ShouldContain("Line.Kind");
        lineResult.Errors.Select(error => error.PropertyName).ShouldContain("Line.EstimatedUnitPrice");
        new UpdateRequisitionLineCommandValidator().TestValidate(new UpdateRequisitionLineCommand(Guid.NewGuid(), 1, 1,
            new RequisitionLineRequest(Guid.NewGuid(), null, null, null, null, "1", new MoneyInput(null, null)))).Errors.Count.ShouldBe(2);
        new AddRequisitionAttachmentCommandValidator().TestValidate(new AddRequisitionAttachmentCommand(Guid.NewGuid(), 1, "", "", 0)).Errors.Count.ShouldBe(3);
    }

    [Fact]
    public async Task DraftHeaderAndLines_CanBeEdited_AndMissingReferencesAreFieldErrors()
    {
        var draft = await NewDraftAsync();
        var drafts = doubles.Drafts();

        var header = await drafts.Handle(new UpdateRequisitionDraftCommand(draft.Id, draft.Version, doubles.CostCenter.Id, "BRL", Purpose + " (v2)", ProcurementTestDoubles.Today.AddDays(20)), default);
        header.Value.Purpose.ShouldEndWith("(v2)");
        (await drafts.Handle(new UpdateRequisitionDraftCommand(draft.Id, draft.Version, doubles.CostCenter.Id, "BRL", Purpose + " (v2)", ProcurementTestDoubles.Today.AddDays(20)), default))
            .Value.Version.ShouldBe(header.Value.Version); // unchanged
        (await drafts.Handle(new UpdateRequisitionDraftCommand(draft.Id, draft.Version, Guid.NewGuid(), "BRL", null, null), default)).Error!.FieldErrors!.Keys.ShouldContain("costCenterId");

        (await drafts.Handle(new AddRequisitionLineCommand(draft.Id, draft.Version, Line(categoryId: Guid.NewGuid())), default)).Error!.FieldErrors!.Keys.ShouldContain("categoryId");
        (await drafts.Handle(new AddRequisitionLineCommand(draft.Id, draft.Version, Line(unitId: Guid.NewGuid())), default)).Error!.FieldErrors!.Keys.ShouldContain("unitOfMeasureId");
        (await drafts.Handle(new AddRequisitionLineCommand(draft.Id, draft.Version,
            new RequisitionLineRequest(Guid.NewGuid(), null, null, null, null, "1", new MoneyInput("1", "BRL"))), default)).Error!.FieldErrors!.Keys.ShouldContain("catalogItemId");

        (await drafts.Handle(new AddRequisitionLineCommand(draft.Id, draft.Version, Line()), default)).IsSuccess.ShouldBeTrue();
        var updated = await drafts.Handle(new UpdateRequisitionLineCommand(draft.Id, draft.Version, 1, Line(quantity: "5", price: "2")), default);
        updated.Value.EstimatedTotal.Amount.ShouldBe("10.00");
        (await drafts.Handle(new UpdateRequisitionLineCommand(draft.Id, draft.Version, 1, Line(quantity: "x")), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await drafts.Handle(new UpdateRequisitionLineCommand(draft.Id, draft.Version, 9, Line()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        var removed = await drafts.Handle(new RemoveRequisitionLineCommand(draft.Id, draft.Version, 1), default);
        removed.Value.Lines.ShouldBeEmpty();
        (await drafts.Handle(new RemoveRequisitionLineCommand(draft.Id, draft.Version, 1), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public async Task DraftWrites_OnASubmittedRequisition_AreImmutable_AndUnlockedAttachmentsCanBeRemoved()
    {
        var draft = await NewDraftAsync();
        var drafts = doubles.Drafts();
        var added = await drafts.Handle(new AddRequisitionAttachmentCommand(draft.Id, draft.Version, "draft.pdf", "application/pdf", 10), default);
        var removed = await drafts.Handle(new RemoveRequisitionAttachmentCommand(draft.Id, draft.Version, added.Value.Attachments.Single().Id), default);
        removed.Value.Attachments.ShouldBeEmpty();
        (await drafts.Handle(new AddRequisitionAttachmentCommand(draft.Id, draft.Version, "x.pdf", "application/pdf", -1), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);

        await drafts.Handle(new AddRequisitionLineCommand(draft.Id, draft.Version, Line()), default);
        await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        (await drafts.Handle(new AddRequisitionLineCommand(draft.Id, draft.Version, Line()), default)).Error!.Code.ShouldBe(ProblemCodes.RequisitionImmutable);
        (await drafts.Handle(new DiscardRequisitionDraftCommand(Guid.NewGuid(), 1), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public async Task Transitions_ReportMissingForbiddenAndInvalidStates()
    {
        var draft = await NewDraftAsync();
        var transitions = doubles.Transitions();

        (await transitions.Handle(new SubmitRequisitionCommand(Guid.NewGuid(), 1), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await transitions.Handle(new SubmitRequisitionCommand(draft.Id, null), default)).Error!.Category.ShouldBe(ErrorCategory.PreconditionRequired);
        (await transitions.Handle(new WithdrawRequisitionCommand(draft.Id, draft.Version), default)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        (await transitions.Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default)).Value.Status.ShouldBe("Cancelled"); // Draft: nothing reserved
        (await transitions.Handle(new CopyRequisitionCommand(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await doubles.Expiry().Handle(new ExpireRequisitionCommand(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await doubles.Expiry().Handle(new ExpireRequisitionCommand(draft.Id), default)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);

        var other = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(other.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        var second = await NewDraftAsync();
        await doubles.Drafts().Handle(new AddRequisitionLineCommand(second.Id, second.Version, Line()), default);
        await doubles.Transitions().Handle(new SubmitRequisitionCommand(second.Id, second.Version), default);
        doubles.ActAs(other);
        (await doubles.Transitions().Handle(new WithdrawRequisitionCommand(second.Id, second.Version), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        doubles.ActAsSystem();
        (await doubles.Drafts().Handle(new CreateRequisitionDraftCommand(doubles.CostCenter.Id, "BRL", null, null), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        (await doubles.Transitions().Handle(new CopyRequisitionCommand(draft.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
    }

    [Fact]
    public async Task Queries_ValidateFilters_AndHideUnreadableResources()
    {
        var draft = await NewDraftAsync();
        var queries = doubles.Queries();

        (await queries.Handle(new ListRequisitionsQuery(Status: "Nope"), default)).Error!.FieldErrors!.Keys.ShouldContain("status");
        (await queries.Handle(new ListRequisitionsQuery(Q: new string('9', 101)), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await queries.Handle(new ListRequisitionsQuery(Sort: "purpose"), default)).Error!.Category.ShouldBe(ErrorCategory.Validation);
        (await queries.Handle(new ListRequisitionsQuery(From: new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Local)), default)).Error!.FieldErrors!.Keys.ShouldContain("from");
        var filtered = await queries.Handle(new ListRequisitionsQuery(Status: "Draft", Q: "000001", From: ProcurementTestDoubles.Now.AddDays(-1), To: ProcurementTestDoubles.Now.AddDays(1)), default);
        filtered.Value.Items.Single().Id.ShouldBe(draft.Id);
        doubles.Requisitions.LastFilter!.Number.ShouldBe("000001");
        (await queries.Handle(new GetRequisitionTimelineQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await queries.Handle(new GetRequisitionBudgetAvailabilityQuery(Guid.NewGuid()), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);

        var administrator = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.Administrator]);
        doubles.ActAs(administrator);
        (await doubles.Queries().Handle(new GetRequisitionQuery(draft.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        (await doubles.Queries().Handle(new GetRequisitionTimelineQuery(draft.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        (await doubles.Queries().Handle(new GetRequisitionBudgetAvailabilityQuery(draft.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
    }

    [Fact]
    public async Task CopyOfACancelledRequisition_ByAReadAllUser_UsesTheCopierAsRequester()
    {
        var draft = await NewDraftAsync();
        await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default);
        var buyer = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(buyer.Id, SeededRoles.PermissionsByRole[SeededRoles.Buyer]);
        doubles.ActAs(buyer);

        var copy = await doubles.Transitions().Handle(new CopyRequisitionCommand(draft.Id), default);

        copy.Value.RequesterId.ShouldBe(buyer.Id);
        copy.Value.AllowedActions.ShouldContain("submit");
    }

    [Fact]
    public void ProcurementWriter_HelpersAndEnumParsing()
    {
        ProcurementWriter.Forbidden().Category.ShouldBe(ErrorCategory.Forbidden);
        ProcurementWriter.NotFound("thing").Message.ShouldContain("thing");
        ProcurementWriter.LocalDate(new DateTime(2027, 3, 1, 2, 0, 0, DateTimeKind.Utc), "America/Sao_Paulo").ShouldBe(new DateOnly(2027, 2, 28));
        ProcurementQueries.ParseEnumList<RequisitionStatus>(" ", "status").Value.ShouldBeNull();
        ProcurementQueries.ParseEnumList<RequisitionStatus>("draft,Draft", "status").Value!.Count.ShouldBe(1);
        new ApplicationError("x", ErrorCategory.Conflict, "y").Category.ShouldBe(ErrorCategory.Conflict);
        DomainErrorMapping.ToApplicationError(new DomainError("file.too-large", DomainErrorKind.TooLarge, "m", "FIL-002")).Category.ShouldBe(ErrorCategory.PayloadTooLarge);
        DomainErrorMapping.ToApplicationError(new DomainError("file.type-not-allowed", DomainErrorKind.UnsupportedMediaType, "m")).Category.ShouldBe(ErrorCategory.UnsupportedMediaType);
    }

    [Fact]
    public async Task CatalogLine_UsesServiceKindFromTheCatalogItem()
    {
        var item = CatalogItem.Create(Guid.NewGuid(), "SV-1", "Installation service", CatalogItemKind.Service, doubles.Category, doubles.Unit, null, null, null, ProcurementTestDoubles.Now).Item!;
        doubles.References.Items.Add(item);
        var draft = await NewDraftAsync();

        var result = await doubles.Drafts().Handle(new AddRequisitionLineCommand(draft.Id, draft.Version,
            new RequisitionLineRequest(item.Id, "Custom description", null, null, null, "1", new MoneyInput("99.9", "brl"))), default);

        (result.Value.Lines.Single().Kind, result.Value.Lines.Single().Description, result.Value.Lines.Single().EstimatedUnitPrice.Amount)
            .ShouldBe(("Service", "Custom description", "99.90"));
    }

    private async Task<PurchaseRequisition> NewDraftAsync()
    {
        var result = await doubles.Drafts().Handle(new CreateRequisitionDraftCommand(doubles.CostCenter.Id, "BRL", Purpose, ProcurementTestDoubles.Today.AddDays(10)), default);
        return doubles.Requisitions.Items.Single(item => item.Id == result.Value.Id);
    }

    private RequisitionLineRequest Line(string quantity = "1", string price = "100", Guid? categoryId = null, Guid? unitId = null) =>
        new(null, "Laptop 14 inch", RequisitionLineKind.Good, categoryId ?? doubles.Category.Id, unitId ?? doubles.Unit.Id, quantity, new MoneyInput(price, "BRL"));
}
