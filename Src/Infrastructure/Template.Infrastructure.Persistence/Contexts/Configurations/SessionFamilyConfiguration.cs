using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Domain.Access;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Access module tables (ADR-0001 module prefix). <see cref="SessionFamily.Version"/> is the optimistic-concurrency
/// token (SP-1), so two rotations of the same family cannot both commit (SEC-004).
/// </summary>
public sealed class SessionFamilyConfiguration : IEntityTypeConfiguration<SessionFamily>
{
    public void Configure(EntityTypeBuilder<SessionFamily> builder)
    {
        builder.ToTable("AccessSessionFamilies");
        builder.HasKey(family => family.Id);
        builder.Property(family => family.Id).ValueGeneratedNever();
        builder.Property(family => family.Version).IsConcurrencyToken();
        builder.Property(family => family.SecurityStampFingerprint).HasMaxLength(SessionFamily.HashLength).IsFixedLength().IsRequired();
        builder.Property(family => family.RevocationReason).HasConversion<string>().HasMaxLength(40);
        builder.Ignore(family => family.IsRevoked);
        builder.Ignore(family => family.CookieExpiresAtUtc);
        builder.HasIndex(family => new { family.UserId, family.RevokedAtUtc });

        builder.HasMany(family => family.Tokens)
            .WithOne()
            .HasForeignKey(token => token.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(family => family.Tokens).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RefreshTokenRecordConfiguration : IEntityTypeConfiguration<RefreshTokenRecord>
{
    public void Configure(EntityTypeBuilder<RefreshTokenRecord> builder)
    {
        builder.ToTable("AccessRefreshTokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.TokenHash).HasMaxLength(SessionFamily.HashLength).IsFixedLength().IsRequired();
        builder.Ignore(token => token.IsCurrent);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.FamilyId, token.Sequence }).IsUnique();
    }
}
