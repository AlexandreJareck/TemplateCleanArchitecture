using Shouldly;
using Template.Domain.Common;
using Template.Domain.Receiving;

namespace Template.Unit.Tests.DomainTests.Purchasing;

/// <summary>Mock-free rules of the receipt aggregate (RCV-002/004/005/006/007/008). Synthetic data.</summary>
public sealed class ReceiptTests
{
    private static readonly DateTime Now = new(2027, 5, 10, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly IssueDate = new(2027, 5, 3);
    private static readonly DateOnly Today = new(2027, 5, 10);
    private static readonly Guid Receiver = Guid.NewGuid();
    private const string Reason = "Synthetic correction reason";

    private static ReceiptPosting Posting(
        IReadOnlyList<ReceiptLineInput> lines, DateOnly? receivedOn = null, Guid? receiver = null, IReadOnlyList<ReceiptAttachmentInput>? attachments = null,
        string? location = "Dock 2") => new(
        Guid.NewGuid(), "PO-2027-000001",
        [new ReceiptOrderLine(1, "Synthetic laptop", "UN", 0), new ReceiptOrderLine(2, "Synthetic consulting", "H", 2)],
        IssueDate, Today, receiver ?? Receiver, receivedOn ?? Today, location, "NF-1", "Synthetic notes", lines, attachments ?? []);

    private static (Receipt? Receipt, DomainError? Error) Post(ReceiptPosting posting) =>
        Receipt.Post(Guid.NewGuid(), "RCV-2027-000001", posting, ReceivingPolicy.V1, Now, "unit-receipt");

    [Fact]
    public void T_RCV_002_a_T_RCV_008_a_PostsGoodsAndServices_WithUnitDecimals()
    {
        var (receipt, error) = Post(Posting([new(1, 4m, 4m, 0m, null, null), new(2, 7.5m, 7.5m, 0m, null, null)],
            attachments: [new ReceiptAttachmentInput("delivery-note.pdf", "application/pdf", 2048)]));

        error.ShouldBeNull();
        receipt!.Status.ShouldBe(ReceiptStatus.Posted);
        receipt.PolicyReference.ShouldBe("ReceivingPolicy:1");
        receipt.TolerancePercent.ShouldBe(0m);
        receipt.HasDiscrepancy.ShouldBeFalse();
        receipt.Lines.Select(line => line.PoLineNumber).ShouldBe([1, 2]);
        receipt.Attachments.Single().AddedBy.ShouldBe(Receiver);
        receipt.DomainEvents.Single().ShouldBeOfType<ReceiptPosted>().LineCount.ShouldBe(2);
        Receipt.FormatNumber(2027, 12).ShouldBe("RCV-2027-000012");
    }

    [Theory]
    [InlineData(0, 0, 0, "lines[1].received")]
    [InlineData(2, 1, 0, "lines[1].accepted")]
    [InlineData(-1, -1, 0, "lines[1].received")]
    [InlineData(1.5, 1.5, 0, "lines[1].received")]
    public void T_RCV_002_b_InvalidQuantities_AreRejected(decimal received, decimal accepted, decimal rejected, string field) =>
        Post(Posting([new(1, received, accepted, rejected, null, null)])).Error!.Field.ShouldBe(field);

    [Fact]
    public void T_RCV_002_c_HeaderAndLineSetRules()
    {
        Post(Posting([])).Error!.Field.ShouldBe("lines");
        Post(Posting([new(1, 1m, 1m, 0m, null, null), new(1, 1m, 1m, 0m, null, null)])).Error!.Field.ShouldBe("lines");
        Post(Posting([new(9, 1m, 1m, 0m, null, null)])).Error!.Field.ShouldBe("lines[9].poLineNumber");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], location: new string('l', Receipt.LocationMaxLength + 1))).Error!.Field.ShouldBe("location");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], attachments: [new("a.exe", "application/x-msdownload", 10)])).Error!.Code.ShouldBe("file.type-not-allowed");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], attachments: [new("a.pdf", "application/pdf", ReceiptEvidencePolicy.MaxBytes + 1)])).Error!.Code.ShouldBe("file.too-large");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], attachments: [new("../a.pdf", "application/pdf", 10)])).Error!.Field.ShouldBe("attachments.fileName");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], attachments: Enumerable.Repeat(new ReceiptAttachmentInput("a.pdf", "application/pdf", 10), 11).ToArray()))
            .Error!.Field.ShouldBe("attachments");
    }

    [Fact]
    public void T_RCV_004_a_ARejectedQuantity_NeedsADiscrepancyTypeAndNotes_AndIsRecorded()
    {
        Post(Posting([new(1, 3m, 1m, 2m, null, "Crushed")])).Error!.Field.ShouldBe("lines[1].discrepancyType");
        Post(Posting([new(1, 3m, 1m, 2m, DiscrepancyType.Damaged, "  ")])).Error!.Field.ShouldBe("lines[1].discrepancyNotes");
        var (receipt, error) = Post(Posting([new(1, 3m, 1m, 2m, DiscrepancyType.Damaged, "  Two boxes crushed  ")]));

        error.ShouldBeNull();
        receipt!.HasDiscrepancy.ShouldBeTrue();
        receipt.Lines.Single().DiscrepancyType.ShouldBe(DiscrepancyType.Damaged);
        receipt.Lines.Single().DiscrepancyNotes.ShouldBe("Two boxes crushed");
        receipt.DomainEvents.OfType<ReceiptDiscrepancyRecorded>().Single().LineNumbers.ShouldBe([1]);
        Post(Posting([new(1, 3m, 3m, 0m, DiscrepancyType.Other, "ignored")])).Receipt!.Lines.Single().DiscrepancyType.ShouldBeNull();
    }

    [Fact]
    public void T_RCV_005_a_TheReceiverAndTheDateWindow_AreEnforced()
    {
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], receiver: Guid.Empty)).Error!.Field.ShouldBe("receiverId");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], receivedOn: IssueDate.AddDays(-1))).Error!.Field.ShouldBe("receivedOn");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], receivedOn: Today.AddDays(1))).Error!.Field.ShouldBe("receivedOn");
        Post(Posting([new(1, 1m, 1m, 0m, null, null)], receivedOn: IssueDate)).Error.ShouldBeNull();
    }

    [Fact]
    public void T_RCV_006_b_T_RCV_007_a_CorrectionsAreAppended_WithinZeroAndTheReceivedQuantity()
    {
        var receipt = Post(Posting([new(1, 5m, 3m, 2m, DiscrepancyType.QualityIssue, "Scratched lids")])).Receipt!;
        receipt.ClearDomainEvents();
        var version = receipt.Version;

        receipt.Correct(Guid.NewGuid(), 1, 1m, "short", Receiver, Now, "c").Error!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        receipt.Correct(Guid.NewGuid(), 1, 1m, Reason, Guid.Empty, Now, "c").Error!.Field.ShouldBe("actor");
        receipt.Correct(Guid.NewGuid(), 2, 1m, Reason, Receiver, Now, "c").Error!.Field.ShouldBe("poLineNumber");
        receipt.Correct(Guid.NewGuid(), 1, 0.5m, Reason, Receiver, Now, "c").Error!.Field.ShouldBe("acceptedDelta");
        receipt.Correct(Guid.NewGuid(), 1, 3m, Reason, Receiver, Now, "c").Error!.Code.ShouldBe(ReceivingErrors.CorrectionOutOfRangeCode);
        receipt.Correct(Guid.NewGuid(), 1, -4m, Reason, Receiver, Now, "c").Error!.Code.ShouldBe(ReceivingErrors.CorrectionOutOfRangeCode);
        receipt.Version.ShouldBe(version);

        // Accepting the two rejected units resolves the discrepancy (RCV-007 compensating record); posted quantities never change.
        var (correction, error) = receipt.Correct(Guid.NewGuid(), 1, 2m, Reason, Receiver, Now, "c");
        error.ShouldBeNull();
        correction!.Sequence.ShouldBe(1);
        receipt.CorrectedAccepted(receipt.Lines.Single()).ShouldBe(5m);
        receipt.Lines.Single().AcceptedQuantity.ShouldBe(3m);
        receipt.Version.ShouldBe(version + 1);
        receipt.DomainEvents.Single().ShouldBeOfType<ReceiptCorrected>().AcceptedDelta.ShouldBe(2m);
        receipt.Correct(Guid.NewGuid(), 1, -5m, Reason, Receiver, Now, "c").Correction!.Sequence.ShouldBe(2);
        receipt.CorrectedAccepted(receipt.Lines.Single()).ShouldBe(0m);
        ReceivingErrors.Immutable.Code.ShouldBe("receipt.immutable");
    }
}
