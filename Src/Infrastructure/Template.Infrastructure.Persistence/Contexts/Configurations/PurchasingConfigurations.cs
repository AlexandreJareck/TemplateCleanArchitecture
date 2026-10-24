using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Purchasing;
using Template.Domain.Common;
using Template.Domain.Purchasing;
using Template.Domain.Receiving;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Purchasing tables (PUR-*). One order per award (PUR-001, unique index), unique numbers once issued (PUR-005), the
/// commercial snapshot stored by value (PUR-002) and append-only history. Rows are never deleted by the application
/// (NFR retention); references to other modules are restricted.
/// </summary>
public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchasingOrders", table =>
        {
            table.HasCheckConstraint("CK_PurchasingOrders_Total", "\"Total\" >= 0 AND \"Subtotal\" >= 0 AND \"Freight\" >= 0 AND \"Discount\" >= 0");
            table.HasCheckConstraint("CK_PurchasingOrders_IssuedNumber", "\"Status\" = 'Draft' OR \"Status\" = 'Cancelled' OR \"Number\" IS NOT NULL");
            table.HasCheckConstraint("CK_PurchasingOrders_Attempts", "\"DocumentAttempts\" >= 0 AND \"NotificationAttempts\" >= 0");
        });
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.Version).IsConcurrencyToken();
        builder.Property(order => order.Number).HasMaxLength(PurchaseOrder.NumberMaxLength);
        builder.Property(order => order.RfqNumber).HasMaxLength(RequestForQuotation.NumberMaxLength).IsRequired();
        builder.Property(order => order.RequisitionNumber).HasMaxLength(PurchaseRequisition.NumberMaxLength).IsRequired();
        builder.Property(order => order.SupplierLegalName).HasMaxLength(Supplier.LegalNameMaxLength).IsRequired();
        builder.Property(order => order.SupplierTradeName).HasMaxLength(Supplier.LegalNameMaxLength);
        builder.Property(order => order.SupplierCountry).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(order => order.SupplierTaxId).HasMaxLength(TaxIdentifier.MaxLength).IsRequired();
        builder.Property(order => order.SupplierAddress).HasMaxLength(PurchaseOrder.SnapshotTextMaxLength);
        builder.Property(order => order.SupplierContactName).HasMaxLength(PurchaseOrder.SnapshotTextMaxLength);
        builder.Property(order => order.SupplierContactEmail).HasMaxLength(PurchaseOrder.SnapshotTextMaxLength);
        builder.Property(order => order.SupplierContactPhone).HasMaxLength(PurchaseOrder.SnapshotTextMaxLength);
        builder.Property(order => order.BuyerName).HasMaxLength(PurchaseOrder.SnapshotTextMaxLength);
        builder.Property(order => order.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(order => order.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(order => order.RequisitionCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(order => order.Subtotal).HasPrecision(18, 2);
        builder.Property(order => order.Freight).HasPrecision(18, 2);
        builder.Property(order => order.Discount).HasPrecision(18, 2);
        builder.Property(order => order.TaxesIncluded).HasPrecision(18, 2);
        builder.Property(order => order.Total).HasPrecision(18, 2);
        builder.Property(order => order.QuoteToBaseRate).HasPrecision(18, 6);
        builder.Property(order => order.RequisitionToBaseRate).HasPrecision(18, 6);
        builder.Property(order => order.RequisitionCurrencyTotal).HasPrecision(18, 2);
        builder.Property(order => order.ShortCloseReleasedAmount).HasPrecision(18, 2);
        builder.Property(order => order.PaymentTerms).HasMaxLength(PurchaseOrder.PaymentTermsMaxLength).IsRequired();
        builder.Property(order => order.DeliveryAddress).HasMaxLength(PurchaseOrder.DeliveryAddressMaxLength);
        builder.Property(order => order.Notes).HasMaxLength(PurchaseOrder.NotesMaxLength);
        builder.Property(order => order.CancellationReason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Property(order => order.ShortCloseReason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(order => order.DocumentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(order => order.NotificationStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(order => order.DocumentTemplateVersion).HasMaxLength(PurchaseOrder.TemplateVersionMaxLength);
        builder.Property(order => order.DocumentFailureCode).HasMaxLength(PurchaseOrder.FailureCodeMaxLength);
        builder.Property(order => order.NotificationFailureCode).HasMaxLength(PurchaseOrder.FailureCodeMaxLength);
        builder.Ignore(order => order.TotalMoney);
        builder.Ignore(order => order.HasAcceptedQuantity);
        builder.Ignore(order => order.IsReceivable);
        builder.Ignore(order => order.DomainEvents);
        builder.HasIndex(order => order.AwardId).IsUnique().HasDatabaseName(IPurchaseOrderRepository.AwardUniqueKey);
        builder.HasIndex(order => order.Number).IsUnique().HasDatabaseName(IPurchaseOrderRepository.NumberUniqueKey).HasFilter("\"Number\" IS NOT NULL");
        builder.HasIndex(order => order.DocumentJobId).HasFilter("\"DocumentJobId\" IS NOT NULL");
        builder.HasIndex(order => new { order.Status, order.UpdatedAtUtc });
        builder.HasIndex(order => new { order.SupplierId, order.Status });
        builder.HasIndex(order => order.RequisitionId);
        builder.HasIndex(order => order.RequesterId);
        builder.HasOne<PurchaseRequisition>().WithMany().HasForeignKey(order => order.RequisitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RequestForQuotation>().WithMany().HasForeignKey(order => order.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RfqAward>().WithMany().HasForeignKey(order => order.AwardId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(order => order.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(order => order.Lines).WithOne().HasForeignKey(line => line.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.History).WithOne().HasForeignKey(entry => entry.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(order => order.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(order => order.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchasingOrderLines", table =>
        {
            table.HasCheckConstraint("CK_PurchasingOrderLines_Quantity", "\"Quantity\" > 0 AND \"UnitPrice\" > 0");
            // RCV-002/003/007: received and rejected never negative; accepted between zero and received plus corrections.
            table.HasCheckConstraint("CK_PurchasingOrderLines_Received", "\"ReceivedQuantity\" >= 0 AND \"RejectedQuantity\" >= 0 AND \"AcceptedQuantity\" >= 0");
            table.HasCheckConstraint("CK_PurchasingOrderLines_UnitDecimals", "\"UnitAllowedDecimals\" BETWEEN 0 AND 3");
        });
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Description).HasMaxLength(PurchaseOrderLine.DescriptionMaxLength).IsRequired();
        builder.Property(line => line.Kind).HasMaxLength(20).IsRequired();
        builder.Property(line => line.UnitCode).HasMaxLength(PurchaseOrderLine.UnitCodeMaxLength).IsRequired();
        builder.Property(line => line.Quantity).HasPrecision(18, 3);
        builder.Property(line => line.UnitPrice).HasPrecision(18, 4);
        builder.Property(line => line.LineTotal).HasPrecision(18, 2);
        builder.Property(line => line.ReceivedQuantity).HasPrecision(18, 3);
        builder.Property(line => line.AcceptedQuantity).HasPrecision(18, 3);
        builder.Property(line => line.RejectedQuantity).HasPrecision(18, 3);
        builder.Ignore(line => line.RemainingQuantity);
        builder.Ignore(line => line.IsComplete);
        builder.HasIndex(line => new { line.PurchaseOrderId, line.LineNumber }).IsUnique();
    }
}

public sealed class PurchaseOrderHistoryEntryConfiguration : IEntityTypeConfiguration<PurchaseOrderHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderHistoryEntry> builder)
    {
        builder.ToTable("PurchasingOrderHistory");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Action).HasMaxLength(PurchaseOrderHistoryEntry.ActionMaxLength).IsRequired();
        builder.Property(entry => entry.From).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.To).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Property(entry => entry.ReasonCode).HasMaxLength(PurchaseOrderHistoryEntry.ReasonCodeMaxLength);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(PurchaseOrderHistoryEntry.CorrelationIdMaxLength).IsRequired();
        builder.Property(entry => entry.EventType).HasMaxLength(PurchaseOrderHistoryEntry.EventTypeMaxLength);
        // A second writer appending the same sequence loses (guard of the root version token).
        builder.HasIndex(entry => new { entry.PurchaseOrderId, entry.Sequence }).IsUnique();
    }
}

/// <summary>Per-year gapless purchase-order counter (SP-2): incremented inside the issue transaction.</summary>
public sealed class PurchaseOrderNumberCounter
{
    public int Year { get; set; }
    public long LastValue { get; set; }
}

public sealed class PurchaseOrderNumberCounterConfiguration : IEntityTypeConfiguration<PurchaseOrderNumberCounter>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderNumberCounter> builder)
    {
        builder.ToTable("PurchasingOrderNumberCounters", table => table.HasCheckConstraint("CK_PurchasingOrderNumberCounters_LastValue", "\"LastValue\" >= 0"));
        builder.HasKey(counter => counter.Year);
        builder.Property(counter => counter.Year).ValueGeneratedNever();
    }
}

/// <summary>
/// Receiving tables (RCV-*). Receipts, their lines, corrections and evidence metadata are append-only: the application
/// has no update or delete path for posted quantities (RCV-006); corrections are new rows (RCV-007).
/// </summary>
public sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("ReceivingReceipts");
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).ValueGeneratedNever();
        builder.Property(receipt => receipt.Version).IsConcurrencyToken();
        builder.Property(receipt => receipt.Number).HasMaxLength(Receipt.NumberMaxLength).IsRequired();
        builder.Property(receipt => receipt.PurchaseOrderNumber).HasMaxLength(PurchaseOrder.NumberMaxLength).IsRequired();
        builder.Property(receipt => receipt.Location).HasMaxLength(Receipt.LocationMaxLength);
        builder.Property(receipt => receipt.DeliveryReference).HasMaxLength(Receipt.DeliveryReferenceMaxLength);
        builder.Property(receipt => receipt.Notes).HasMaxLength(Receipt.NotesMaxLength);
        builder.Property(receipt => receipt.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(receipt => receipt.PolicyReference).HasMaxLength(40).IsRequired();
        builder.Property(receipt => receipt.TolerancePercent).HasPrecision(9, 4);
        builder.Ignore(receipt => receipt.DomainEvents);
        builder.HasIndex(receipt => receipt.Number).IsUnique();
        builder.HasIndex(receipt => new { receipt.PurchaseOrderId, receipt.PostedAtUtc });
        builder.HasIndex(receipt => new { receipt.HasDiscrepancy, receipt.PostedAtUtc });
        builder.HasOne<PurchaseOrder>().WithMany().HasForeignKey(receipt => receipt.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(receipt => receipt.Lines).WithOne().HasForeignKey(line => line.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(receipt => receipt.Corrections).WithOne().HasForeignKey(correction => correction.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(receipt => receipt.Attachments).WithOne().HasForeignKey(attachment => attachment.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(receipt => receipt.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(receipt => receipt.Corrections).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(receipt => receipt.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> builder)
    {
        builder.ToTable("ReceivingReceiptLines", table =>
        {
            // RCV-002: received > 0, accepted and rejected not negative, accepted + rejected = received.
            table.HasCheckConstraint("CK_ReceivingReceiptLines_Quantities",
                "\"ReceivedQuantity\" > 0 AND \"AcceptedQuantity\" >= 0 AND \"RejectedQuantity\" >= 0 AND \"AcceptedQuantity\" + \"RejectedQuantity\" = \"ReceivedQuantity\"");
            // RCV-004: a rejected quantity carries its discrepancy type.
            table.HasCheckConstraint("CK_ReceivingReceiptLines_Discrepancy", "\"RejectedQuantity\" = 0 OR \"DiscrepancyType\" IS NOT NULL");
        });
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Description).HasMaxLength(PurchaseOrderLine.DescriptionMaxLength).IsRequired();
        builder.Property(line => line.UnitCode).HasMaxLength(PurchaseOrderLine.UnitCodeMaxLength).IsRequired();
        builder.Property(line => line.ReceivedQuantity).HasPrecision(18, 3);
        builder.Property(line => line.AcceptedQuantity).HasPrecision(18, 3);
        builder.Property(line => line.RejectedQuantity).HasPrecision(18, 3);
        builder.Property(line => line.DiscrepancyType).HasConversion<string>().HasMaxLength(20);
        builder.Property(line => line.DiscrepancyNotes).HasMaxLength(Receipt.DiscrepancyNotesMaxLength);
        builder.HasIndex(line => new { line.ReceiptId, line.PoLineNumber }).IsUnique();
    }
}

public sealed class ReceiptCorrectionConfiguration : IEntityTypeConfiguration<ReceiptCorrection>
{
    public void Configure(EntityTypeBuilder<ReceiptCorrection> builder)
    {
        builder.ToTable("ReceivingReceiptCorrections", table => table.HasCheckConstraint("CK_ReceivingReceiptCorrections_Delta", "\"AcceptedDelta\" <> 0"));
        builder.HasKey(correction => correction.Id);
        builder.Property(correction => correction.Id).ValueGeneratedNever();
        builder.Property(correction => correction.AcceptedDelta).HasPrecision(18, 3);
        builder.Property(correction => correction.Reason).HasMaxLength(MasterDataRules.ReasonMaxLength).IsRequired();
        builder.Property(correction => correction.CorrelationId).HasMaxLength(ReceiptCorrection.CorrelationIdMaxLength).IsRequired();
        builder.HasIndex(correction => new { correction.ReceiptId, correction.Sequence }).IsUnique();
    }
}

public sealed class ReceiptAttachmentConfiguration : IEntityTypeConfiguration<ReceiptAttachment>
{
    public void Configure(EntityTypeBuilder<ReceiptAttachment> builder)
    {
        builder.ToTable("ReceivingReceiptAttachments", table =>
            table.HasCheckConstraint("CK_ReceivingReceiptAttachments_Length", $"\"Length\" > 0 AND \"Length\" <= {ReceiptEvidencePolicy.MaxBytes}"));
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Id).ValueGeneratedNever();
        builder.Property(attachment => attachment.FileName).HasMaxLength(ReceiptAttachment.FileNameMaxLength).IsRequired();
        builder.Property(attachment => attachment.MediaType).HasMaxLength(100).IsRequired();
        builder.HasIndex(attachment => attachment.ReceiptId);
    }
}

/// <summary>Per-year receipt counter (gaps allowed, like requisition numbers).</summary>
public sealed class ReceiptNumberCounter
{
    public int Year { get; set; }
    public long LastValue { get; set; }
}

public sealed class ReceiptNumberCounterConfiguration : IEntityTypeConfiguration<ReceiptNumberCounter>
{
    public void Configure(EntityTypeBuilder<ReceiptNumberCounter> builder)
    {
        builder.ToTable("ReceivingReceiptNumberCounters");
        builder.HasKey(counter => counter.Year);
        builder.Property(counter => counter.Year).ValueGeneratedNever();
    }
}
