using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Security;
using Template.Application.Wrappers;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Requisitions;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// Requisition use cases against port fakes: authorization and ownership (REQ-001, REQ-011, BUD-012), submission with
/// reservation (REQ-004/005, BUD-004/005), release paths (REQ-007/008/009, BUD-007), audit (AUD-001/003) and
/// optimistic concurrency (SP-1).
/// </summary>
public sealed class RequisitionHandlerTests
{
    private const string Purpose = "Replacement laptops for the support team";
    private const string Reason = "Duplicated request, cancelled by the requester";
    private readonly ProcurementTestDoubles doubles = new();

    public RequisitionHandlerTests() => doubles.ActAs(doubles.Requester);

    [Fact]
    public async Task CreateDraft_UsesTheActorAsRequester_AllocatesANumber_AndAudits()
    {
        var result = await doubles.Drafts().Handle(new CreateRequisitionDraftCommand(doubles.CostCenter.Id, "brl", Purpose, ProcurementTestDoubles.Today.AddDays(10)), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Number.ShouldBe("REQ-2027-000001");
        result.Value.RequesterId.ShouldBe(doubles.Requester.Id);
        result.Value.Currency.ShouldBe("BRL");
        result.Value.AllowedActions.ShouldBe(["edit", "discard", "attach", "submit", "cancel"]);
        var audit = doubles.Audit.Records.Single();
        (audit.Action, audit.ActorId, audit.ToState).ShouldBe((RequisitionAudit.DraftCreated, (Guid?)doubles.Requester.Id, "Draft"));
        audit.Details.Keys.ShouldBe(["number", "costCenterId"], ignoreOrder: true);
    }

    [Fact]
    public async Task CreateDraft_RejectsUnknownCostCenter_AndUnsupportedCurrency()
    {
        (await doubles.Drafts().Handle(new CreateRequisitionDraftCommand(Guid.NewGuid(), "BRL", null, null), default)).Error!.FieldErrors!.Keys.ShouldContain("costCenterId");
        (await doubles.Drafts().Handle(new CreateRequisitionDraftCommand(doubles.CostCenter.Id, "JPY", null, null), default)).Error!.FieldErrors!.Keys.ShouldContain("currency");
        doubles.Requisitions.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_REQ_001_a_AnotherUser_CannotEditTheDraft_ButSeesNotFoundWithoutReadPermission()
    {
        var draft = await NewDraftAsync();
        var other = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(other.Id, SeededRoles.PermissionsByRole[SeededRoles.Requester]);
        doubles.ActAs(other);

        var result = await doubles.Drafts().Handle(AddLine(draft), default);

        result.Error!.Category.ShouldBe(ErrorCategory.NotFound); // REQ-011: no existence leak
        draft.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_REQ_001_a_ReaderWithoutEditAny_IsForbidden()
    {
        var draft = await NewDraftAsync();
        var buyer = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(buyer.Id, SeededRoles.PermissionsByRole[SeededRoles.Buyer]); // read-all, create, no edit-any
        doubles.ActAs(buyer);

        var result = await doubles.Drafts().Handle(AddLine(draft), default);

        result.Error!.Code.ShouldBe(ProblemCodes.Forbidden);
    }

    [Fact]
    public async Task T_REQ_001_b_ProxyWithEditAny_MayEdit_AndIsRecordedAsActor()
    {
        var draft = await NewDraftAsync();
        var manager = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(manager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        doubles.ActAs(manager);

        var result = await doubles.Drafts().Handle(AddLine(draft), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AllowedActions.ShouldNotContain("submit"); // submission stays with the requester (REQ-004)
        doubles.Audit.Records.Last().ActorId.ShouldBe(manager.Id);
        doubles.Audit.Records.Last().Action.ShouldBe(RequisitionAudit.DraftUpdated);
    }

    [Fact]
    public async Task Lines_UseTheCatalogItemReferences_AndRejectInactiveCatalogData()
    {
        var item = CatalogItem.Create(Guid.NewGuid(), "NB-14", "Notebook 14 inch", CatalogItemKind.Good, doubles.Category, doubles.Unit, 4500m, "BRL", null, ProcurementTestDoubles.Now).Item!;
        doubles.References.Items.Add(item);
        var draft = await NewDraftAsync();

        var result = await doubles.Drafts().Handle(
            new AddRequisitionLineCommand(draft.Id, draft.Version, new RequisitionLineRequest(item.Id, null, null, null, null, "2", new MoneyInput("4400.50", "BRL"))), default);

        result.IsSuccess.ShouldBeTrue();
        var line = result.Value.Lines.Single();
        (line.Description, line.CategoryCode, line.UnitCode, line.LineTotal.Amount).ShouldBe(("Notebook 14 inch", "IT", "UN", "8801.00"));
        result.Value.EstimatedTotal.Amount.ShouldBe("8801.00");

        item.Deactivate("Discontinued by the supplier", ProcurementTestDoubles.Now);
        var inactive = await doubles.Drafts().Handle(
            new AddRequisitionLineCommand(draft.Id, draft.Version, new RequisitionLineRequest(item.Id, null, null, null, null, "1", new MoneyInput("1", "BRL"))), default);
        inactive.Error!.Code.ShouldBe(ProblemCodes.CatalogInactive);
    }

    [Fact]
    public async Task T_REQ_002_a_MalformedDecimals_AreFieldValidationErrors()
    {
        var draft = await NewDraftAsync();

        var quantity = await doubles.Drafts().Handle(AddLine(draft, quantity: "1,5"), default);
        var price = await doubles.Drafts().Handle(AddLine(draft, price: "abc"), default);

        quantity.Error!.FieldErrors!.Keys.ShouldContain("quantity");
        price.Error!.FieldErrors!.Keys.ShouldContain("estimatedUnitPrice.amount");
    }

    [Fact]
    public async Task StaleVersion_Is412_AndMissingIfMatch_Is428()
    {
        var draft = await NewDraftAsync();

        (await doubles.Drafts().Handle(new AddRequisitionLineCommand(draft.Id, null, Line()), default)).Error!.Category.ShouldBe(ErrorCategory.PreconditionRequired);
        var stale = await doubles.Drafts().Handle(new AddRequisitionLineCommand(draft.Id, draft.Version + 5, Line()), default);
        stale.Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        stale.Error.Extensions!["version"].ShouldBe(draft.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task T_REQ_005_b_Submit_ReservesTheEstimatedTotal_InOneTransaction_WithTwoAudits()
    {
        var draft = await SubmittableDraftAsync(quantity: "2", price: "150");

        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe("Submitted");
        result.Value.AllowedActions.ShouldBe(["attach", "withdraw", "cancel"]);
        doubles.Allocation.ReservedAmount.ShouldBe(300m);
        doubles.Allocation.Reservations.Single().RequisitionId.ShouldBe(draft.Id);
        (doubles.UnitOfWork.Begins, doubles.UnitOfWork.Commits, doubles.UnitOfWork.Rollbacks).ShouldBe((1, 1, 0));
        doubles.Periods.ShareLocks.ShouldBe(1);
        doubles.Allocations.UpdateLocks.ShouldBe(1);
        var audits = doubles.Audit.Records.TakeLast(2).ToArray();
        audits.Select(record => record.Action).ShouldBe([RequisitionAudit.Submitted, BudgetMoney.ReservationCreated]);
        audits[1].Details["amount"].ShouldBe("300");
        audits.ShouldAllBe(record => record.CorrelationId == "unit-test-trace");
    }

    [Fact]
    public async Task T_BUD_005_b_InsufficientBudget_LeavesTheDraftUnchanged_AndRollsBack()
    {
        var draft = await SubmittableDraftAsync(quantity: "3", price: "400");
        var version = draft.Version;
        var audits = doubles.Audit.Records.Count;

        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetInsufficient);
        result.Error.RuleId.ShouldBe("BUD-005");
        result.Error.Category.ShouldBe(ErrorCategory.BusinessRule);
        draft.Status.ShouldBe(RequisitionStatus.Draft);
        draft.Version.ShouldBe(version);
        doubles.Allocation.ReservedAmount.ShouldBe(0m);
        doubles.Audit.Records.Count.ShouldBe(audits);
        (doubles.UnitOfWork.Commits, doubles.UnitOfWork.Rollbacks).ShouldBe((0, 1));
    }

    [Fact]
    public async Task T_BUD_004_b_ClosedPeriod_HasNoOpenAllocation()
    {
        var draft = await SubmittableDraftAsync();
        doubles.Period.Close(false, ProcurementTestDoubles.Now);

        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetNoOpenAllocation);
        draft.Status.ShouldBe(RequisitionStatus.Draft);
    }

    [Fact]
    public async Task T_BUD_004_c_CurrencyWithoutAllocation_HasNoOpenAllocation()
    {
        var draft = await NewDraftAsync(currency: "USD");
        await doubles.Drafts().Handle(AddLine(draft, currency: "USD"), default);

        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        result.Error!.Code.ShouldBe(ProblemCodes.BudgetNoOpenAllocation);
        doubles.Allocation.ReservedAmount.ShouldBe(0m);
    }

    [Fact]
    public async Task T_REQ_004_a_UnmetGuards_Are422WithFields_BeforeAnyLock()
    {
        var draft = await NewDraftAsync(purpose: null);

        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        result.Error!.Code.ShouldBe(ProblemCodes.RequisitionNotSubmittable);
        ((string[])result.Error.Extensions!["fields"]!).ShouldBe(["lines", "purpose", "neededBy"], ignoreOrder: true);
        doubles.UnitOfWork.Begins.ShouldBe(0);
    }

    [Fact]
    public async Task T_ORG_003_b_InactiveCostCenter_BlocksSubmission()
    {
        var draft = await SubmittableDraftAsync();
        doubles.CostCenter.Deactivate("Cost center merged into another", ProcurementTestDoubles.Now);

        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        ((string[])result.Error!.Extensions!["fields"]!).ShouldContain("costCenterId");
    }

    [Fact]
    public async Task Submit_IsOwnOnly_EvenForAProxy()
    {
        var draft = await SubmittableDraftAsync();
        var manager = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(manager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        doubles.ActAs(manager);

        (await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
    }

    [Fact]
    public async Task Submit_LostRace_Is412_AndNothingIsRecorded()
    {
        var draft = await SubmittableDraftAsync();
        doubles.UnitOfWork.ThrowConflict = true;
        doubles.Requisitions.ConcurrentVersion = 99;

        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);

        result.Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        result.Error.Extensions!["version"].ShouldBe("99");
        doubles.UnitOfWork.Discards.ShouldBeGreaterThan(0);
        doubles.UnitOfWork.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public async Task T_REQ_007_b_Withdraw_ReleasesTheReservationOnce_AndIncrementsTheRevision()
    {
        var draft = await SubmittedAsync();

        var result = await doubles.Transitions().Handle(new WithdrawRequisitionCommand(draft.Id, draft.Version), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe("Draft");
        result.Value.Revision.ShouldBe(2);
        doubles.Allocation.AvailableAmount.ShouldBe(1000m);
        doubles.Allocation.Reservations.Single().ReleaseReason.ShouldBe(BudgetReleaseReason.Withdrawn);
        doubles.Audit.Records.TakeLast(2).Select(record => record.Action).ShouldBe([RequisitionAudit.Withdrawn, BudgetMoney.ReservationReleased]);
        doubles.Audit.Records.Last().Details["releaseReason"].ShouldBe("Withdrawn");

        (await doubles.Transitions().Handle(new WithdrawRequisitionCommand(draft.Id, draft.Version), default)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
    }

    [Fact]
    public async Task T_REQ_008_b_Cancel_ReleasesExactlyOnce_AndASecondCancelChangesNothing()
    {
        var draft = await SubmittedAsync();

        var result = await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default);
        var second = await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default);

        result.Value.Status.ShouldBe("Cancelled");
        result.Value.AllowedActions.ShouldBe(["copy"]);
        second.Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        doubles.Allocation.AvailableAmount.ShouldBe(1000m);
        doubles.Allocation.Reservations.Single().Changes.Count.ShouldBe(2);
        doubles.Audit.Records.Count(record => record.Action == BudgetMoney.ReservationReleased).ShouldBe(1);
    }

    [Fact]
    public async Task T_REQ_008_a_CancelRequiresAReason_AndAnAuthority()
    {
        var draft = await SubmittedAsync();
        (await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, "short"), default)).Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);

        var auditor = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(auditor.Id, SeededRoles.PermissionsByRole[SeededRoles.Auditor]); // read-all, no cancel permission
        doubles.ActAs(auditor);
        (await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
        doubles.Allocation.ReservedAmount.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task T_REQ_008_a_CancelAnyHolder_MayCancelAnotherUsersRequisition()
    {
        var draft = await SubmittedAsync();
        var manager = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(manager.Id, SeededRoles.PermissionsByRole[SeededRoles.ProcurementManager]);
        doubles.ActAs(manager);

        var result = await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default);

        result.Value.Status.ShouldBe("Cancelled");
        doubles.Audit.Records.Single(record => record.Action == RequisitionAudit.Cancelled).ActorId.ShouldBe(manager.Id);
    }

    [Fact]
    public async Task T_REQ_009_a_ExpiryWithFakeClock_ReleasesOnce_AsTheSystem()
    {
        var draft = await SubmittedAsync();
        doubles.ActAsSystem();

        doubles.Clock.UtcNow = ProcurementTestDoubles.Now.AddDays(40);
        (await doubles.Expiry().Handle(new ExpireDueRequisitionsCommand(), default)).Value.ShouldBe(0);
        doubles.Clock.UtcNow = ProcurementTestDoubles.Now.AddDays(42);
        (await doubles.Expiry().Handle(new ExpireDueRequisitionsCommand(), default)).Value.ShouldBe(1);
        (await doubles.Expiry().Handle(new ExpireRequisitionCommand(draft.Id), default)).Value.ShouldBeFalse(); // already expired: no-op

        draft.Status.ShouldBe(RequisitionStatus.Expired);
        doubles.Allocation.Reservations.Single().ReleaseReason.ShouldBe(BudgetReleaseReason.Expired);
        doubles.Allocation.AvailableAmount.ShouldBe(1000m);
        var audit = doubles.Audit.Records.Single(record => record.Action == RequisitionAudit.Expired);
        audit.ActorId.ShouldBeNull();
        audit.FromState.ShouldBe("Submitted");
    }

    [Fact]
    public async Task Discard_OnlyForNeverSubmittedDrafts()
    {
        var draft = await NewDraftAsync();
        (await doubles.Drafts().Handle(new DiscardRequisitionDraftCommand(draft.Id, draft.Version), default)).IsSuccess.ShouldBeTrue();
        doubles.Requisitions.Items.ShouldBeEmpty();

        var submitted = await SubmittedAsync();
        await doubles.Transitions().Handle(new WithdrawRequisitionCommand(submitted.Id, submitted.Version), default);
        (await doubles.Drafts().Handle(new DiscardRequisitionDraftCommand(submitted.Id, submitted.Version), default)).Error!.Code
            .ShouldBe(ProblemCodes.RequisitionImmutable);
    }

    [Fact]
    public async Task Attachments_AreAppendOnlyAfterSubmission_AndMapTo413And415()
    {
        var draft = await SubmittedAsync();

        (await doubles.Drafts().Handle(new AddRequisitionAttachmentCommand(draft.Id, draft.Version, "big.pdf", "application/pdf", 20_000_000), default))
            .Error!.Category.ShouldBe(ErrorCategory.PayloadTooLarge);
        (await doubles.Drafts().Handle(new AddRequisitionAttachmentCommand(draft.Id, draft.Version, "x.exe", "application/octet-stream", 10), default))
            .Error!.Category.ShouldBe(ErrorCategory.UnsupportedMediaType);
        var added = await doubles.Drafts().Handle(new AddRequisitionAttachmentCommand(draft.Id, draft.Version, "quote.pdf", "application/pdf", 1024), default);
        added.Value.Attachments.Single().LockedAtRevision.ShouldBe(1);
        (await doubles.Drafts().Handle(new RemoveRequisitionAttachmentCommand(draft.Id, draft.Version, added.Value.Attachments.Single().Id), default))
            .Error!.Code.ShouldBe(ProblemCodes.FileImmutable);
    }

    [Fact]
    public async Task T_REQ_011_a_ListScopes_AndTeamVisibility()
    {
        var mine = await SubmittedAsync();
        var manager = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(manager.Id, SeededRoles.PermissionsByRole[SeededRoles.CostCenterManager]);
        doubles.Scope.Scopes[manager.Id] = new OrganizationScope([doubles.CostCenter.Id], []);
        doubles.ActAs(manager);

        var team = await doubles.Queries().Handle(new ListRequisitionsQuery(Scope: "visible"), default);
        var own = await doubles.Queries().Handle(new ListRequisitionsQuery(), default);
        var detail = await doubles.Queries().Handle(new GetRequisitionQuery(mine.Id), default);

        team.Value.Items.Select(item => item.Id).ShouldBe([mine.Id]);
        own.Value.Items.ShouldBeEmpty();
        detail.Value.AllowedActions.ShouldBeEmpty();
        (await doubles.Queries().Handle(new ListRequisitionsQuery(Scope: "everything"), default)).Error!.FieldErrors!.Keys.ShouldContain("scope");

        doubles.Scope.Scopes.Clear();
        (await doubles.Queries().Handle(new GetRequisitionQuery(mine.Id), default)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public async Task Reads_WithoutAnyRequisitionPermission_Are403()
    {
        var administrator = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(administrator.Id, SeededRoles.PermissionsByRole[SeededRoles.Administrator]);
        doubles.ActAs(administrator);

        (await doubles.Queries().Handle(new ListRequisitionsQuery(), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);
    }

    [Fact]
    public async Task T_BUD_012_a_RequesterSeesAvailabilityOfOwnDraftOnly_BudgetReadersAlways()
    {
        var draft = await SubmittableDraftAsync();

        var own = await doubles.Queries().Handle(new GetRequisitionBudgetAvailabilityQuery(draft.Id), default);
        own.Value.HasOpenAllocation.ShouldBeTrue();
        own.Value.Available!.Amount.ShouldBe("1000.00");

        await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);
        (await doubles.Queries().Handle(new GetRequisitionBudgetAvailabilityQuery(draft.Id), default)).Error!.Code.ShouldBe(ProblemCodes.Forbidden);

        var controller = new ProcurementTestDoubles.Actor();
        doubles.Permissions.Grant(controller.Id, SeededRoles.PermissionsByRole[SeededRoles.BudgetController]);
        doubles.ActAs(controller);
        (await doubles.Queries().Handle(new GetRequisitionBudgetAvailabilityQuery(draft.Id), default)).Value.Reserved!.Amount.ShouldBe("100.00");
    }

    [Fact]
    public async Task Timeline_CombinesStateAndReservationHistory_InOrder()
    {
        var draft = await SubmittableDraftAsync();
        doubles.Clock.UtcNow = ProcurementTestDoubles.Now.AddMinutes(1);
        await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);
        doubles.Clock.UtcNow = ProcurementTestDoubles.Now.AddMinutes(2);
        await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default);

        var timeline = await doubles.Queries().Handle(new GetRequisitionTimelineQuery(draft.Id), default);

        timeline.Value.Entries.Select(entry => (entry.Kind, entry.To)).ShouldBe(
        [
            ("state", "Draft"), ("reservation", "Reserved"), ("state", "Submitted"), ("reservation", "Released"), ("state", "Cancelled")
        ]);
        timeline.Value.Entries.Last().Reason.ShouldBe(Reason);
    }

    [Fact]
    public async Task Copy_CreatesANewDraftFromATerminalRequisition()
    {
        var draft = await SubmittedAsync();
        (await doubles.Transitions().Handle(new CopyRequisitionCommand(draft.Id), default)).Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        await doubles.Transitions().Handle(new CancelRequisitionCommand(draft.Id, draft.Version, Reason), default);

        var copy = await doubles.Transitions().Handle(new CopyRequisitionCommand(draft.Id), default);

        copy.Value.SourceRequisitionId.ShouldBe(draft.Id);
        copy.Value.Status.ShouldBe("Draft");
        copy.Value.Lines.Count.ShouldBe(1);
        doubles.Audit.Records.Last().Action.ShouldBe(RequisitionAudit.Copied);
    }

    private async Task<PurchaseRequisition> NewDraftAsync(string? purpose = Purpose, string currency = "BRL")
    {
        var result = await doubles.Drafts().Handle(
            new CreateRequisitionDraftCommand(doubles.CostCenter.Id, currency, purpose, purpose is null ? null : ProcurementTestDoubles.Today.AddDays(10)), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return doubles.Requisitions.Items.Single(item => item.Id == result.Value.Id);
    }

    private async Task<PurchaseRequisition> SubmittableDraftAsync(string quantity = "1", string price = "100")
    {
        var draft = await NewDraftAsync();
        (await doubles.Drafts().Handle(AddLine(draft, quantity, price), default)).IsSuccess.ShouldBeTrue();
        return draft;
    }

    private async Task<PurchaseRequisition> SubmittedAsync()
    {
        var draft = await SubmittableDraftAsync();
        var result = await doubles.Transitions().Handle(new SubmitRequisitionCommand(draft.Id, draft.Version), default);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return draft;
    }

    private AddRequisitionLineCommand AddLine(PurchaseRequisition draft, string quantity = "1", string price = "100", string currency = "BRL") =>
        new(draft.Id, draft.Version, Line(quantity, price, currency));

    private RequisitionLineRequest Line(string quantity = "1", string price = "100", string currency = "BRL") =>
        new(null, "Laptop 14 inch", RequisitionLineKind.Good, doubles.Category.Id, doubles.Unit.Id, quantity, new MoneyInput(price, currency));
}
