#nullable enable
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.Infrastructure.Persistence.Contexts.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("MessagingOutbox");
        builder.HasKey(message => message.EventId);
        builder.Property(message => message.EventId).ValueGeneratedNever();
        builder.Property(message => message.EventType).HasMaxLength(160).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(message => message.AggregateType).HasMaxLength(100).IsRequired();
        builder.Property(message => message.AggregateVersion).HasMaxLength(64).IsRequired();
        builder.Property(message => message.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.ClaimOwner).HasMaxLength(128);
        builder.Property(message => message.LastFailureCode).HasMaxLength(100);
        builder.HasIndex(message => new { message.DispatchedAtUtc, message.PoisonedAtUtc, message.NextAttemptAtUtc });
        builder.HasIndex(message => new { message.AggregateId, message.CreatedAtUtc, message.EventId });
        builder.HasIndex(message => message.ClaimExpiresAtUtc);
    }
}

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("MessagingInbox");
        builder.HasKey(message => new { message.ConsumerName, message.EventId });
        builder.Property(message => message.ConsumerName).HasMaxLength(128);
        builder.Property(message => message.EventType).HasMaxLength(160).IsRequired();
        builder.HasIndex(message => message.ReceivedAtUtc);
        builder.HasIndex(message => message.CompletedAtUtc);
    }
}

public sealed class ProcurementProjectionStateConfiguration : IEntityTypeConfiguration<ProcurementProjectionState>
{
    public void Configure(EntityTypeBuilder<ProcurementProjectionState> builder)
    {
        builder.ToTable("MessagingProcurementProjections");
        builder.HasKey(state => new { state.ConsumerName, state.AggregateType, state.AggregateId, state.EventType });
        builder.Property(state => state.ConsumerName).HasMaxLength(128);
        builder.Property(state => state.AggregateType).HasMaxLength(100);
        builder.Property(state => state.EventType).HasMaxLength(160);
        builder.Property(state => state.LastAggregateVersion).HasMaxLength(64).IsRequired();
        builder.HasIndex(state => state.LastEventId);
        builder.HasIndex(state => state.UpdatedAtUtc);
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("MessagingNotificationDeliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();
        builder.Property(delivery => delivery.ConsumerName).HasMaxLength(128).IsRequired();
        builder.Property(delivery => delivery.EventType).HasMaxLength(160).IsRequired();
        builder.Property(delivery => delivery.RecipientType).HasMaxLength(40).IsRequired();
        builder.Property(delivery => delivery.Template).HasMaxLength(100).IsRequired();
        builder.Property(delivery => delivery.AggregateType).HasMaxLength(100).IsRequired();
        builder.Property(delivery => delivery.Channel).HasMaxLength(20).IsRequired();
        builder.Property(delivery => delivery.Status).HasMaxLength(20).IsRequired();
        // Event catalog §3: delivery records are idempotent per (event, recipient, template).
        builder.HasIndex(delivery => new { delivery.EventId, delivery.RecipientId, delivery.Template }).IsUnique();
        builder.HasIndex(delivery => new { delivery.AggregateId, delivery.CreatedAtUtc });
    }
}
