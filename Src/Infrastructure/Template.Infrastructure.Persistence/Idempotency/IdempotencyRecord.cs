using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Template.Infrastructure.Persistence.Idempotency;

/// <summary>
/// Persistence-only record of an idempotent request (docs/product/api-guidelines.md §6). It is an API transport
/// concern, not a domain entity. <see cref="StatusCode"/> is <c>null</c> while the first request is still executing.
/// </summary>
internal sealed class IdempotencyRecord
{
    public const int KeyMaxLength = 64;
    public const int OperationMaxLength = 300;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public string? ResponseBody { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("ApiIdempotencyRecords");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength).IsRequired();
        builder.Property(record => record.Operation).HasMaxLength(IdempotencyRecord.OperationMaxLength).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(record => record.ContentType).HasMaxLength(200);
        builder.Property(record => record.Location).HasMaxLength(2000);
        builder.HasIndex(record => new { record.UserId, record.Key }).IsUnique();
        builder.HasIndex(record => record.ExpiresAtUtc);
    }
}
