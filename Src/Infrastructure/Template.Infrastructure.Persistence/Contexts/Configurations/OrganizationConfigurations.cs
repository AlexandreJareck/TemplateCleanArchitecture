using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Organization;
using Template.Domain.Common;
using Template.Domain.Organization;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Organization module tables (ADR-0001 module prefix). <see cref="VersionedAggregateRoot.Version"/> is the
/// concurrency token (SP-1); codes are stored upper-cased so the unique indexes are case-insensitive (ORG-001).
/// </summary>
public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("OrganizationDepartments");
        builder.HasKey(department => department.Id);
        builder.Property(department => department.Id).ValueGeneratedNever();
        builder.Property(department => department.Version).IsConcurrencyToken();
        builder.Property(department => department.Code).HasMaxLength(20).IsRequired();
        builder.Property(department => department.Name).HasMaxLength(MasterDataRules.NameMaxLength).IsRequired();
        builder.HasIndex(department => department.Code).IsUnique().HasDatabaseName(IDepartmentRepository.CodeUniqueKey);
        builder.HasIndex(department => department.IsActive);
    }
}

public sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.ToTable("OrganizationCostCenters");
        builder.HasKey(costCenter => costCenter.Id);
        builder.Property(costCenter => costCenter.Id).ValueGeneratedNever();
        builder.Property(costCenter => costCenter.Version).IsConcurrencyToken();
        builder.Property(costCenter => costCenter.Code).HasMaxLength(20).IsRequired();
        builder.Property(costCenter => costCenter.Name).HasMaxLength(MasterDataRules.NameMaxLength).IsRequired();
        builder.HasIndex(costCenter => costCenter.Code).IsUnique().HasDatabaseName(ICostCenterRepository.CodeUniqueKey);
        builder.HasIndex(costCenter => new { costCenter.DepartmentId, costCenter.IsActive });
        builder.HasIndex(costCenter => costCenter.ManagerUserId);
        // ORG-002/ORG-005: the owning department can never be deleted while referenced.
        builder.HasOne<Department>().WithMany().HasForeignKey(costCenter => costCenter.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class OrganizationSettingsConfiguration : IEntityTypeConfiguration<OrganizationSettings>
{
    public void Configure(EntityTypeBuilder<OrganizationSettings> builder)
    {
        builder.ToTable("OrganizationSettings");
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();
        builder.Property(settings => settings.Version).IsConcurrencyToken();
        builder.Property(settings => settings.DisplayName).HasMaxLength(MasterDataRules.NameMaxLength).IsRequired();
        builder.Property(settings => settings.BaseCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(settings => settings.TimeZone).HasMaxLength(OrganizationSettings.TimeZoneMaxLength).IsRequired();
        builder.Property(settings => settings.SupportedCurrencies).IsRequired();
    }
}
