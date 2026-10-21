using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Budgeting;
using Template.Domain.Budgeting;
using Template.Domain.Organization;
using Template.Domain.Requisitions;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Budgeting module tables (BUD-*). Period codes are unique and ranges never overlap (exclusion constraint added by the
/// migration); allocations are unique per (period, cost center, currency) and keep reserved/committed totals that a
/// check constraint bounds by the amount, so availability can never become negative even outside the application.
/// Reservations and every history row are owned by the allocation aggregate (one version token, SP-1).
/// </summary>
public sealed class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> builder)
    {
        builder.ToTable("BudgetFiscalPeriods", table =>
            table.HasCheckConstraint("CK_BudgetFiscalPeriods_DateRange", "\"EndDate\" >= \"StartDate\""));
        builder.HasKey(period => period.Id);
        builder.Property(period => period.Id).ValueGeneratedNever();
        builder.Property(period => period.Version).IsConcurrencyToken();
        builder.Property(period => period.Code).HasMaxLength(20).IsRequired();
        builder.Property(period => period.Name).HasMaxLength(Template.Domain.Common.MasterDataRules.NameMaxLength).IsRequired();
        builder.Property(period => period.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(period => period.Code).IsUnique().HasDatabaseName(IFiscalPeriodRepository.CodeUniqueKey);
        builder.HasIndex(period => new { period.Status, period.StartDate, period.EndDate });
    }
}

public sealed class BudgetAllocationConfiguration : IEntityTypeConfiguration<BudgetAllocation>
{
    public void Configure(EntityTypeBuilder<BudgetAllocation> builder)
    {
        builder.ToTable("BudgetAllocations", table =>
        {
            table.HasCheckConstraint("CK_BudgetAllocations_Amount", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_BudgetAllocations_Totals", "\"ReservedAmount\" >= 0 AND \"CommittedAmount\" >= 0 AND \"ReservedAmount\" + \"CommittedAmount\" <= \"Amount\"");
        });
        builder.HasKey(allocation => allocation.Id);
        builder.Property(allocation => allocation.Id).ValueGeneratedNever();
        builder.Property(allocation => allocation.Version).IsConcurrencyToken();
        builder.Property(allocation => allocation.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(allocation => allocation.Amount).HasPrecision(18, 2);
        builder.Property(allocation => allocation.ReservedAmount).HasPrecision(18, 2);
        builder.Property(allocation => allocation.CommittedAmount).HasPrecision(18, 2);
        builder.Property(allocation => allocation.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(allocation => allocation.AvailableAmount);
        builder.Ignore(allocation => allocation.Available);
        builder.Ignore(allocation => allocation.DomainEvents);
        builder.HasIndex(allocation => new { allocation.FiscalPeriodId, allocation.CostCenterId, allocation.Currency })
            .IsUnique().HasDatabaseName(IBudgetAllocationRepository.UniqueKey);
        builder.HasIndex(allocation => allocation.CostCenterId);
        builder.HasOne<FiscalPeriod>().WithMany().HasForeignKey(allocation => allocation.FiscalPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CostCenter>().WithMany().HasForeignKey(allocation => allocation.CostCenterId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(allocation => allocation.Reservations).WithOne().HasForeignKey(reservation => reservation.AllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(allocation => allocation.AmountHistory).WithOne().HasForeignKey(change => change.AllocationId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(allocation => allocation.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(allocation => allocation.AmountHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class BudgetReservationConfiguration : IEntityTypeConfiguration<BudgetReservation>
{
    public void Configure(EntityTypeBuilder<BudgetReservation> builder)
    {
        builder.ToTable("BudgetReservations", table => table.HasCheckConstraint("CK_BudgetReservations_Amount", "\"Amount\" > 0"));
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();
        builder.Property(reservation => reservation.Amount).HasPrecision(18, 2);
        builder.Property(reservation => reservation.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(reservation => reservation.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(reservation => reservation.ReleaseReason).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(reservation => reservation.IsActive);
        // BUD-008: one active reservation per requisition across every allocation (also guards concurrent writers).
        builder.HasIndex(reservation => reservation.RequisitionId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Reserved', 'Committed')")
            .HasDatabaseName(IBudgetAllocationRepository.ActiveReservationUniqueKey);
        builder.HasIndex(reservation => new { reservation.AllocationId, reservation.Status });
        builder.HasIndex(reservation => new { reservation.RequisitionId, reservation.CreatedAtUtc });
        builder.HasOne<PurchaseRequisition>().WithMany().HasForeignKey(reservation => reservation.RequisitionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(reservation => reservation.Changes).WithOne().HasForeignKey(change => change.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(reservation => reservation.Changes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class BudgetReservationChangeConfiguration : IEntityTypeConfiguration<BudgetReservationChange>
{
    public void Configure(EntityTypeBuilder<BudgetReservationChange> builder)
    {
        builder.ToTable("BudgetReservationChanges");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.Operation).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(change => change.From).HasConversion<string>().HasMaxLength(20);
        builder.Property(change => change.To).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(change => change.Amount).HasPrecision(18, 2);
        builder.Property(change => change.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(change => change.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(change => change.CorrelationId).HasMaxLength(BudgetReservationChange.CorrelationIdMaxLength).IsRequired();
        builder.HasIndex(change => new { change.ReservationId, change.Sequence }).IsUnique();
    }
}

public sealed class BudgetAllocationAmountChangeConfiguration : IEntityTypeConfiguration<BudgetAllocationAmountChange>
{
    public void Configure(EntityTypeBuilder<BudgetAllocationAmountChange> builder)
    {
        builder.ToTable("BudgetAllocationAmountChanges");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.PreviousAmount).HasPrecision(18, 2);
        builder.Property(change => change.NewAmount).HasPrecision(18, 2);
        builder.Property(change => change.Reason).HasMaxLength(Template.Domain.Common.MasterDataRules.ReasonMaxLength);
        // A second writer appending the same sequence loses (guard of the root version token).
        builder.HasIndex(change => new { change.AllocationId, change.Sequence }).IsUnique();
    }
}
