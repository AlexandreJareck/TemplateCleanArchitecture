#nullable enable
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Messaging;

public sealed class PostgreSqlMessagingStore(ApplicationDbContext dbContext) :
    IIntegrationEventOutbox,
    IOutboxStore,
    IProcurementEventEffectStore,
    INotificationDeliveryStore
{
    public void Add(IntegrationEventEnvelope envelope)
    {
        ProcurementIntegrationEventContract.Validate(envelope);
        dbContext.Set<OutboxMessage>().Add(new OutboxMessage(
            envelope.EventId, envelope.EventType, envelope.EventVersion, envelope.OccurredAtUtc,
            envelope.CorrelationId, envelope.CausationId, envelope.AggregateType, envelope.AggregateId,
            envelope.AggregateVersion, envelope.Payload.GetRawText(), envelope.OccurredAtUtc));
    }

    public async Task<IReadOnlyList<OutboxLease>> ClaimAsync(
        string owner, DateTime nowUtc, TimeSpan leaseDuration, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        batchSize = Math.Clamp(batchSize, 1, 500);
        var expiresAt = nowUtc.Add(leaseDuration);

        if (!dbContext.Database.IsRelational())
        {
            var memoryRows = await dbContext.Set<OutboxMessage>()
                .Where(message => message.DispatchedAtUtc == null && message.PoisonedAtUtc == null
                    && message.NextAttemptAtUtc <= nowUtc
                    && (message.ClaimExpiresAtUtc == null || message.ClaimExpiresAtUtc <= nowUtc))
                .OrderBy(message => message.CreatedAtUtc).ThenBy(message => message.EventId)
                .Take(batchSize).ToListAsync(cancellationToken);
            foreach (var row in memoryRows)
                row.Claim(owner, expiresAt);
            await dbContext.SaveChangesAsync(cancellationToken);
            return memoryRows.Select(row => Lease(row, owner)).ToArray();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var rows = await dbContext.Set<OutboxMessage>().FromSqlInterpolated($$"""
            SELECT candidate.*
            FROM "MessagingOutbox" AS candidate
            WHERE candidate."DispatchedAtUtc" IS NULL
              AND candidate."PoisonedAtUtc" IS NULL
              AND candidate."NextAttemptAtUtc" <= {{nowUtc}}
              AND (candidate."ClaimExpiresAtUtc" IS NULL OR candidate."ClaimExpiresAtUtc" <= {{nowUtc}})
              AND NOT EXISTS (
                  SELECT 1 FROM "MessagingOutbox" AS earlier
                  WHERE earlier."AggregateId" = candidate."AggregateId"
                    AND earlier."DispatchedAtUtc" IS NULL
                    AND earlier."PoisonedAtUtc" IS NULL
                    AND (earlier."CreatedAtUtc", earlier."EventId") < (candidate."CreatedAtUtc", candidate."EventId"))
            ORDER BY candidate."CreatedAtUtc", candidate."EventId"
            FOR UPDATE SKIP LOCKED
            LIMIT {{batchSize}}
            """).ToListAsync(cancellationToken);
        foreach (var row in rows)
            row.Claim(owner, expiresAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows.Select(row => Lease(row, owner)).ToArray();
    }

    public async Task MarkDispatchedAsync(Guid eventId, string owner, DateTime dispatchedAtUtc, CancellationToken cancellationToken)
    {
        var row = await ClaimedAsync(eventId, owner, cancellationToken);
        row.MarkDispatched(dispatchedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        Guid eventId, string owner, DateTime nowUtc, DateTime nextAttemptAtUtc, int maxAttempts,
        string failureCode, CancellationToken cancellationToken)
    {
        var row = await ClaimedAsync(eventId, owner, cancellationToken);
        row.MarkFailed(nowUtc, nextAttemptAtUtc, maxAttempts, failureCode);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteDispatchedBeforeAsync(DateTime cutoffUtc, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<OutboxMessage>().Where(message => message.DispatchedAtUtc < cutoffUtc)
            .OrderBy(message => message.DispatchedAtUtc).Take(Math.Clamp(maxRows, 1, 5000)).ToListAsync(cancellationToken);
        dbContext.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<InboxApplyResult> ApplyOnceAsync(
        string consumerName, IntegrationEventEnvelope envelope, DateTime processedAtUtc, CancellationToken cancellationToken)
    {
        ProcurementIntegrationEventContract.Validate(envelope);
        if (!dbContext.Database.IsRelational())
        {
            if (await dbContext.Set<InboxMessage>().FindAsync([consumerName, envelope.EventId], cancellationToken) is not null)
                return InboxApplyResult.Duplicate;
            var inbox = new InboxMessage(consumerName, envelope.EventId, envelope.EventType, processedAtUtc);
            dbContext.Add(inbox);
            var projection = await dbContext.Set<ProcurementProjectionState>().FindAsync(
                [consumerName, envelope.AggregateType, envelope.AggregateId, envelope.EventType], cancellationToken);
            if (projection is null)
                dbContext.Add(new ProcurementProjectionState(consumerName, envelope.AggregateType, envelope.AggregateId,
                    envelope.EventType, envelope.EventId, envelope.AggregateVersion, processedAtUtc));
            else
                projection.Apply(envelope.EventId, envelope.AggregateVersion, processedAtUtc);
            inbox.Complete(processedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return InboxApplyResult.Applied;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "MessagingInbox" ("ConsumerName", "EventId", "EventType", "ReceivedAtUtc", "CompletedAtUtc")
            VALUES ({{consumerName}}, {{envelope.EventId}}, {{envelope.EventType}}, {{processedAtUtc}}, NULL)
            ON CONFLICT ("ConsumerName", "EventId") DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InboxApplyResult.Duplicate;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "MessagingProcurementProjections"
                ("ConsumerName", "AggregateType", "AggregateId", "EventType", "LastEventId", "LastAggregateVersion", "ProcessedCount", "UpdatedAtUtc")
            VALUES ({{consumerName}}, {{envelope.AggregateType}}, {{envelope.AggregateId}}, {{envelope.EventType}},
                {{envelope.EventId}}, {{envelope.AggregateVersion}}, 1, {{processedAtUtc}})
            ON CONFLICT ("ConsumerName", "AggregateType", "AggregateId", "EventType") DO UPDATE SET
                "LastEventId" = EXCLUDED."LastEventId",
                "LastAggregateVersion" = EXCLUDED."LastAggregateVersion",
                "ProcessedCount" = "MessagingProcurementProjections"."ProcessedCount" + 1,
                "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "MessagingInbox" SET "CompletedAtUtc" = {{processedAtUtc}}
            WHERE "ConsumerName" = {{consumerName}} AND "EventId" = {{envelope.EventId}}
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InboxApplyResult.Applied;
    }

    public async Task<InboxApplyResult> DeliverOnceAsync(
        string consumerName, IntegrationEventEnvelope envelope, IReadOnlyList<NotificationRequest> requests, DateTime processedAtUtc,
        CancellationToken cancellationToken)
    {
        ProcurementIntegrationEventContract.Validate(envelope);
        if (!dbContext.Database.IsRelational())
        {
            if (await dbContext.Set<InboxMessage>().FindAsync([consumerName, envelope.EventId], cancellationToken) is not null)
                return InboxApplyResult.Duplicate;
            var inbox = new InboxMessage(consumerName, envelope.EventId, envelope.EventType, processedAtUtc);
            dbContext.Add(inbox);
            foreach (var request in requests)
                dbContext.Add(Delivery(consumerName, envelope, request, processedAtUtc));
            inbox.Complete(processedAtUtc);
            await dbContext.SaveChangesAsync(cancellationToken);
            return InboxApplyResult.Applied;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "MessagingInbox" ("ConsumerName", "EventId", "EventType", "ReceivedAtUtc", "CompletedAtUtc")
            VALUES ({{consumerName}}, {{envelope.EventId}}, {{envelope.EventType}}, {{processedAtUtc}}, NULL)
            ON CONFLICT ("ConsumerName", "EventId") DO NOTHING
            """, cancellationToken);
        if (inserted == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InboxApplyResult.Duplicate;
        }

        foreach (var request in requests)
        {
            var delivery = Delivery(consumerName, envelope, request, processedAtUtc);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "MessagingNotificationDeliveries"
                    ("Id", "ConsumerName", "EventId", "EventType", "RecipientType", "RecipientId", "Template", "AggregateType", "AggregateId",
                     "Channel", "Status", "CreatedAtUtc")
                VALUES ({{delivery.Id}}, {{delivery.ConsumerName}}, {{delivery.EventId}}, {{delivery.EventType}}, {{delivery.RecipientType}},
                    {{delivery.RecipientId}}, {{delivery.Template}}, {{delivery.AggregateType}}, {{delivery.AggregateId}}, {{delivery.Channel}},
                    {{delivery.Status}}, {{delivery.CreatedAtUtc}})
                ON CONFLICT ("EventId", "RecipientId", "Template") DO NOTHING
                """, cancellationToken);
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            UPDATE "MessagingInbox" SET "CompletedAtUtc" = {{processedAtUtc}}
            WHERE "ConsumerName" = {{consumerName}} AND "EventId" = {{envelope.EventId}}
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InboxApplyResult.Applied;
    }

    private static NotificationDelivery Delivery(string consumerName, IntegrationEventEnvelope envelope, NotificationRequest request, DateTime atUtc) =>
        new(Guid.NewGuid(), consumerName, envelope.EventId, envelope.EventType, request.RecipientType, request.RecipientId, request.Template,
            request.AggregateType, request.AggregateId, atUtc);

    private async Task<OutboxMessage> ClaimedAsync(Guid eventId, string owner, CancellationToken cancellationToken) =>
        await dbContext.Set<OutboxMessage>().SingleOrDefaultAsync(
            message => message.EventId == eventId && message.ClaimOwner == owner, cancellationToken)
        ?? throw new InvalidOperationException("The Outbox lease is missing or owned by another dispatcher.");

    private static OutboxLease Lease(OutboxMessage row, string owner)
    {
        using var document = JsonDocument.Parse(row.PayloadJson);
        var envelope = new IntegrationEventEnvelope(
            row.EventId, row.EventType, row.EventVersion, row.OccurredAtUtc, row.CorrelationId, row.CausationId,
            row.AggregateType, row.AggregateId, row.AggregateVersion, document.RootElement.Clone());
        return new OutboxLease(envelope, row.AttemptCount, owner);
    }
}
