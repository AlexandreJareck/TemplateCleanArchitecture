using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Domain.Products.Entities;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;
public class ProductConfiguration : IEntityTypeConfiguration<ProductEntity>
{
    public void Configure(EntityTypeBuilder<ProductEntity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.BarCode).HasMaxLength(50);
    }
}
