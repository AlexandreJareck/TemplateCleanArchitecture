using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Requisitions;
using Template.Domain.Catalog;
using Template.Domain.Organization;
using Template.Domain.Requisitions;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Requisitions module tables (REQ-*). Lines, attachments, state changes and submission snapshots are owned rows of the
/// aggregate (one version token, SP-1); only a never-submitted draft is ever deleted (REQ-010), which cascades to its
/// draft rows. References to master data are restricted (ORG-005).
/// </summary>
public sealed class PurchaseRequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> builder)
    {
        builder.ToTable("PurchaseRequisitions", table =>
        {
            table.HasCheckConstraint("CK_PurchaseRequisitions_EstimatedTotal", "\"EstimatedTotal\" >= 0");
            table.HasCheckConstraint("CK_PurchaseRequisitions_Revision", "\"Revision\" >= 1");
        });
        builder.HasKey(requisition => requisition.Id);
        builder.Property(requisition => requisition.Id).ValueGeneratedNever();
        builder.Property(requisition => requisition.Version).IsConcurrencyToken();
        builder.Property(requisition => requisition.Number).HasMaxLength(PurchaseRequisition.NumberMaxLength).IsRequired();
        builder.Property(requisition => requisition.CostCenterCode).HasMaxLength(20).IsRequired();
        builder.Property(requisition => requisition.CostCenterName).HasMaxLength(Template.Domain.Common.MasterDataRules.NameMaxLength).IsRequired();
        builder.Property(requisition => requisition.Purpose).HasMaxLength(PurchaseRequisition.PurposeMaxLength);
        builder.Property(requisition => requisition.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(requisition => requisition.EstimatedTotal).HasPrecision(18, 2);
        builder.Property(requisition => requisition.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(requisition => requisition.EstimatedTotalMoney);
        builder.Ignore(requisition => requisition.IsTerminal);
        builder.Ignore(requisition => requisition.WasEverSubmitted);
        builder.Ignore(requisition => requisition.DomainEvents);
        builder.HasIndex(requisition => requisition.Number).IsUnique().HasDatabaseName(IPurchaseRequisitionRepository.NumberUniqueKey);
        builder.HasIndex(requisition => new { requisition.RequesterId, requisition.CreatedAtUtc });
        builder.HasIndex(requisition => new { requisition.CostCenterId, requisition.Status });
        builder.HasIndex(requisition => new { requisition.DepartmentId, requisition.Status });
        builder.HasIndex(requisition => new { requisition.Status, requisition.NeededBy });
        builder.HasOne<CostCenter>().WithMany().HasForeignKey(requisition => requisition.CostCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PurchaseRequisition>().WithMany().HasForeignKey(requisition => requisition.SourceRequisitionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(requisition => requisition.Lines).WithOne().HasForeignKey(line => line.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(requisition => requisition.Attachments).WithOne().HasForeignKey(attachment => attachment.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(requisition => requisition.StateChanges).WithOne().HasForeignKey(change => change.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(requisition => requisition.Submissions).WithOne().HasForeignKey(submission => submission.RequisitionId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(requisition => requisition.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(requisition => requisition.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(requisition => requisition.StateChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(requisition => requisition.Submissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RequisitionLineConfiguration : IEntityTypeConfiguration<RequisitionLine>
{
    public void Configure(EntityTypeBuilder<RequisitionLine> builder)
    {
        builder.ToTable("PurchaseRequisitionLines", table =>
        {
            table.HasCheckConstraint("CK_PurchaseRequisitionLines_Quantity", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_PurchaseRequisitionLines_EstimatedUnitPrice", "\"EstimatedUnitPrice\" > 0");
        });
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Description).HasMaxLength(RequisitionLine.DescriptionMaxLength).IsRequired();
        builder.Property(line => line.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(line => line.CategoryCode).HasMaxLength(20).IsRequired();
        builder.Property(line => line.CategoryName).HasMaxLength(Template.Domain.Common.MasterDataRules.NameMaxLength).IsRequired();
        builder.Property(line => line.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(line => line.UnitName).HasMaxLength(Template.Domain.Common.MasterDataRules.NameMaxLength).IsRequired();
        builder.Property(line => line.Quantity).HasPrecision(18, 3);
        builder.Property(line => line.EstimatedUnitPrice).HasPrecision(18, 4);
        builder.Property(line => line.LineTotal).HasPrecision(18, 2);
        builder.HasIndex(line => new { line.RequisitionId, line.LineNumber }).IsUnique();
        builder.HasOne<CatalogItem>().WithMany().HasForeignKey(line => line.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(line => line.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(line => line.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RequisitionAttachmentConfiguration : IEntityTypeConfiguration<RequisitionAttachment>
{
    public void Configure(EntityTypeBuilder<RequisitionAttachment> builder)
    {
        builder.ToTable("PurchaseRequisitionAttachments", table =>
            table.HasCheckConstraint("CK_PurchaseRequisitionAttachments_Length", $"\"Length\" > 0 AND \"Length\" <= {FilePolicy.MaxBytes}"));
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Id).ValueGeneratedNever();
        builder.Property(attachment => attachment.FileName).HasMaxLength(FilePolicy.FileNameMaxLength).IsRequired();
        builder.Property(attachment => attachment.MediaType).HasMaxLength(FilePolicy.MediaTypeMaxLength).IsRequired();
        builder.Property(attachment => attachment.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(attachment => attachment.RequisitionId);
    }
}

public sealed class RequisitionStateChangeConfiguration : IEntityTypeConfiguration<RequisitionStateChange>
{
    public void Configure(EntityTypeBuilder<RequisitionStateChange> builder)
    {
        builder.ToTable("PurchaseRequisitionStateChanges");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.From).HasConversion<string>().HasMaxLength(20);
        builder.Property(change => change.To).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(change => change.Reason).HasMaxLength(Template.Domain.Common.MasterDataRules.ReasonMaxLength);
        builder.Property(change => change.ReasonCode).HasMaxLength(RequisitionStateChange.ReasonCodeMaxLength);
        builder.Property(change => change.CorrelationId).HasMaxLength(RequisitionStateChange.CorrelationIdMaxLength).IsRequired();
        builder.Property(change => change.EventType).HasMaxLength(RequisitionStateChange.EventTypeMaxLength);
        // A second writer appending the same sequence loses (guard of the root version token).
        builder.HasIndex(change => new { change.RequisitionId, change.Sequence }).IsUnique();
    }
}

public sealed class RequisitionSubmissionConfiguration : IEntityTypeConfiguration<RequisitionSubmission>
{
    public void Configure(EntityTypeBuilder<RequisitionSubmission> builder)
    {
        builder.ToTable("PurchaseRequisitionSubmissions");
        builder.HasKey(submission => submission.Id);
        builder.Property(submission => submission.Id).ValueGeneratedNever();
        builder.Property(submission => submission.Purpose).HasMaxLength(PurchaseRequisition.PurposeMaxLength).IsRequired();
        builder.Property(submission => submission.CostCenterCode).HasMaxLength(20).IsRequired();
        builder.Property(submission => submission.CostCenterName).HasMaxLength(Template.Domain.Common.MasterDataRules.NameMaxLength).IsRequired();
        builder.Property(submission => submission.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(submission => submission.EstimatedTotal).HasPrecision(18, 2);
        builder.HasIndex(submission => new { submission.RequisitionId, submission.Revision }).IsUnique();
        builder.HasMany(submission => submission.Lines).WithOne().HasForeignKey(line => line.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(submission => submission.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RequisitionSubmissionLineConfiguration : IEntityTypeConfiguration<RequisitionSubmissionLine>
{
    public void Configure(EntityTypeBuilder<RequisitionSubmissionLine> builder)
    {
        builder.ToTable("PurchaseRequisitionSubmissionLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Description).HasMaxLength(RequisitionLine.DescriptionMaxLength).IsRequired();
        builder.Property(line => line.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(line => line.CategoryCode).HasMaxLength(20).IsRequired();
        builder.Property(line => line.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(line => line.Quantity).HasPrecision(18, 3);
        builder.Property(line => line.EstimatedUnitPrice).HasPrecision(18, 4);
        builder.Property(line => line.LineTotal).HasPrecision(18, 2);
        builder.HasIndex(line => new { line.SubmissionId, line.LineNumber }).IsUnique();
    }
}

/// <summary>Per-year requisition number counter (persistence-only; gaps are allowed for requisitions).</summary>
public sealed class RequisitionNumberCounter
{
    public int Year { get; set; }
    public long LastValue { get; set; }
}

public sealed class RequisitionNumberCounterConfiguration : IEntityTypeConfiguration<RequisitionNumberCounter>
{
    public void Configure(EntityTypeBuilder<RequisitionNumberCounter> builder)
    {
        builder.ToTable("PurchaseRequisitionNumberCounters");
        builder.HasKey(counter => counter.Year);
        builder.Property(counter => counter.Year).ValueGeneratedNever();
    }
}
