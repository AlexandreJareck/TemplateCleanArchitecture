using Shouldly;
using Template.Domain.Common;
using Template.Domain.Requisitions;

namespace Template.Unit.Tests.DomainTests.Requisitions;

/// <summary>REQ-002..012, CALC-002 and FIL-002/004 rules of the purchase requisition aggregate, without mocks.</summary>
public sealed class PurchaseRequisitionTests
{
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2027, 3, 1);
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly CostCenterSnapshot CostCenter = new(Guid.NewGuid(), "CC-OPS", "Operations", Guid.NewGuid(), true);
    private static readonly CategorySnapshot Category = new(Guid.NewGuid(), "IT", "IT supplies", true);
    private static readonly UnitSnapshot Unit = new(Guid.NewGuid(), "UN", "Unit", 0, true);
    private static readonly UnitSnapshot Kilogram = new(Guid.NewGuid(), "KG", "Kilogram", 3, true);
    private const string Purpose = "Replacement laptops for the support team";
    private const string Reason = "No longer needed after reorganization";
    private const string Correlation = "trace-requisition-tests";

    [Fact]
    public void CreateDraft_StartsAtRevisionOne_WithCreationHistory_AndRejectsInactiveCostCenter()
    {
        var requisition = NewDraft();

        requisition.Status.ShouldBe(RequisitionStatus.Draft);
        requisition.Revision.ShouldBe(1);
        requisition.Number.ShouldBe("REQ-2027-000001");
        requisition.StateChanges.Single().From.ShouldBeNull();
        requisition.StateChanges.Single().CorrelationId.ShouldBe(Correlation);
        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000002", Requester, CostCenter with { IsActive = false }, "BRL", true,
            null, null, Requester, Now, Correlation).Error!.RuleId.ShouldBe("ORG-003");
        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000003", Requester, CostCenter, "XYZ", false,
            null, null, Requester, Now, Correlation).Error!.Field.ShouldBe("currency");
        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000004", Requester, CostCenter, "BRL", true,
            "too short", null, Requester, Now, Correlation).Error!.Field.ShouldBe("purpose");
    }

    [Fact]
    public void FormatNumber_UsesYearAndSixDigitSequence() =>
        PurchaseRequisition.FormatNumber(2027, 42).ShouldBe("REQ-2027-000042");

    [Fact]
    public void T_REQ_002_a_And_T_REQ_003_LinesAreValidated_AndTotalsAreServerCalculated()
    {
        var requisition = NewDraft();

        requisition.AddLine(Line(quantity: 0m), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("quantity");
        requisition.AddLine(Line(quantity: 1.5m), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("quantity"); // UN allows 0 decimals
        requisition.AddLine(Line(unit: Kilogram, quantity: 1.2345m), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("quantity");
        requisition.AddLine(Line(price: 0m), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("estimatedUnitPrice.amount");
        requisition.AddLine(Line(price: 1.23456m), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("estimatedUnitPrice.amount");
        requisition.AddLine(Line(currency: "USD"), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("estimatedUnitPrice.currency");
        requisition.AddLine(Line(description: "ab"), RequisitionPolicy.V1, Now).Error!.Field.ShouldBe("description");
        requisition.AddLine(Line(category: Category with { IsActive = false }), RequisitionPolicy.V1, Now).Error!.RuleId.ShouldBe("CAT-002");
        requisition.Lines.ShouldBeEmpty();

        requisition.AddLine(Line(quantity: 3m, price: 33.3333m), RequisitionPolicy.V1, Now).Error.ShouldBeNull();
        requisition.AddLine(Line(unit: Kilogram, quantity: 1.125m, price: 10m), RequisitionPolicy.V1, Now).Error.ShouldBeNull();

        requisition.Lines.Select(line => line.LineTotal).ShouldBe([100.00m, 11.25m]);
        requisition.EstimatedTotal.ShouldBe(111.25m);
    }

    [Fact]
    public void LineNumbers_AreStable_AfterRemoval_AndUpdateRecalculates()
    {
        var requisition = NewDraft();
        requisition.AddLine(Line(), RequisitionPolicy.V1, Now);
        requisition.AddLine(Line(), RequisitionPolicy.V1, Now);
        requisition.AddLine(Line(), RequisitionPolicy.V1, Now);

        requisition.RemoveLine(2, Now).ShouldBeNull();
        requisition.AddLine(Line(), RequisitionPolicy.V1, Now).Line!.LineNumber.ShouldBe(4);
        requisition.UpdateLine(1, Line(quantity: 10m, price: 2m), Now).ShouldBeNull();

        requisition.Lines.Select(line => line.LineNumber).Order().ShouldBe([1, 3, 4]);
        requisition.EstimatedTotal.ShouldBe(20m + 2 * 50m);
        requisition.RemoveLine(2, Now)!.Kind.ShouldBe(DomainErrorKind.NotFound);
        requisition.UpdateLine(9, Line(), Now)!.Kind.ShouldBe(DomainErrorKind.NotFound);
    }

    [Fact]
    public void T_REQ_002_a_PolicyLimitsTheNumberOfLines()
    {
        var requisition = NewDraft();
        var policy = RequisitionPolicy.V1 with { MaxLines = 2 };
        requisition.AddLine(Line(), policy, Now);
        requisition.AddLine(Line(), policy, Now);

        requisition.AddLine(Line(), policy, Now).Error!.Field.ShouldBe("lines");
        RequisitionPolicy.V1.MaxLines.ShouldBe(100);
    }

    [Fact]
    public void CurrencyIsLockedWhileLinesExist()
    {
        var requisition = NewDraft();
        requisition.UpdateHeader(CostCenter, "USD", true, Purpose, Today, Now).ShouldBeNull();
        requisition.AddLine(Line(currency: "USD"), RequisitionPolicy.V1, Now);

        requisition.UpdateHeader(CostCenter, "BRL", true, Purpose, Today, Now)!.Field.ShouldBe("currency");
    }

    [Fact]
    public void T_REQ_004_a_SubmissionGuards_AreReportedTogether()
    {
        var requisition = NewDraft(purpose: null);
        var context = Context(requisition, requesterActive: false, costCenterActive: false, localDate: Today);

        var error = requisition.CheckSubmittable(context);

        error!.Code.ShouldBe(RequisitionErrors.NotSubmittableCode);
        error.Details!["fields"].Split(',').ShouldBe(["lines", "purpose", "neededBy", "costCenterId", "requesterId"], ignoreOrder: true);
    }

    [Fact]
    public void T_REQ_004_a_NeededByMustBeOnOrAfterTheSubmissionDate_AndReferencesActive()
    {
        var requisition = SubmittableDraft();
        var line = requisition.Lines.Single();

        requisition.CheckSubmittable(Context(requisition, localDate: Today.AddDays(1), neededBy: Today))!.Details!["fields"].ShouldBe("neededBy");
        requisition.CheckSubmittable(Context(requisition, localDate: Today)).ShouldBeNull();
        var inactive = Context(requisition, localDate: Today, inactive: [line.CategoryId]);
        requisition.CheckSubmittable(inactive)!.Details!["fields"].ShouldBe($"lines[{line.LineNumber}].categoryId");
    }

    [Fact]
    public void T_REQ_006_a_SubmittedContentIsImmutable_AndTheSnapshotIsStored()
    {
        var requisition = SubmittableDraft();
        requisition.AddAttachment(Guid.NewGuid(), "quote.pdf", "application/pdf", 1000, Requester, Now);
        var reservation = Guid.NewGuid();

        requisition.Submit(Context(requisition), Guid.NewGuid(), reservation, Guid.NewGuid()).ShouldBeNull();

        requisition.Status.ShouldBe(RequisitionStatus.Submitted);
        requisition.ActiveReservationId.ShouldBe(reservation);
        var snapshot = requisition.Submissions.Single();
        snapshot.Revision.ShouldBe(1);
        snapshot.EstimatedTotal.ShouldBe(requisition.EstimatedTotal);
        snapshot.Lines.Single().Description.ShouldBe(requisition.Lines.Single().Description);
        snapshot.PolicyVersion.ShouldBe(1);
        requisition.Attachments.Single().LockedAtRevision.ShouldBe(1);
        requisition.DomainEvents.Single().EventType.ShouldBe("procurement.requisition.submitted");
        requisition.StateChanges.Last().EventType.ShouldBe("procurement.requisition.submitted");

        requisition.AddLine(Line(), RequisitionPolicy.V1, Now).Error!.Code.ShouldBe(RequisitionErrors.ImmutableCode);
        requisition.UpdateHeader(CostCenter, "BRL", true, Purpose, Today, Now)!.Code.ShouldBe(RequisitionErrors.ImmutableCode);
        requisition.RemoveLine(1, Now)!.Code.ShouldBe(RequisitionErrors.ImmutableCode);
        requisition.Submit(Context(requisition), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        requisition.EnsureDiscardable()!.Code.ShouldBe(RequisitionErrors.ImmutableCode);
    }

    [Fact]
    public void T_REQ_007_a_Withdraw_ReturnsToDraftWithANewRevision_AndDetachesTheReservation()
    {
        var requisition = SubmittedRequisition(out var allocation);

        var (allocationId, error) = requisition.Withdraw(Requester, Now, Correlation);

        error.ShouldBeNull();
        allocationId.ShouldBe(allocation);
        requisition.Status.ShouldBe(RequisitionStatus.Draft);
        requisition.Revision.ShouldBe(2);
        requisition.ActiveReservationId.ShouldBeNull();
        requisition.WasEverSubmitted.ShouldBeTrue();
        requisition.EnsureDiscardable()!.RuleId.ShouldBe("REQ-010");
        requisition.Withdraw(Requester, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);

        requisition.Submit(Context(requisition), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).ShouldBeNull();
        requisition.Submissions.Select(submission => submission.Revision).ShouldBe([1, 2]);
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft, CancellationAuthority.Requester)]
    [InlineData(RequisitionStatus.Submitted, CancellationAuthority.Requester)]
    [InlineData(RequisitionStatus.Draft, CancellationAuthority.CancelAny)]
    [InlineData(RequisitionStatus.Submitted, CancellationAuthority.CancelAny)]
    public void T_REQ_008_a_CancelWithReason_FromAllowedStates_ReleasesOnlyWhenReserved(RequisitionStatus state, CancellationAuthority authority)
    {
        Guid? expectedAllocation = null;
        var requisition = state == RequisitionStatus.Draft ? SubmittableDraft() : SubmittedRequisition(out var allocation);
        if (state == RequisitionStatus.Submitted)
            expectedAllocation = requisition.ActiveAllocationId;

        requisition.Cancel(authority, "short", Requester, Now, Correlation).Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        var (allocationId, error) = requisition.Cancel(authority, Reason, Requester, Now, Correlation);

        error.ShouldBeNull();
        allocationId.ShouldBe(expectedAllocation);
        requisition.Status.ShouldBe(RequisitionStatus.Cancelled);
        requisition.IsTerminal.ShouldBeTrue();
        requisition.StateChanges.Last().Reason.ShouldBe(Reason);
        requisition.StateChanges.Last().ReasonCode.ShouldBe("Cancelled");
        requisition.Cancel(CancellationAuthority.CancelAny, Reason, Requester, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        requisition.AddAttachment(Guid.NewGuid(), "late.pdf", "application/pdf", 10, Requester, Now).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void T_REQ_009_a_ExpiresOnlyAfterNeededByPlusGrace_Once()
    {
        var requisition = SubmittedRequisition(out _);
        var dueDay = requisition.NeededBy!.Value.AddDays(RequisitionPolicy.V1.ExpiryGraceDays);

        requisition.Expire(dueDay, RequisitionPolicy.V1, Now, Correlation).Changed.ShouldBeFalse();
        var expired = requisition.Expire(dueDay.AddDays(1), RequisitionPolicy.V1, Now, Correlation);
        expired.Changed.ShouldBeTrue();
        expired.AllocationId.ShouldNotBeNull();
        requisition.Status.ShouldBe(RequisitionStatus.Expired);
        requisition.StateChanges.Last().ActorId.ShouldBeNull();

        var again = requisition.Expire(dueDay.AddDays(2), RequisitionPolicy.V1, Now, Correlation);
        again.Changed.ShouldBeFalse();
        again.Error.ShouldBeNull();
        NewDraft().Expire(dueDay.AddDays(2), RequisitionPolicy.V1, Now, Correlation).Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
    }

    [Fact]
    public void T_REQ_010_a_NeverSubmittedDraft_IsDiscardable() => NewDraft().EnsureDiscardable().ShouldBeNull();

    [Fact]
    public void T_REQ_012_a_OnlyTerminalRequisitionsCanBeCopied_AndTheCopyReferencesTheSource()
    {
        var requisition = SubmittableDraft();
        PurchaseRequisition.CopyAsNewDraft(requisition, Guid.NewGuid(), "REQ-2027-000009", Requester, CostCenter, true, Requester, Now, Correlation)
            .Error!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        requisition.Cancel(CancellationAuthority.Requester, Reason, Requester, Now, Correlation);

        var (copy, error) = PurchaseRequisition.CopyAsNewDraft(requisition, Guid.NewGuid(), "REQ-2027-000009", Requester, CostCenter, true, Requester, Now, Correlation);

        error.ShouldBeNull();
        copy!.SourceRequisitionId.ShouldBe(requisition.Id);
        copy.Status.ShouldBe(RequisitionStatus.Draft);
        copy.Lines.Count.ShouldBe(1);
        copy.EstimatedTotal.ShouldBe(requisition.EstimatedTotal);
        copy.NeededBy.ShouldBeNull();
    }

    [Fact]
    public void T_FIL_002_a_AttachmentMetadata_FollowsTheFilePolicy()
    {
        var requisition = NewDraft();

        requisition.AddAttachment(Guid.NewGuid(), "script.exe", "application/x-msdownload", 10, null, Now).Error!.Code.ShouldBe(RequisitionErrors.FileTypeNotAllowedCode);
        requisition.AddAttachment(Guid.NewGuid(), "big.pdf", "application/pdf", FilePolicy.MaxBytes + 1, null, Now).Error!.Kind.ShouldBe(DomainErrorKind.TooLarge);
        requisition.AddAttachment(Guid.NewGuid(), "   ", "application/pdf", 10, null, Now).Error!.Field.ShouldBe("fileName");
        var (attachment, _) = requisition.AddAttachment(Guid.NewGuid(), "..\\folder/se<cret>.PDF", "Application/PDF", 10, null, Now);
        attachment!.FileName.ShouldBe("se_cret_.PDF");
        attachment.MediaType.ShouldBe("application/pdf");
        attachment.Status.ShouldBe(AttachmentStatus.Pending);

        for (var index = 1; index < FilePolicy.MaxAttachmentsPerOwner; index++)
            requisition.AddAttachment(Guid.NewGuid(), $"file{index}.txt", "text/plain", 10, null, Now).Error.ShouldBeNull();
        requisition.AddAttachment(Guid.NewGuid(), "eleven.txt", "text/plain", 10, null, Now).Error!.Code.ShouldBe(RequisitionErrors.FileLimitReachedCode);
    }

    [Fact]
    public void T_FIL_004_a_LockedAttachments_AreAppendOnly()
    {
        var requisition = SubmittableDraft();
        var draftAttachment = requisition.AddAttachment(Guid.NewGuid(), "draft.pdf", "application/pdf", 10, null, Now).Attachment!;
        requisition.RemoveAttachment(draftAttachment.Id, Now).ShouldBeNull();
        var locked = requisition.AddAttachment(Guid.NewGuid(), "quote.pdf", "application/pdf", 10, null, Now).Attachment!;
        requisition.Submit(Context(requisition), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var added = requisition.AddAttachment(Guid.NewGuid(), "extra.pdf", "application/pdf", 10, null, Now).Attachment!;
        added.LockedAtRevision.ShouldBe(1);
        requisition.RemoveAttachment(locked.Id, Now)!.Code.ShouldBe(RequisitionErrors.FileImmutableCode);
        requisition.Withdraw(Requester, Now, Correlation);
        requisition.RemoveAttachment(locked.Id, Now)!.Code.ShouldBe(RequisitionErrors.FileImmutableCode);
        requisition.RemoveAttachment(Guid.NewGuid(), Now)!.Kind.ShouldBe(DomainErrorKind.NotFound);
    }

    [Fact]
    public void EveryChange_IncrementsTheVersion()
    {
        var requisition = NewDraft();
        var versions = new List<long> { requisition.Version };
        requisition.AddLine(Line(), RequisitionPolicy.V1, Now);
        versions.Add(requisition.Version);
        requisition.UpdateHeader(CostCenter, "BRL", true, Purpose, Today.AddDays(5), Now);
        versions.Add(requisition.Version);
        requisition.Submit(Context(requisition), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        versions.Add(requisition.Version);

        versions.ShouldBe([1, 2, 3, 4]);
    }

    internal static PurchaseRequisition NewDraft(string? purpose = Purpose) =>
        PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000001", Requester, CostCenter, "BRL", true, purpose, null, Requester, Now, Correlation)
            .Requisition!;

    private static PurchaseRequisition SubmittableDraft()
    {
        var requisition = NewDraft();
        requisition.UpdateHeader(CostCenter, "BRL", true, Purpose, Today.AddDays(10), Now).ShouldBeNull();
        requisition.AddLine(Line(quantity: 2m, price: 150m), RequisitionPolicy.V1, Now).Error.ShouldBeNull();
        return requisition;
    }

    private static PurchaseRequisition SubmittedRequisition(out Guid allocation)
    {
        var requisition = SubmittableDraft();
        allocation = Guid.NewGuid();
        requisition.Submit(Context(requisition), Guid.NewGuid(), Guid.NewGuid(), allocation).ShouldBeNull();
        return requisition;
    }

    private static SubmissionContext Context(
        PurchaseRequisition requisition, bool requesterActive = true, bool costCenterActive = true, DateOnly? localDate = null,
        DateOnly? neededBy = null, Guid[]? inactive = null)
    {
        if (neededBy is { } date)
            requisition.UpdateHeader(CostCenter, requisition.Currency, true, requisition.Purpose, date, Now);
        var states = requisition.Lines.SelectMany(line => new[] { line.CatalogItemId, line.CategoryId, line.UnitOfMeasureId })
            .OfType<Guid>().Distinct().ToDictionary(id => id, id => !(inactive ?? []).Contains(id));
        return new SubmissionContext(
            Requester, Now, localDate ?? Today, requesterActive, CostCenter with { IsActive = costCenterActive }, states, Correlation, RequisitionPolicy.V1);
    }

    private static RequisitionLineInput Line(
        decimal quantity = 1m, decimal price = 50m, string currency = "BRL", string description = "Laptop 14 inch",
        CategorySnapshot? category = null, UnitSnapshot? unit = null) =>
        new(null, description, RequisitionLineKind.Good, category ?? Category, unit ?? Unit, quantity, new Money(price, currency));
}
