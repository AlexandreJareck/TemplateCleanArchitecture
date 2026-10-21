using Shouldly;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Requisitions;

namespace Template.Unit.Tests.DomainTests;

/// <summary>Boundary and programming-error paths of the Phase 27 aggregates and shared kernel, without mocks.</summary>
public sealed class ProcurementDomainEdgeCaseTests
{
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2027, 3, 1);
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly CostCenterSnapshot CostCenter = new(Guid.NewGuid(), "CC", "Support", Guid.NewGuid(), true);
    private static readonly CategorySnapshot Category = new(Guid.NewGuid(), "IT", "IT", true);
    private static readonly UnitSnapshot Unit = new(Guid.NewGuid(), "UN", "Unit", 0, true);
    private const string Reason = "Synthetic reason for domain edges";

    [Fact]
    public void SharedKernel_ZeroMinorUnitsAndEventBuffer()
    {
        Money.Zero("BRL").ShouldBe(new Money(0m, "BRL"));
        new Money(2m, "BRL").CompareTo(new Money(1m, "BRL")).ShouldBe(1);
        CalculationRules.MinorUnits("EUR").ShouldBe(2);
        CalculationRules.HasMinorUnitPrecision(1.001m, "BRL").ShouldBeFalse();
        var buffer = new DomainEventBuffer();
        buffer.Raise(new BudgetReservationCreated(Now, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Money(1m, "BRL")));
        buffer.Events.Count.ShouldBe(1);
        buffer.Clear();
        buffer.Events.ShouldBeEmpty();
        Should.Throw<ArgumentNullException>(() => buffer.Raise(null!));
    }

    [Fact]
    public void FiscalPeriod_RejectsInvalidNames_AndProgrammingErrors()
    {
        FiscalPeriod.Create(Guid.NewGuid(), "FY27", " ", Today, Today, null, Now).Error!.Field.ShouldBe("name");
        var period = FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal", Today, Today.AddDays(1), null, Now).Period!;
        period.Update("", Today, Today, Now)!.Field.ShouldBe("name");
        period.Update("Fiscal", Today, Today.AddDays(-1), Now)!.Field.ShouldBe("endDate");
        Should.Throw<ArgumentException>(() => FiscalPeriod.Create(Guid.Empty, "FY27", "Fiscal", Today, Today, null, Now));
        Should.Throw<ArgumentException>(() => period.Open(DateTime.Now));
        period.Open(Now);
        period.Close(false, Now);
        period.Close(false, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void Allocation_RejectsInvalidChanges_AndCommitWithoutReservation()
    {
        var period = FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), null, Now).Period!;
        period.Open(Now);
        BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(Guid.NewGuid(), true), null, true, 1m, null, Now).Error!.Field.ShouldBe("currency");
        var allocation = BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(Guid.NewGuid(), true), "BRL", true, 100m, null, Now).Allocation!;

        allocation.ChangeAmount(0m, Reason, null, Now)!.Field.ShouldBe("amount");
        allocation.Close("short", Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(0m, "BRL"), period, Today, null, Now, "t").Error!.Field.ShouldBe("estimatedTotal");
        allocation.Reserve(Guid.NewGuid(), Guid.NewGuid(), 1, new Money(1.001m, "BRL"), period, Today, null, Now, "t").Error!.Field.ShouldBe("estimatedTotal");
        allocation.Commit(Guid.NewGuid(), null, Now, "t").Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        allocation.Close(Reason, Now).ShouldBeNull();
        allocation.ChangeAmount(200m, Reason, null, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        allocation.Close(Reason, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        allocation.ClearDomainEvents();
        allocation.DomainEvents.ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => allocation.Commit(Guid.NewGuid(), null, Now, ""));
        Should.Throw<ArgumentNullException>(() => BudgetAllocation.Create(Guid.NewGuid(), null!, new MasterDataReference(Guid.NewGuid(), true), "BRL", true, 1m, null, Now));
    }

    [Fact]
    public void Requisition_LineGuards_ForKindUnitCatalogItemAndOverflow()
    {
        var requisition = Draft();

        requisition.AddLine(Line(kind: (RequisitionLineKind)9), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("kind");
        requisition.AddLine(Line(unit: Unit with { IsActive = false }), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("unitOfMeasureId");
        requisition.AddLine(Line(item: new MasterDataReference(Guid.NewGuid(), false)), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("catalogItemId");
        requisition.AddLine(Line(quantity: 99_999_999_999m, price: 99_999m), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("quantity");
        requisition.AddLine(Line(price: 100_000_000_000_000m), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("estimatedUnitPrice.amount");
        requisition.AddAttachment(Guid.NewGuid(), "a.pdf", "application/pdf", 0, null, Now).Error!.Field.ShouldBe("length");
        requisition.AddAttachment(Guid.NewGuid(), "a.pdf", null, 1, null, Now).Error!.Code.ShouldBe(RequisitionErrors.FileTypeNotAllowedCode);
        requisition.UpdateLine(1, Line(description: null), Now)!.Kind.ShouldBe(DomainErrorKind.NotFound);
        requisition.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Requisition_HeaderGuards_AndProgrammingErrors()
    {
        var requisition = Draft();
        var otherCostCenter = new CostCenterSnapshot(Guid.NewGuid(), "CC2", "Other", Guid.NewGuid(), false);

        requisition.UpdateHeader(otherCostCenter, "BRL", true, null, null, Now)!.RuleId.ShouldBe("ORG-003");
        requisition.UpdateHeader(CostCenter, "BRL", false, null, null, Now)!.Field.ShouldBe("currency");
        requisition.UpdateHeader(CostCenter, "BRL", true, "Short", null, Now)!.Field.ShouldBe("purpose");
        var version = requisition.Version;
        requisition.UpdateHeader(CostCenter, "BRL", true, "Synthetic purpose text", null, Now).ShouldBeNull();
        requisition.UpdateHeader(CostCenter, "BRL", true, "Synthetic purpose text", null, Now).ShouldBeNull();
        requisition.Version.ShouldBe(version + 1);
        requisition.ClearDomainEvents();

        var context = new SubmissionContext(Requester, Now, Today, true, otherCostCenter, new Dictionary<Guid, bool>(), "t", RequisitionPolicy.V1);
        Should.Throw<ArgumentException>(() => requisition.CheckSubmittable(context));
        Should.Throw<ArgumentException>(() => requisition.Submit(context with { CostCenter = CostCenter }, Guid.Empty, Guid.NewGuid(), Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => PurchaseRequisition.CreateDraft(Guid.NewGuid(), "", Requester, CostCenter, "BRL", true, null, null, null, Now, "t"));
        Should.Throw<ArgumentNullException>(() => PurchaseRequisition.CopyAsNewDraft(null!, Guid.NewGuid(), "N", Requester, CostCenter, true, null, Now, "t"));
        requisition.Cancel(CancellationAuthority.Requester, Reason, null, Now, new string('c', 200)).Error.ShouldBeNull();
        requisition.StateChanges.Last().CorrelationId.Length.ShouldBe(RequisitionStateChange.CorrelationIdMaxLength);
        Should.Throw<ArgumentNullException>(() => requisition.Expire(Today, null!, Now, "t"));
    }

    [Fact]
    public void Requisition_AttachmentsAreRejectedOnceTerminal_AndFileNamesAreSanitized()
    {
        FilePolicy.SanitizeFileName("...").ShouldBeNull();
        FilePolicy.SanitizeFileName(new string('a', 300)).ShouldBeNull();
        FilePolicy.SanitizeFileName("ok.txt").ShouldBe("ok.txt");
        var requisition = Draft();
        requisition.Cancel(CancellationAuthority.CancelAny, Reason, null, Now, "t");
        requisition.RemoveAttachment(Guid.NewGuid(), Now)!.Kind.ShouldBe(DomainErrorKind.NotFound);
        requisition.UpdateLine(1, Line(), Now)!.Code.ShouldBe(RequisitionErrors.ImmutableCode);
        requisition.Withdraw(null, Now, "t").Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    private static PurchaseRequisition Draft() =>
        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000001", Requester, CostCenter, "BRL", true, null, null, Requester, Now, "t").Requisition!;

    private static RequisitionLineInput Line(
        RequisitionLineKind kind = RequisitionLineKind.Good, UnitSnapshot? unit = null, MasterDataReference? item = null, decimal quantity = 1m,
        decimal price = 10m, string? description = "Synthetic line") =>
        new(item, description, kind, Category, unit ?? Unit, quantity, new Money(price, "BRL"));
}
