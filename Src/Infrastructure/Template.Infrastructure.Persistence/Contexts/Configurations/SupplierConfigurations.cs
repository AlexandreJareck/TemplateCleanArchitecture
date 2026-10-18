using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Application.Interfaces.Suppliers;
using Template.Domain.Catalog;
using Template.Domain.Suppliers;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Suppliers module tables. The normalized tax ID is unique per country (SUP-001); contacts, addresses, category
/// links and status history are owned rows of the aggregate (one version token on the root, SP-1). Category links
/// reference catalog categories with a restricted delete (ORG-005).
/// </summary>
public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(supplier => supplier.Id);
        builder.Property(supplier => supplier.Id).ValueGeneratedNever();
        builder.Property(supplier => supplier.Version).IsConcurrencyToken();
        builder.Property(supplier => supplier.LegalName).HasMaxLength(Supplier.LegalNameMaxLength).IsRequired();
        builder.Property(supplier => supplier.TradeName).HasMaxLength(Supplier.TradeNameMaxLength);
        builder.Property(supplier => supplier.Country).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(supplier => supplier.TaxId).HasMaxLength(TaxIdentifier.MaxLength).IsRequired();
        builder.Property(supplier => supplier.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(supplier => supplier.IsEligibleForNewCommitments);
        builder.HasIndex(supplier => new { supplier.Country, supplier.TaxId }).IsUnique().HasDatabaseName(ISupplierRepository.TaxIdUniqueKey);
        builder.HasIndex(supplier => supplier.Status);
        builder.HasIndex(supplier => supplier.LegalName);

        builder.OwnsOne(supplier => supplier.Enrichment, enrichment =>
        {
            enrichment.Property(snapshot => snapshot.Provider).HasColumnName("EnrichmentProvider").HasMaxLength(SupplierEnrichmentSnapshot.ProviderMaxLength).IsRequired();
            enrichment.Property(snapshot => snapshot.RetrievedAtUtc).HasColumnName("EnrichmentRetrievedAtUtc");
            enrichment.Property(snapshot => snapshot.AcceptedAtUtc).HasColumnName("EnrichmentAcceptedAtUtc");
            enrichment.Property(snapshot => snapshot.AcceptedBy).HasColumnName("EnrichmentAcceptedBy");
            enrichment.Property(snapshot => snapshot.AcceptedFields).HasColumnName("EnrichmentAcceptedFields").HasMaxLength(SupplierEnrichmentSnapshot.AcceptedFieldsMaxLength).IsRequired();
            enrichment.Property(snapshot => snapshot.LegalName).HasColumnName("EnrichmentLegalName").HasMaxLength(Supplier.LegalNameMaxLength);
            enrichment.Property(snapshot => snapshot.TradeName).HasColumnName("EnrichmentTradeName").HasMaxLength(Supplier.TradeNameMaxLength);
            enrichment.Property(snapshot => snapshot.RegistrationStatus).HasColumnName("EnrichmentRegistrationStatus").HasMaxLength(SupplierEnrichmentSnapshot.RegistrationStatusMaxLength);
            enrichment.Property(snapshot => snapshot.PostalCode).HasColumnName("EnrichmentPostalCode").HasMaxLength(SupplierAddress.PostalCodeMaxLength);
        });

        builder.HasMany(supplier => supplier.Contacts).WithOne().HasForeignKey(contact => contact.SupplierId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(supplier => supplier.Addresses).WithOne().HasForeignKey(address => address.SupplierId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(supplier => supplier.Categories).WithOne().HasForeignKey(link => link.SupplierId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(supplier => supplier.StatusHistory).WithOne().HasForeignKey(change => change.SupplierId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(supplier => supplier.Contacts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(supplier => supplier.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(supplier => supplier.Categories).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(supplier => supplier.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class SupplierContactConfiguration : IEntityTypeConfiguration<SupplierContact>
{
    public void Configure(EntityTypeBuilder<SupplierContact> builder)
    {
        builder.ToTable("SupplierContacts");
        builder.HasKey(contact => contact.Id);
        builder.Property(contact => contact.Id).ValueGeneratedNever();
        builder.Property(contact => contact.Name).HasMaxLength(SupplierContact.NameMaxLength).IsRequired();
        builder.Property(contact => contact.Email).HasMaxLength(SupplierContact.EmailMaxLength);
        builder.Property(contact => contact.Phone).HasMaxLength(SupplierContact.PhoneMaxLength);
        builder.Property(contact => contact.Role).HasMaxLength(SupplierContact.RoleMaxLength);
    }
}

public sealed class SupplierAddressConfiguration : IEntityTypeConfiguration<SupplierAddress>
{
    public void Configure(EntityTypeBuilder<SupplierAddress> builder)
    {
        builder.ToTable("SupplierAddresses");
        builder.HasKey(address => address.Id);
        builder.Property(address => address.Id).ValueGeneratedNever();
        builder.Property(address => address.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(address => address.PostalCode).HasMaxLength(SupplierAddress.PostalCodeMaxLength).IsRequired();
        builder.Property(address => address.Street).HasMaxLength(SupplierAddress.StreetMaxLength).IsRequired();
        builder.Property(address => address.Number).HasMaxLength(SupplierAddress.NumberMaxLength);
        builder.Property(address => address.Complement).HasMaxLength(SupplierAddress.ComplementMaxLength);
        builder.Property(address => address.District).HasMaxLength(SupplierAddress.DistrictMaxLength);
        builder.Property(address => address.City).HasMaxLength(SupplierAddress.CityMaxLength).IsRequired();
        builder.Property(address => address.State).HasMaxLength(SupplierAddress.StateMaxLength).IsRequired();
        builder.Property(address => address.Country).HasMaxLength(2).IsFixedLength().IsRequired();
    }
}

public sealed class SupplierCategoryConfiguration : IEntityTypeConfiguration<SupplierCategory>
{
    public void Configure(EntityTypeBuilder<SupplierCategory> builder)
    {
        builder.ToTable("SupplierCategories");
        builder.HasKey(link => new { link.SupplierId, link.CategoryId });
        builder.HasIndex(link => link.CategoryId);
        builder.HasOne<Category>().WithMany().HasForeignKey(link => link.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupplierStatusChangeConfiguration : IEntityTypeConfiguration<SupplierStatusChange>
{
    public void Configure(EntityTypeBuilder<SupplierStatusChange> builder)
    {
        builder.ToTable("SupplierStatusChanges");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.From).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(change => change.To).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(change => change.Reason).HasMaxLength(Template.Domain.Common.MasterDataRules.ReasonMaxLength);
        // A second writer appending the same sequence loses (guard of the root version token).
        builder.HasIndex(change => new { change.SupplierId, change.Sequence }).IsUnique();
    }
}
