using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Auditing;
using Template.Application.Interfaces.Idempotency;
using Template.Domain.Access;
using Template.Domain.Auditing;
using Template.Infrastructure.Persistence;

namespace Template.Integration.Tests.Data;

/// <summary>
/// Session, audit and idempotency adapters on EF InMemory through the real Persistence registration. InMemory does not
/// enforce unique indexes or PostgreSQL types; the PostgreSQL suite covers those semantics.
/// </summary>
public sealed class AccessPersistenceAdapterTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly TestClock clock = new() { UtcNow = Now };
    private readonly ServiceProvider provider;

    public AccessPersistenceAdapterTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestDatabaseName"] = Guid.NewGuid().ToString("N") })
            .Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: true);
        services.AddSingleton<IClock>(clock);
        services.AddScoped<IAuthenticatedUserService>(_ => new AuthenticatedUserService(Guid.NewGuid().ToString(), "integration"));
        provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task SessionRepository_FindsFamilyByAnyOfItsTokenHashes_AndListsActiveOldestFirst()
    {
        var userId = Guid.NewGuid();
        var policy = SessionPolicy.Version1;
        await using (var scope = provider.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionFamilyRepository>();
            var older = SessionFamily.Start(Guid.NewGuid(), userId, Hash("stamp"), Hash("t1"), Now.AddMinutes(-5), policy);
            older.Refresh(Hash("t1"), Hash("t2"), Hash("stamp"), Now, policy);
            sessions.Add(older);
            sessions.Add(SessionFamily.Start(Guid.NewGuid(), userId, Hash("stamp"), Hash("t3"), Now, policy));
            var revoked = SessionFamily.Start(Guid.NewGuid(), userId, Hash("stamp"), Hash("t4"), Now, policy);
            revoked.Revoke(SessionRevocationReason.Logout, Now);
            sessions.Add(revoked);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var repository = read.ServiceProvider.GetRequiredService<ISessionFamilyRepository>();
        var byOldToken = await repository.FindByTokenHashAsync(Hash("t1"), CancellationToken.None);
        var byNewToken = await repository.FindByTokenHashAsync(Hash("t2"), CancellationToken.None);

        byOldToken.ShouldNotBeNull();
        byOldToken.Id.ShouldBe(byNewToken!.Id);
        byOldToken.Tokens.Count.ShouldBe(2);
        (await repository.FindByTokenHashAsync(Hash("unknown"), CancellationToken.None)).ShouldBeNull();
        var active = await repository.ListActiveAsync(userId, Now, CancellationToken.None);
        active.Count.ShouldBe(2);
        active[0].Id.ShouldBe(byOldToken.Id);
        (await repository.IsActiveAsync(active[1].Id, userId, Now, CancellationToken.None)).ShouldBeTrue();
        (await repository.IsActiveAsync(active[1].Id, Guid.NewGuid(), Now, CancellationToken.None)).ShouldBeFalse();
        (await repository.IsActiveAsync(active[1].Id, userId, active[1].IdleExpiresAtUtc, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task AuditReader_FiltersSortsAndPagesWithStableTieBreaker()
    {
        var actor = Guid.NewGuid();
        await using (var scope = provider.CreateAsyncScope())
        {
            var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
            for (var index = 0; index < 5; index++)
                trail.Append(AuditRecord.Create(Guid.NewGuid(), Now.AddMinutes(index % 2), actor, "security.session.created",
                    "session-family", $"family-{index}", "trace", details: new Dictionary<string, string> { ["reason"] = "r" }));
            trail.Append(AuditRecord.Create(Guid.NewGuid(), Now.AddMinutes(9), null, "security.rate-limited", "endpoint", "POST /api/v2/auth/sessions", "trace"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var reader = read.ServiceProvider.GetRequiredService<IAuditRecordReader>();
        var sort = PageRequest.Create(1, 2, "-occurredAt", IAuditRecordReader.SortFields, "-occurredAt").Value;

        var firstPage = await reader.ListAsync(new AuditRecordFilter(ActorId: actor), sort, CancellationToken.None);
        var secondPage = await reader.ListAsync(new AuditRecordFilter(ActorId: actor), sort with { Page = 2 }, CancellationToken.None);
        var all = await reader.ListAsync(new AuditRecordFilter(), PageRequest.Create(1, 100, "action", IAuditRecordReader.SortFields, "-occurredAt").Value, CancellationToken.None);
        var window = await reader.ListAsync(new AuditRecordFilter(FromUtc: Now.AddMinutes(1), ToUtc: Now.AddMinutes(9)), sort with { PageSize = 100 }, CancellationToken.None);

        firstPage.TotalItems.ShouldBe(5);
        firstPage.TotalPages.ShouldBe(3);
        firstPage.Items.ShouldAllBe(item => item.OccurredAt == Now.AddMinutes(1));
        firstPage.Items.Select(item => item.Id).Concat(secondPage.Items.Select(item => item.Id)).ShouldBeUnique();
        firstPage.Items[0].Id.CompareTo(firstPage.Items[1].Id).ShouldBeLessThan(0);
        all.Items.Select(item => item.Action).ShouldBe(all.Items.Select(item => item.Action).Order(StringComparer.Ordinal));
        all.Items.First(item => item.Action == "security.session.created").Details.ShouldBe(new Dictionary<string, string> { ["reason"] = "r" });
        window.TotalItems.ShouldBe(2);
    }

    [Fact]
    public async Task IdempotencyStore_StartsReplaysDetectsReuseAndReleases()
    {
        var request = new IdempotencyRequest(Guid.NewGuid(), "key-1", "POST api/v2/things", Hash("body"));
        var store = provider.GetRequiredService<IIdempotencyStore>();

        (await store.BeginAsync(request, CancellationToken.None)).Status.ShouldBe(IdempotencyBeginStatus.Started);
        (await store.BeginAsync(request, CancellationToken.None)).Status.ShouldBe(IdempotencyBeginStatus.InProgress);
        (await store.BeginAsync(request with { RequestHash = Hash("other") }, CancellationToken.None)).Status.ShouldBe(IdempotencyBeginStatus.KeyReused);
        (await store.BeginAsync(request with { Operation = "POST api/v2/others" }, CancellationToken.None)).Status.ShouldBe(IdempotencyBeginStatus.KeyReused);
        (await store.BeginAsync(request with { UserId = Guid.NewGuid() }, CancellationToken.None)).Status.ShouldBe(IdempotencyBeginStatus.Started);

        await store.CompleteAsync(request, new StoredIdempotentResponse(201, "application/json", "{\"id\":1}", "/api/v2/things/1"), CancellationToken.None);
        var replay = await store.BeginAsync(request, CancellationToken.None);
        replay.Status.ShouldBe(IdempotencyBeginStatus.Replay);
        replay.Response.ShouldBe(new StoredIdempotentResponse(201, "application/json", "{\"id\":1}", "/api/v2/things/1"));

        var abandoned = request with { Key = "key-2" };
        (await store.BeginAsync(abandoned, CancellationToken.None)).Status.ShouldBe(IdempotencyBeginStatus.Started);
        await store.AbandonAsync(abandoned, CancellationToken.None);
        (await store.BeginAsync(abandoned, CancellationToken.None)).Status.ShouldBe(IdempotencyBeginStatus.Started);
    }

    [Fact]
    public async Task IdempotencyStore_ExpiredRecord_IsReplacedAfter24Hours()
    {
        var request = new IdempotencyRequest(Guid.NewGuid(), "key-expiring", "POST api/v2/things", Hash("body"));
        var store = provider.GetRequiredService<IIdempotencyStore>();
        await store.BeginAsync(request, CancellationToken.None);
        await store.CompleteAsync(request, new StoredIdempotentResponse(200, null, null, null), CancellationToken.None);

        clock.UtcNow = Now + IIdempotencyStore.Lifetime;

        (await store.BeginAsync(request with { RequestHash = Hash("new body") }, CancellationToken.None)).Status
            .ShouldBe(IdempotencyBeginStatus.Started);
    }

    public async ValueTask DisposeAsync() => await provider.DisposeAsync();

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow { get; set; }
    }
}
