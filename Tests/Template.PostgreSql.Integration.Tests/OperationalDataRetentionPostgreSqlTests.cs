using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Idempotency;
using Template.Application.Interfaces.Maintenance;
using Template.Domain.Access;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Operational data retention (NFR §5) on real PostgreSQL 16.15: only rows past their window are removed, refresh-token
/// hashes go with their family through the FK cascade, and the batch bound is honored. All values are synthetic.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class OperationalDataRetentionPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private readonly MutableClock clock = new() { UtcNow = Now };
    private ServiceProvider provider = null!;

    public Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IAuthenticatedUserService>(new PostgreSqlFixture.TestAuthenticatedUser());
        provider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task SessionFamilies_PastAbsoluteExpiryPlusRetention_AreRemovedWithTheirTokens()
    {
        var policy = new SessionPolicy(TimeSpan.FromDays(7), TimeSpan.FromHours(12), TimeSpan.FromSeconds(10), 5);
        var old = SessionFamily.Start(Guid.NewGuid(), Guid.NewGuid(), Hash(), Hash(), Now.AddDays(-40), policy);
        var recent = SessionFamily.Start(Guid.NewGuid(), Guid.NewGuid(), Hash(), Hash(), Now.AddDays(-20), policy);
        recent.Revoke(SessionRevocationReason.Logout, Now.AddDays(-19));
        await using (var context = fixture.CreateContext())
        {
            context.AddRange(old, recent);
            await context.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IOperationalDataRetention>()
                .DeleteSessionFamiliesExpiredBeforeAsync(Now.AddDays(-30), 1000, CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);

        await using var verify = fixture.CreateContext();
        (await CountAsync(verify, "AccessSessionFamilies", old.Id)).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(verify,
            $"SELECT count(*) FROM \"AccessRefreshTokens\" WHERE \"FamilyId\" = '{old.Id}'")).ShouldBe(0);
        // Revoked but within absolute expiry + 30 days: kept for reuse detection.
        (await CountAsync(verify, "AccessSessionFamilies", recent.Id)).ShouldBe(1);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(verify,
            $"SELECT count(*) FROM \"AccessRefreshTokens\" WHERE \"FamilyId\" = '{recent.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task CompletedInboxEntries_OlderThanTheCutoff_AreRemovedInBoundedBatches()
    {
        var consumer = $"retention-{Guid.NewGuid():N}";
        var oldIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var recentId = Guid.NewGuid();
        await using (var context = fixture.CreateContext())
        {
            foreach (var (id, index) in oldIds.Select((id, index) => (id, index)))
            {
                var message = new InboxMessage(consumer, id, "procurement.test", Now.AddDays(-30 - index));
                message.Complete(Now.AddDays(-30 - index));
                context.Add(message);
            }

            var recent = new InboxMessage(consumer, recentId, "procurement.test", Now.AddDays(-1));
            recent.Complete(Now.AddDays(-1));
            context.Add(recent);
            await context.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var retention = scope.ServiceProvider.GetRequiredService<IOperationalDataRetention>();
            // The suite shares one database, so other tests' Inbox rows may also be old; the batch bound still holds.
            (await retention.DeleteCompletedInboxBeforeAsync(Now.AddDays(-7), 1, CancellationToken.None)).ShouldBe(1);
            while (await retention.DeleteCompletedInboxBeforeAsync(Now.AddDays(-7), 1000, CancellationToken.None) == 1000) { }
        }

        await using var verify = fixture.CreateContext();
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(verify,
            $"SELECT count(*) FROM \"MessagingInbox\" WHERE \"ConsumerName\" = '{consumer}' AND \"EventId\" <> '{recentId}'")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(verify,
            $"SELECT count(*) FROM \"MessagingInbox\" WHERE \"ConsumerName\" = '{consumer}' AND \"EventId\" = '{recentId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task IdempotencyRecords_AreRemovedOnlyAfterTheirLifetime()
    {
        var userId = Guid.NewGuid();
        var store = provider.GetRequiredService<IIdempotencyStore>();
        clock.UtcNow = Now - IIdempotencyStore.Lifetime - TimeSpan.FromMinutes(1);
        await store.BeginAsync(new IdempotencyRequest(userId, "expired-key", "POST /retention", new string('a', 64)), CancellationToken.None);
        clock.UtcNow = Now;
        await store.BeginAsync(new IdempotencyRequest(userId, "live-key", "POST /retention", new string('b', 64)), CancellationToken.None);

        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IOperationalDataRetention>()
                .DeleteExpiredIdempotencyRecordsAsync(Now, 1000, CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);

        await using var verify = fixture.CreateContext();
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(verify,
            $"SELECT count(*) FROM \"ApiIdempotencyRecords\" WHERE \"UserId\" = '{userId}' AND \"Key\" = 'expired-key'")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(verify,
            $"SELECT count(*) FROM \"ApiIdempotencyRecords\" WHERE \"UserId\" = '{userId}' AND \"Key\" = 'live-key'")).ShouldBe(1);
    }

    private static Task<long> CountAsync(Template.Infrastructure.Persistence.Contexts.ApplicationDbContext context, string table, Guid id) =>
        PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT count(*) FROM \"{table}\" WHERE \"Id\" = '{id}'");

    private static string Hash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; }
    }
}
