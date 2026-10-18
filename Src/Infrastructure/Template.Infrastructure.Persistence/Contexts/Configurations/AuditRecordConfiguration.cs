using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Domain.Auditing;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

/// <summary>
/// Insert-only audit table (AUD-001/002). Details are stored as JSON text of allow-listed keys (AUD-003); rule IDs as a
/// PostgreSQL <c>text[]</c>.
/// </summary>
public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.Action).HasMaxLength(AuditRecord.ActionMaxLength).IsRequired();
        builder.Property(record => record.ResourceType).HasMaxLength(AuditRecord.ResourceTypeMaxLength).IsRequired();
        builder.Property(record => record.ResourceId).HasMaxLength(AuditRecord.ResourceIdMaxLength).IsRequired();
        builder.Property(record => record.FromState).HasMaxLength(AuditRecord.StateMaxLength);
        builder.Property(record => record.ToState).HasMaxLength(AuditRecord.StateMaxLength);
        builder.Property(record => record.Reason).HasMaxLength(AuditRecord.ReasonMaxLength);
        builder.Property(record => record.CorrelationId).HasMaxLength(AuditRecord.CorrelationIdMaxLength).IsRequired();
        builder.Property(record => record.CausationId).HasMaxLength(AuditRecord.CorrelationIdMaxLength);
        builder.Property(record => record.RuleIds).IsRequired();
        builder.Property(record => record.Details)
            .HasConversion(
                details => JsonSerializer.Serialize(details, JsonOptions),
                json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>(),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (left, right) => left!.Count == right!.Count && !left.Except(right).Any(),
                    details => details.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                    details => details.ToDictionary(pair => pair.Key, pair => pair.Value)))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(record => record.OccurredAtUtc);
        builder.HasIndex(record => new { record.ResourceType, record.ResourceId });
        builder.HasIndex(record => record.ActorId);
    }
}
