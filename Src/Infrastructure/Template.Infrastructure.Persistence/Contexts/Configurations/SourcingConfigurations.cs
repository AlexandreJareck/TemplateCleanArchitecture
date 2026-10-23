using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Sourcing;
using Template.Domain.Common;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Sourcing module tables (SRC-*, Phase 29). Every child row belongs to the RFQ aggregate (one version token, SP-1);
/// history, revisions, comparisons, waivers and rate snapshots are append-only and never deleted. Sequence guards turn
/// a concurrent second writer into a concurrency conflict; references to requisitions and suppliers are restricted.
/// </summary>
public sealed class RequestForQuotationConfiguration : IEntityTypeConfiguration<RequestForQuotation>
{
    public void Configure(EntityTypeBuilder<RequestForQuotation> builder)
    {
        builder.ToTable("SourcingRfqs", table =>
        {
            table.HasCheckConstraint("CK_SourcingRfqs_RoundNumber", "\"RoundNumber\" >= 1");
            table.HasCheckConstraint("CK_SourcingRfqs_RequisitionEstimatedTotal", "\"RequisitionEstimatedTotal\" >= 0");
        });
        builder.HasKey(rfq => rfq.Id);
        builder.Property(rfq => rfq.Id).ValueGeneratedNever();
        builder.Property(rfq => rfq.Version).IsConcurrencyToken();
        builder.Property(rfq => rfq.Number).HasMaxLength(RequestForQuotation.NumberMaxLength).IsRequired();
        builder.Property(rfq => rfq.RequisitionNumber).HasMaxLength(PurchaseRequisition.NumberMaxLength).IsRequired();
        builder.Property(rfq => rfq.RequisitionCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(rfq => rfq.RequisitionEstimatedTotal).HasPrecision(18, 2);
        builder.Property(rfq => rfq.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(rfq => rfq.EstimateInBaseCurrency).HasPrecision(18, 2);
        builder.Property(rfq => rfq.Method).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(rfq => rfq.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(rfq => rfq.CloseReason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Property(rfq => rfq.CancellationReason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Property(rfq => rfq.CancellationReasonCode).HasMaxLength(RequestForQuotation.ReasonCodeMaxLength);
        builder.Ignore(rfq => rfq.CurrentComparison);
        builder.Ignore(rfq => rfq.PendingAward);
        builder.Ignore(rfq => rfq.IsTerminal);
        builder.Ignore(rfq => rfq.IsActive);
        builder.Ignore(rfq => rfq.ActiveInvitations);
        builder.Ignore(rfq => rfq.RequiredPublishWaiver);
        builder.Ignore(rfq => rfq.DomainEvents);
        builder.HasIndex(rfq => rfq.Number).IsUnique().HasDatabaseName(IRequestForQuotationRepository.NumberUniqueKey);
        // SRC-001: one non-terminal RFQ per requisition, also when two buyers race.
        builder.HasIndex(rfq => rfq.RequisitionId).IsUnique().HasFilter("\"Status\" <> 'Cancelled'")
            .HasDatabaseName(IRequestForQuotationRepository.ActiveRfqUniqueKey);
        builder.HasIndex(rfq => new { rfq.RequisitionId, rfq.RoundNumber }).IsUnique();
        builder.HasIndex(rfq => new { rfq.Status, rfq.DeadlineUtc });
        builder.HasIndex(rfq => rfq.CreatedAtUtc);
        builder.HasOne<PurchaseRequisition>().WithMany().HasForeignKey(rfq => rfq.RequisitionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(rfq => rfq.Lines).WithOne().HasForeignKey(line => line.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rfq => rfq.Invitations).WithOne().HasForeignKey(invitation => invitation.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rfq => rfq.Quotes).WithOne().HasForeignKey(quote => quote.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rfq => rfq.Waivers).WithOne().HasForeignKey(waiver => waiver.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rfq => rfq.ExchangeRates).WithOne().HasForeignKey(rate => rate.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rfq => rfq.Comparisons).WithOne().HasForeignKey(comparison => comparison.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rfq => rfq.Awards).WithOne().HasForeignKey(award => award.RfqId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(rfq => rfq.History).WithOne().HasForeignKey(entry => entry.RfqId).OnDelete(DeleteBehavior.Restrict);
        foreach (var navigation in new[] { "Lines", "Invitations", "Quotes", "Waivers", "ExchangeRates", "Comparisons", "Awards", "History" })
            builder.Navigation(navigation).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RfqLineConfiguration : IEntityTypeConfiguration<RfqLine>
{
    public void Configure(EntityTypeBuilder<RfqLine> builder)
    {
        builder.ToTable("SourcingRfqLines", table => table.HasCheckConstraint("CK_SourcingRfqLines_Quantity", "\"Quantity\" > 0"));
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.Description).HasMaxLength(RequisitionLine.DescriptionMaxLength).IsRequired();
        builder.Property(line => line.Kind).HasMaxLength(20).IsRequired();
        builder.Property(line => line.CategoryCode).HasMaxLength(20).IsRequired();
        builder.Property(line => line.UnitCode).HasMaxLength(20).IsRequired();
        builder.Property(line => line.Quantity).HasPrecision(18, 3);
        builder.Property(line => line.EstimatedUnitPrice).HasPrecision(18, 4);
        builder.Property(line => line.EstimatedLineTotal).HasPrecision(18, 2);
        builder.HasIndex(line => new { line.RfqId, line.LineNumber }).IsUnique();
    }
}

public sealed class RfqInvitationConfiguration : IEntityTypeConfiguration<RfqInvitation>
{
    public void Configure(EntityTypeBuilder<RfqInvitation> builder)
    {
        builder.ToTable("SourcingRfqInvitations");
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.Id).ValueGeneratedNever();
        builder.Property(invitation => invitation.SupplierLegalName).HasMaxLength(RfqInvitation.LegalNameMaxLength).IsRequired();
        builder.Property(invitation => invitation.SupplierCountry).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(invitation => invitation.SupplierTaxId).HasMaxLength(RfqInvitation.TaxIdMaxLength).IsRequired();
        builder.Property(invitation => invitation.SupplierStatusAtInvitation).HasMaxLength(20).IsRequired();
        builder.Ignore(invitation => invitation.IsActive);
        builder.HasIndex(invitation => new { invitation.RfqId, invitation.Sequence }).IsUnique();
        builder.HasIndex(invitation => new { invitation.RfqId, invitation.SupplierId }).IsUnique().HasFilter("\"RemovedAtUtc\" IS NULL")
            .HasDatabaseName("IX_SourcingRfqInvitations_RfqId_SupplierId_Active");
        builder.HasIndex(invitation => invitation.SupplierId);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(invitation => invitation.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupplierQuoteConfiguration : IEntityTypeConfiguration<SupplierQuote>
{
    public void Configure(EntityTypeBuilder<SupplierQuote> builder)
    {
        builder.ToTable("SourcingQuotes");
        builder.HasKey(quote => quote.Id);
        builder.Property(quote => quote.Id).ValueGeneratedNever();
        builder.Ignore(quote => quote.Latest);
        builder.HasIndex(quote => new { quote.RfqId, quote.SupplierId }).IsUnique();
        builder.HasOne<RfqInvitation>().WithMany().HasForeignKey(quote => quote.InvitationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(quote => quote.Revisions).WithOne().HasForeignKey(revision => revision.QuoteId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(quote => quote.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class QuoteRevisionConfiguration : IEntityTypeConfiguration<QuoteRevision>
{
    public void Configure(EntityTypeBuilder<QuoteRevision> builder)
    {
        builder.ToTable("SourcingQuoteRevisions", table =>
        {
            table.HasCheckConstraint("CK_SourcingQuoteRevisions_Amounts",
                "\"Subtotal\" > 0 AND \"Freight\" >= 0 AND \"Discount\" >= 0 AND \"TaxesIncluded\" >= 0 AND \"Total\" >= 0");
            table.HasCheckConstraint("CK_SourcingQuoteRevisions_DeliveryDays", $"\"DeliveryDays\" >= 0 AND \"DeliveryDays\" <= {QuoteRevision.MaxDeliveryDays}");
        });
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.Property(revision => revision.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(revision => revision.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(revision => revision.Subtotal).HasPrecision(18, 2);
        builder.Property(revision => revision.Freight).HasPrecision(18, 2);
        builder.Property(revision => revision.Discount).HasPrecision(18, 2);
        builder.Property(revision => revision.TaxesIncluded).HasPrecision(18, 2);
        builder.Property(revision => revision.Total).HasPrecision(18, 2);
        builder.Property(revision => revision.PaymentTerms).HasMaxLength(QuoteRevision.PaymentTermsMaxLength).IsRequired();
        builder.Property(revision => revision.Notes).HasMaxLength(QuoteRevision.NotesMaxLength);
        builder.Property(revision => revision.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(revision => revision.WithdrawalReason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Ignore(revision => revision.TotalMoney);
        // SRC-009: two writers recording the same revision number lose on this guard.
        builder.HasIndex(revision => new { revision.QuoteId, revision.RevisionNumber }).IsUnique();
        builder.HasMany(revision => revision.Lines).WithOne().HasForeignKey(line => line.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(revision => revision.Attachments).WithOne().HasForeignKey(attachment => attachment.RevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(revision => revision.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(revision => revision.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class QuoteLineOfferConfiguration : IEntityTypeConfiguration<QuoteLineOffer>
{
    public void Configure(EntityTypeBuilder<QuoteLineOffer> builder)
    {
        builder.ToTable("SourcingQuoteLines", table =>
            table.HasCheckConstraint("CK_SourcingQuoteLines_Positive", "\"QuantityOffered\" > 0 AND \"UnitPrice\" > 0"));
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();
        builder.Property(line => line.QuantityOffered).HasPrecision(18, 3);
        builder.Property(line => line.UnitPrice).HasPrecision(18, 4);
        builder.Property(line => line.LineTotal).HasPrecision(18, 2);
        builder.HasIndex(line => new { line.RevisionId, line.RfqLineNumber }).IsUnique();
    }
}

public sealed class QuoteAttachmentConfiguration : IEntityTypeConfiguration<QuoteAttachment>
{
    public void Configure(EntityTypeBuilder<QuoteAttachment> builder)
    {
        builder.ToTable("SourcingQuoteAttachments", table =>
            table.HasCheckConstraint("CK_SourcingQuoteAttachments_Length", $"\"Length\" > 0 AND \"Length\" <= {FilePolicy.MaxBytes}"));
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Id).ValueGeneratedNever();
        builder.Property(attachment => attachment.FileName).HasMaxLength(FilePolicy.FileNameMaxLength).IsRequired();
        builder.Property(attachment => attachment.MediaType).HasMaxLength(FilePolicy.MediaTypeMaxLength).IsRequired();
        builder.HasIndex(attachment => attachment.RevisionId);
    }
}

public sealed class RfqWaiverConfiguration : IEntityTypeConfiguration<RfqWaiver>
{
    public void Configure(EntityTypeBuilder<RfqWaiver> builder)
    {
        builder.ToTable("SourcingWaivers");
        builder.HasKey(waiver => waiver.Id);
        builder.Property(waiver => waiver.Id).ValueGeneratedNever();
        builder.Property(waiver => waiver.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(waiver => waiver.RuleId).HasMaxLength(20).IsRequired();
        builder.Property(waiver => waiver.Permission).HasMaxLength(RfqWaiver.PermissionMaxLength).IsRequired();
        builder.Property(waiver => waiver.Reason).HasMaxLength(MasterDataRules.ReasonMaxLength).IsRequired();
        builder.HasIndex(waiver => new { waiver.RfqId, waiver.DecidedAtUtc });
    }
}

public sealed class RfqExchangeRateConfiguration : IEntityTypeConfiguration<RfqExchangeRate>
{
    public void Configure(EntityTypeBuilder<RfqExchangeRate> builder)
    {
        builder.ToTable("SourcingExchangeRates", table => table.HasCheckConstraint("CK_SourcingExchangeRates_Rate", "\"Rate\" > 0"));
        builder.HasKey(rate => rate.Id);
        builder.Property(rate => rate.Id).ValueGeneratedNever();
        builder.Property(rate => rate.Purpose).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(rate => rate.FromCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(rate => rate.ToCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(rate => rate.Rate).HasPrecision(18, 6);
        builder.Property(rate => rate.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(rate => rate.Provider).HasMaxLength(RfqExchangeRate.ProviderMaxLength).IsRequired();
        builder.Property(rate => rate.Reason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.HasIndex(rate => new { rate.RfqId, rate.Purpose, rate.FromCurrency });
    }
}

public sealed class RfqComparisonConfiguration : IEntityTypeConfiguration<RfqComparison>
{
    public void Configure(EntityTypeBuilder<RfqComparison> builder)
    {
        builder.ToTable("SourcingComparisons");
        builder.HasKey(comparison => comparison.Id);
        builder.Property(comparison => comparison.Id).ValueGeneratedNever();
        builder.Property(comparison => comparison.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(comparison => comparison.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(comparison => comparison.Warnings).HasMaxLength(RfqComparison.WarningsMaxLength).IsRequired();
        builder.Ignore(comparison => comparison.WarningList);
        builder.Ignore(comparison => comparison.MissingRateCurrencies);
        builder.HasIndex(comparison => new { comparison.RfqId, comparison.Sequence }).IsUnique();
        builder.HasMany(comparison => comparison.Entries).WithOne().HasForeignKey(entry => entry.ComparisonId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(comparison => comparison.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ComparisonEntryConfiguration : IEntityTypeConfiguration<ComparisonEntry>
{
    public void Configure(EntityTypeBuilder<ComparisonEntry> builder)
    {
        builder.ToTable("SourcingComparisonEntries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.SupplierLegalName).HasMaxLength(RfqInvitation.LegalNameMaxLength).IsRequired();
        builder.Property(entry => entry.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(entry => entry.RawTotal).HasPrecision(18, 2);
        builder.Property(entry => entry.NormalizedTotal).HasPrecision(18, 2);
        builder.Property(entry => entry.Rate).HasPrecision(18, 6);
        builder.Property(entry => entry.InvalidReasons).HasMaxLength(ComparisonEntry.InvalidReasonsMaxLength).IsRequired();
        builder.Ignore(entry => entry.InvalidReasonList);
        builder.HasIndex(entry => new { entry.ComparisonId, entry.QuoteRevisionId }).IsUnique();
    }
}

public sealed class RfqAwardConfiguration : IEntityTypeConfiguration<RfqAward>
{
    public void Configure(EntityTypeBuilder<RfqAward> builder)
    {
        builder.ToTable("SourcingAwards", table => table.HasCheckConstraint(
            "CK_SourcingAwards_Amounts", "\"AwardedTotal\" > 0 AND \"NormalizedTotal\" > 0 AND \"RequisitionCurrencyTotal\" > 0"));
        builder.HasKey(award => award.Id);
        builder.Property(award => award.Id).ValueGeneratedNever();
        builder.Property(award => award.SupplierLegalName).HasMaxLength(RfqInvitation.LegalNameMaxLength).IsRequired();
        builder.Property(award => award.AwardedTotal).HasPrecision(18, 2);
        builder.Property(award => award.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(award => award.NormalizedTotal).HasPrecision(18, 2);
        builder.Property(award => award.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(award => award.RequisitionCurrencyTotal).HasPrecision(18, 2);
        builder.Property(award => award.RequisitionCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(award => award.Justification).HasMaxLength(RfqAward.JustificationMaxLength);
        builder.Property(award => award.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        // SRC-016: at most one award PendingApproval per RFQ (guards concurrent submissions).
        builder.HasIndex(award => award.RfqId).IsUnique().HasFilter("\"Status\" = 'PendingApproval'")
            .HasDatabaseName("IX_SourcingAwards_RfqId_Pending");
        builder.HasIndex(award => award.SupplierId);
        builder.HasOne<QuoteRevision>().WithMany().HasForeignKey(award => award.QuoteRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RfqComparison>().WithMany().HasForeignKey(award => award.ComparisonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RfqHistoryEntryConfiguration : IEntityTypeConfiguration<RfqHistoryEntry>
{
    public void Configure(EntityTypeBuilder<RfqHistoryEntry> builder)
    {
        builder.ToTable("SourcingRfqHistory");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Action).HasMaxLength(RfqHistoryEntry.ActionMaxLength).IsRequired();
        builder.Property(entry => entry.From).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.To).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(entry => entry.Reason).HasMaxLength(MasterDataRules.ReasonMaxLength);
        builder.Property(entry => entry.CorrelationId).HasMaxLength(RfqHistoryEntry.CorrelationIdMaxLength).IsRequired();
        builder.Property(entry => entry.EventType).HasMaxLength(RfqHistoryEntry.EventTypeMaxLength);
        // A second writer appending the same sequence loses (guard of the root version token).
        builder.HasIndex(entry => new { entry.RfqId, entry.Sequence }).IsUnique();
    }
}

/// <summary>Per-year RFQ number counter (persistence-only; gaps are allowed).</summary>
public sealed class RfqNumberCounter
{
    public int Year { get; set; }
    public long LastValue { get; set; }
}

public sealed class RfqNumberCounterConfiguration : IEntityTypeConfiguration<RfqNumberCounter>
{
    public void Configure(EntityTypeBuilder<RfqNumberCounter> builder)
    {
        builder.ToTable("SourcingRfqNumberCounters");
        builder.HasKey(counter => counter.Year);
        builder.Property(counter => counter.Year).ValueGeneratedNever();
    }
}
