using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Catalog;
using Template.Domain.Catalog;
using Template.Domain.Common;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>Catalog module tables; unique upper-cased codes (CAT-001) and restricted deletes (ORG-005).</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("CatalogCategories");
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.Property(category => category.Version).IsConcurrencyToken();
        builder.Property(category => category.Code).HasMaxLength(20).IsRequired();
        builder.Property(category => category.Name).HasMaxLength(MasterDataRules.NameMaxLength).IsRequired();
        builder.HasIndex(category => category.Code).IsUnique().HasDatabaseName(ICategoryRepository.CodeUniqueKey);
    }
}

public sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("CatalogUnitsOfMeasure", table =>
            table.HasCheckConstraint("CK_CatalogUnitsOfMeasure_AllowedDecimals", "\"AllowedDecimals\" BETWEEN 0 AND 3"));
        builder.HasKey(unit => unit.Id);
        builder.Property(unit => unit.Id).ValueGeneratedNever();
        builder.Property(unit => unit.Version).IsConcurrencyToken();
        builder.Property(unit => unit.Code).HasMaxLength(20).IsRequired();
        builder.Property(unit => unit.Name).HasMaxLength(MasterDataRules.NameMaxLength).IsRequired();
        builder.HasIndex(unit => unit.Code).IsUnique().HasDatabaseName(IUnitOfMeasureRepository.CodeUniqueKey);
    }
}

public sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("CatalogItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Version).IsConcurrencyToken();
        builder.Property(item => item.Code).HasMaxLength(40).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(CatalogItem.DescriptionMaxLength).IsRequired();
        builder.Property(item => item.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(item => item.ReferencePriceAmount).HasPrecision(18, 4);
        builder.Property(item => item.ReferencePriceCurrency).HasMaxLength(3).IsFixedLength();
        builder.HasIndex(item => item.Code).IsUnique().HasDatabaseName(ICatalogItemRepository.CodeUniqueKey);
        builder.HasIndex(item => new { item.CategoryId, item.IsActive });
        builder.HasIndex(item => item.UnitOfMeasureId);
        builder.HasOne<Category>().WithMany().HasForeignKey(item => item.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(item => item.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);
    }
}
