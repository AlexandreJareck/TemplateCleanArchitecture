using Microsoft.EntityFrameworkCore;
using Shouldly;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;
using Template.Domain.Products.Entities;
using Template.Domain.Requisitions;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.PostgreSql.Integration.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ReliableMessagingPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"MessagingInbox\", \"MessagingOutbox\", \"MessagingProcurementProjections\"");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_CreatesAdditiveOutboxInboxAndProjectionTables()
    {
        await using var context = fixture.CreateContext();
        (await context.Database.GetAppliedMigrationsAsync()).ShouldContain(migration => migration.EndsWith("Phase28ReliableMessaging", StringComparison.Ordinal));
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN ('MessagingOutbox','MessagingInbox','MessagingProcurementProjections')
            """)).ShouldBe(3);
    }

    [Fact]
    public async Task Outbox_RollsBackWithTheOwningTransaction_AndCommitsAtomically()
    {
        var rolledBack = Envelope(Guid.NewGuid(), Guid.NewGuid());
        var rolledBackProductId = Guid.NewGuid();
        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Add(new ProductEntity(rolledBackProductId, "Phase 28 rollback", 1m, $"P28-R-{rolledBackProductId:N}"));
            new PostgreSqlMessagingStore(context).Add(rolledBack);
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using (var check = fixture.CreateContext())
        {
            (await check.Set<OutboxMessage>().AnyAsync(row => row.EventId == rolledBack.EventId)).ShouldBeFalse();
            (await check.Set<ProductEntity>().AnyAsync(row => row.Id == rolledBackProductId)).ShouldBeFalse();
        }

        var committed = Envelope(Guid.NewGuid(), Guid.NewGuid());
        var committedProductId = Guid.NewGuid();
        await using (var context = fixture.CreateContext())
        {
            context.Add(new ProductEntity(committedProductId, "Phase 28 commit", 1m, $"P28-C-{committedProductId:N}"));
            new PostgreSqlMessagingStore(context).Add(committed);
            await context.SaveChangesAsync();
        }
        await using (var check = fixture.CreateContext())
        {
            (await check.Set<OutboxMessage>().CountAsync(row => row.EventId == committed.EventId)).ShouldBe(1);
            (await check.Set<ProductEntity>().CountAsync(row => row.Id == committedProductId)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task TwoDispatchers_ClaimDisjointBatches_AndPreservePerAggregateOrder()
    {
        var aggregateA = Guid.NewGuid();
        var first = Envelope(Guid.NewGuid(), aggregateA, Now);
        var second = Envelope(Guid.NewGuid(), aggregateA, Now.AddMilliseconds(1));
        var other = Envelope(Guid.NewGuid(), Guid.NewGuid(), Now.AddMilliseconds(2));
        await SeedAsync(first, second, other);

        await using var contextA = fixture.CreateContext();
        await using var contextB = fixture.CreateContext();
        var claims = await Task.WhenAll(
            new PostgreSqlMessagingStore(contextA).ClaimAsync("worker-a", Now.AddMinutes(1), TimeSpan.FromMinutes(1), 10, CancellationToken.None),
            new PostgreSqlMessagingStore(contextB).ClaimAsync("worker-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1), 10, CancellationToken.None));
        var ids = claims.SelectMany(value => value).Select(value => value.Envelope.EventId).ToArray();

        ids.Distinct().Count().ShouldBe(ids.Length);
        ids.ShouldContain(first.EventId);
        ids.ShouldContain(other.EventId);
        ids.ShouldNotContain(second.EventId);
    }

    [Fact]
    public async Task ExpiredLease_IsRecovered_AndConfirmedDispatchIsNotClaimedAgain()
    {
        var envelope = Envelope(Guid.NewGuid(), Guid.NewGuid());
        await SeedAsync(envelope);
        await using (var firstContext = fixture.CreateContext())
        {
            var first = await new PostgreSqlMessagingStore(firstContext).ClaimAsync("worker-crashed", Now.AddMinutes(2), TimeSpan.FromSeconds(10), 1, CancellationToken.None);
            first.Single().Envelope.EventId.ShouldBe(envelope.EventId);
        }
        await using (var earlyContext = fixture.CreateContext())
            (await new PostgreSqlMessagingStore(earlyContext).ClaimAsync("worker-early", Now.AddMinutes(2).AddSeconds(5), TimeSpan.FromSeconds(10), 1, CancellationToken.None)).ShouldBeEmpty();
        await using (var recoveredContext = fixture.CreateContext())
        {
            var store = new PostgreSqlMessagingStore(recoveredContext);
            (await store.ClaimAsync("worker-recovered", Now.AddMinutes(2).AddSeconds(11), TimeSpan.FromSeconds(10), 1, CancellationToken.None)).Single();
            await store.MarkDispatchedAsync(envelope.EventId, "worker-recovered", Now.AddMinutes(3), CancellationToken.None);
        }
        await using (var finalContext = fixture.CreateContext())
            (await new PostgreSqlMessagingStore(finalContext).ClaimAsync("worker-later", Now.AddHours(1), TimeSpan.FromSeconds(10), 10, CancellationToken.None))
                .ShouldNotContain(item => item.Envelope.EventId == envelope.EventId);
    }

    [Fact]
    public async Task ConcurrentDuplicateDelivery_AppliesProjectionExactlyOnce()
    {
        var envelope = Envelope(Guid.NewGuid(), Guid.NewGuid());
        using var barrier = new Barrier(8);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var context = fixture.CreateContext();
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await new PostgreSqlMessagingStore(context).ApplyOnceAsync("projection-v1", envelope, Now, CancellationToken.None);
        })));

        results.Count(result => result == InboxApplyResult.Applied).ShouldBe(1);
        results.Count(result => result == InboxApplyResult.Duplicate).ShouldBe(7);
        await using var check = fixture.CreateContext();
        (await check.Set<InboxMessage>().CountAsync(row => row.ConsumerName == "projection-v1" && row.EventId == envelope.EventId)).ShouldBe(1);
        (await check.Set<ProcurementProjectionState>().SingleAsync(row => row.LastEventId == envelope.EventId)).ProcessedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Retention_DeletesOnlyConfirmedRowsOlderThanTheCutoff()
    {
        var old = Envelope(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-40));
        var recent = Envelope(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2));
        var pending = Envelope(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-60));
        await SeedAsync(old, recent, pending);

        await using (var context = fixture.CreateContext())
        {
            var store = new PostgreSqlMessagingStore(context);
            var claimed = await store.ClaimAsync("retention-worker", Now, TimeSpan.FromMinutes(1), 3, CancellationToken.None);
            claimed.Count.ShouldBe(3);
            await store.MarkDispatchedAsync(old.EventId, "retention-worker", Now.AddDays(-35), CancellationToken.None);
            await store.MarkDispatchedAsync(recent.EventId, "retention-worker", Now.AddDays(-1), CancellationToken.None);
            (await store.DeleteDispatchedBeforeAsync(Now.AddDays(-30), 100, CancellationToken.None)).ShouldBe(1);
        }

        await using var check = fixture.CreateContext();
        (await check.Set<OutboxMessage>().AnyAsync(row => row.EventId == old.EventId)).ShouldBeFalse();
        (await check.Set<OutboxMessage>().AnyAsync(row => row.EventId == recent.EventId)).ShouldBeTrue();
        (await check.Set<OutboxMessage>().AnyAsync(row => row.EventId == pending.EventId)).ShouldBeTrue();
    }

    private async Task SeedAsync(params IntegrationEventEnvelope[] envelopes)
    {
        await using var context = fixture.CreateContext();
        var store = new PostgreSqlMessagingStore(context);
        foreach (var envelope in envelopes)
            store.Add(envelope);
        await context.SaveChangesAsync();
    }

    private static IntegrationEventEnvelope Envelope(Guid eventId, Guid aggregateId, DateTime? occurredAt = null) =>
        ProcurementIntegrationEventContract.FromDomainEvent(
            new RequisitionExpired(occurredAt ?? Now, aggregateId, 1), "phase28-pg", eventId: eventId);
}
