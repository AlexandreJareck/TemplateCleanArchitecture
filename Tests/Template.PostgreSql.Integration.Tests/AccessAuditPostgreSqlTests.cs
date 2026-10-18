using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Access.Commands;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Auditing;
using Template.Application.Interfaces.Idempotency;
using Template.Application.Security;
using Template.Application.Services.Access;
using Template.Application.Settings;
using Template.Domain.Access;
using Template.Domain.Auditing;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 24 persistence on real PostgreSQL 16.15: hash-only refresh storage (SEC-002-a), reuse revocation with audit
/// (SEC-003-a), concurrent rotation (SEC-004-a, SP-1 version token), family limit (SEC-010-a), atomic audit (AUD-001-a)
/// and the idempotency unique key under concurrency. All values are synthetic.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class AccessAuditPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Start = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly MutableClock clock = new() { UtcNow = Start };
    private readonly Accounts accounts = new();
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
        services.AddSingleton<IRequestContext>(new TraceContext());
        services.AddSingleton<IAccountDirectory>(accounts);
        services.AddSingleton<ISessionTokenService, Tokens>();
        services.AddSingleton<IAccessTokenIssuer, AccessTokens>();
        services.AddSingleton<IOptions<SessionSettings>>(Options.Create(new SessionSettings()));
        services.AddScoped<SecurityAuditRecorder>();
        services.AddScoped<ISessionRevocationService, SessionRevocationService>();
        services.AddScoped<CreateSessionCommandHandler>();
        services.AddScoped<RefreshSessionCommandHandler>();
        provider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task Migration_CreatesAccessAuditAndIdempotencyTables_WithExpectedTypes()
    {
        await using var context = fixture.CreateContext();

        (await context.Database.GetAppliedMigrationsAsync()).ShouldContain(migration => migration.EndsWith("Phase24AccessAuditIdempotency", StringComparison.Ordinal));
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await ColumnType(context, "AuditRecords", "RuleIds")).ShouldBe("ARRAY");
        (await ColumnType(context, "AuditRecords", "Details")).ShouldBe("jsonb");
        (await ColumnType(context, "AccessSessionFamilies", "Version")).ShouldBe("bigint");
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, """
            SELECT count(*) FROM pg_indexes
            WHERE indexname IN ('IX_AccessRefreshTokens_TokenHash', 'IX_ApiIdempotencyRecords_UserId_Key')
              AND indexdef LIKE 'CREATE UNIQUE INDEX%'
            """)).ShouldBe(2);
    }

    [Fact]
    public async Task Login_StoresOnlyTheHash_AndAuditsInTheSameCommit()
    {
        var issued = await LoginAsync();

        await using var context = fixture.CreateContext();
        var stored = await context.Set<RefreshTokenRecord>().SingleAsync(token => token.FamilyId == issued.SessionFamilyId);
        stored.TokenHash.ShouldBe(Tokens.Hash(issued.RefreshToken));
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"""
            SELECT
              (SELECT count(*) FROM "AccessRefreshTokens" WHERE "TokenHash" = '{issued.RefreshToken}') +
              (SELECT count(*) FROM "AccessSessionFamilies" WHERE "SecurityStampFingerprint" = '{issued.RefreshToken}') +
              (SELECT count(*) FROM "AuditRecords" WHERE "Details"::text LIKE '%{issued.RefreshToken}%' OR "ResourceId" = '{issued.RefreshToken}')
            """)).ShouldBe(0);
        (await context.Set<AuditRecord>().CountAsync(record =>
            record.ResourceId == issued.SessionFamilyId.ToString() && record.Action == SecurityAuditActions.SessionCreated)).ShouldBe(1);
    }

    [Fact]
    public async Task ParallelRefresh_OfTheSameToken_HasExactlyOneWinner_AndNoRevocation()
    {
        var issued = await LoginAsync();
        var csrf = Tokens.Csrf(issued.SessionFamilyId);
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await scope.ServiceProvider.GetRequiredService<RefreshSessionCommandHandler>()
                .Handle(new RefreshSessionCommand(issued.RefreshToken, csrf), CancellationToken.None);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Single(result => !result.IsSuccess).Error!.Code.ShouldBe(ProblemCodes.RefreshInProgress);
        await using var context = fixture.CreateContext();
        var family = await context.Set<SessionFamily>().Include(candidate => candidate.Tokens).SingleAsync(candidate => candidate.Id == issued.SessionFamilyId);
        family.IsRevoked.ShouldBeFalse();
        family.Version.ShouldBe(2);
        family.Tokens.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReuseAfterGrace_RevokesFamily_AndRecordsReuseAudit()
    {
        var issued = await LoginAsync();
        var csrf = Tokens.Csrf(issued.SessionFamilyId);
        (await RefreshAsync(issued.RefreshToken, csrf)).IsSuccess.ShouldBeTrue();
        clock.UtcNow = clock.UtcNow.AddSeconds(11);

        (await RefreshAsync(issued.RefreshToken, csrf)).Error!.Code.ShouldBe(ProblemCodes.SessionRevoked);

        await using var context = fixture.CreateContext();
        var family = await context.Set<SessionFamily>().SingleAsync(candidate => candidate.Id == issued.SessionFamilyId);
        family.RevocationReason.ShouldBe(SessionRevocationReason.ReuseDetected);
        (await context.Set<AuditRecord>().CountAsync(record =>
            record.ResourceId == issued.SessionFamilyId.ToString() && record.Action == SecurityAuditActions.SessionReuseDetected)).ShouldBe(1);
    }

    [Fact]
    public async Task StaleVersion_IsAConcurrencyConflict_AndWritesNothing()
    {
        var issued = await LoginAsync();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var hash = Tokens.Hash(issued.RefreshToken);
        var first = await firstScope.ServiceProvider.GetRequiredService<ISessionFamilyRepository>().FindByTokenHashAsync(hash, CancellationToken.None);
        var second = await secondScope.ServiceProvider.GetRequiredService<ISessionFamilyRepository>().FindByTokenHashAsync(hash, CancellationToken.None);

        first!.Revoke(SessionRevocationReason.Logout, clock.UtcNow);
        await firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        second!.Revoke(SessionRevocationReason.RevokeAll, clock.UtcNow);
        secondScope.ServiceProvider.GetRequiredService<IAuditTrail>().Append(SyntheticAudit(issued.SessionFamilyId));

        await Should.ThrowAsync<ConcurrencyConflictException>(() => secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
        await using var context = fixture.CreateContext();
        (await context.Set<SessionFamily>().SingleAsync(candidate => candidate.Id == issued.SessionFamilyId)).RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        (await context.Set<AuditRecord>().CountAsync(record => record.Action == "synthetic.audit" && record.ResourceId == issued.SessionFamilyId.ToString())).ShouldBe(0);
    }

    [Fact]
    public async Task ConcurrentRotation_SecondCommit_IsAConcurrencyConflict_AndKeepsOneSuccessor()
    {
        var issued = await LoginAsync();
        var hash = Tokens.Hash(issued.RefreshToken);
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var first = await firstScope.ServiceProvider.GetRequiredService<ISessionFamilyRepository>().FindByTokenHashAsync(hash, CancellationToken.None);
        var second = await secondScope.ServiceProvider.GetRequiredService<ISessionFamilyRepository>().FindByTokenHashAsync(hash, CancellationToken.None);

        first!.Refresh(hash, Tokens.Hash("successor-a"), Tokens.Hash("stamp"), clock.UtcNow, SessionPolicy.Version1).ShouldBe(SessionRefreshOutcome.Rotated);
        second!.Refresh(hash, Tokens.Hash("successor-b"), Tokens.Hash("stamp"), clock.UtcNow, SessionPolicy.Version1).ShouldBe(SessionRefreshOutcome.Rotated);
        await firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        // The (family, sequence) unique index or the version token rejects the loser; both map to the same exception.
        await Should.ThrowAsync<ConcurrencyConflictException>(() => secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
        await using var context = fixture.CreateContext();
        var successors = await context.Set<RefreshTokenRecord>().Where(token => token.FamilyId == issued.SessionFamilyId && token.Sequence == 2).ToListAsync();
        successors.Single().TokenHash.ShouldBe(Tokens.Hash("successor-a"));
    }

    [Fact]
    public async Task FailedAuditInsert_RollsBackTheTransition()
    {
        var issued = await LoginAsync();
        var duplicateId = Guid.NewGuid();
        await using (var seed = provider.CreateAsyncScope())
        {
            seed.ServiceProvider.GetRequiredService<IAuditTrail>().Append(SyntheticAudit(issued.SessionFamilyId, duplicateId));
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var family = await scope.ServiceProvider.GetRequiredService<ISessionFamilyRepository>()
                .FindByTokenHashAsync(Tokens.Hash(issued.RefreshToken), CancellationToken.None);
            family!.Revoke(SessionRevocationReason.Logout, clock.UtcNow);
            scope.ServiceProvider.GetRequiredService<IAuditTrail>().Append(SyntheticAudit(issued.SessionFamilyId, duplicateId));
            await Should.ThrowAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
        }

        await using var context = fixture.CreateContext();
        (await context.Set<SessionFamily>().SingleAsync(candidate => candidate.Id == issued.SessionFamilyId)).IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task SixthLogin_RevokesTheOldestFamily()
    {
        accounts.UserId = Guid.NewGuid();
        var sessions = new List<Guid>();
        for (var index = 0; index < 6; index++)
        {
            clock.UtcNow = Start.AddMinutes(index);
            sessions.Add((await LoginAsync()).SessionFamilyId);
        }

        await using var context = fixture.CreateContext();
        var families = await context.Set<SessionFamily>().Where(family => family.UserId == accounts.UserId).ToListAsync();
        families.Count(family => family.IsActive(clock.UtcNow)).ShouldBe(5);
        families.Single(family => family.Id == sessions[0]).RevocationReason.ShouldBe(SessionRevocationReason.FamilyLimitExceeded);
    }

    [Fact]
    public async Task IdempotencyStore_ConcurrentFirstRequests_HaveOneOwner()
    {
        var request = new IdempotencyRequest(Guid.NewGuid(), "pg-key", "POST api/v2/things", Tokens.Hash("body"));
        var store = provider.GetRequiredService<IIdempotencyStore>();
        using var barrier = new Barrier(4);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return (await store.BeginAsync(request, CancellationToken.None)).Status;
        })));

        outcomes.Count(status => status == IdempotencyBeginStatus.Started).ShouldBe(1);
        outcomes.Count(status => status == IdempotencyBeginStatus.InProgress).ShouldBe(3);
    }

    private async Task<Template.Application.Features.Access.SessionIssued> LoginAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<CreateSessionCommandHandler>()
            .Handle(new CreateSessionCommand("synthetic-user", "synthetic-password"), CancellationToken.None);
        return result.Value;
    }

    private async Task<Result<Template.Application.Features.Access.SessionIssued>> RefreshAsync(string token, string csrf)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshSessionCommandHandler>()
            .Handle(new RefreshSessionCommand(token, csrf), CancellationToken.None);
    }

    private AuditRecord SyntheticAudit(Guid familyId, Guid? id = null) =>
        AuditRecord.Create(id ?? Guid.NewGuid(), clock.UtcNow, null, "synthetic.audit", "session-family", familyId.ToString(), "pg-trace");

    private static Task<string> ColumnType(ApplicationDbContext context, string table, string column) =>
        PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            $"SELECT data_type FROM information_schema.columns WHERE table_name = '{table}' AND column_name = '{column}'");

    private sealed class MutableClock : IClock
    {
        public DateTime UtcNow { get; set; }
    }

    private sealed class TraceContext : IRequestContext
    {
        public string CorrelationId => "pg-trace";
    }

    private sealed class Accounts : IAccountDirectory
    {
        private static readonly string Fingerprint = Tokens.Hash("stamp");
        public Guid UserId { get; set; } = Guid.NewGuid();

        public Task<VerifiedAccount?> VerifyCredentialsAsync(string userName, string password, CancellationToken cancellationToken) =>
            Task.FromResult<VerifiedAccount?>(new VerifiedAccount(UserId, Fingerprint));

        public Task<string?> GetSecurityStampFingerprintAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(Fingerprint);

        public Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<AccountProfile?>(null);

        public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(userId == UserId);

        public Task<Template.Application.Common.Paging.PagedResult<UserSummary>> SearchUsersAsync(
            string? search, Template.Application.Common.Paging.PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new Template.Application.Common.Paging.PagedResult<UserSummary>([], page.Page, page.PageSize, 0));
    }

    private sealed class Tokens : ISessionTokenService
    {
        public GeneratedRefreshToken NewRefreshToken()
        {
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            return new GeneratedRefreshToken(token, Hash(token));
        }

        public string? HashRefreshToken(string? token) => string.IsNullOrEmpty(token) ? null : Hash(token);
        public string CreateCsrfToken(Guid sessionFamilyId) => Csrf(sessionFamilyId);
        public bool IsValidCsrfToken(Guid sessionFamilyId, string? csrfToken) => csrfToken == Csrf(sessionFamilyId);

        public static string Csrf(Guid familyId) => $"csrf-{familyId:N}";
        public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed class AccessTokens : IAccessTokenIssuer
    {
        public Task<IssuedAccessToken> IssueAsync(Guid userId, Guid sessionFamilyId, CancellationToken cancellationToken) =>
            Task.FromResult(new IssuedAccessToken("synthetic-access-token", Start.AddMinutes(15)));
    }
}
