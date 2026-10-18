using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Template.Application.Common.Concurrency;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Auditing;
using Template.Application.Services.Access;
using Template.Application.Settings;
using Template.Domain.Access;
using Template.Domain.Auditing;

namespace Template.Unit.Tests.ApplicationTests.Features.Access;

/// <summary>Port fakes for the session use cases. Token values are synthetic and never asserted in messages.</summary>
internal sealed class AccessTestDoubles
{
    public static readonly DateTime Start = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    public MutableClock Clock { get; } = new(Start);
    public FakeAccounts Accounts { get; } = new();
    public FakeTokens Tokens { get; } = new();
    public FakeSessions Sessions { get; } = new();
    public FakeAccessTokens AccessTokens { get; } = new();
    public FakeAuditTrail Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public SessionSettings Settings { get; } = new();
    public IOptions<SessionSettings> Options => Microsoft.Extensions.Options.Options.Create(Settings);

    public SecurityAuditRecorder Recorder => new(Audit, Clock, new FixedRequestContext());

    public SessionFamily AddFamily(Guid userId, string tokenHash, DateTime? startedAt = null, string? stamp = null)
    {
        var family = SessionFamily.Start(Guid.NewGuid(), userId, stamp ?? FakeAccounts.Fingerprint, tokenHash, startedAt ?? Clock.UtcNow, Settings.ToPolicy());
        Sessions.Families.Add(family);
        return family;
    }

    internal sealed class MutableClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }

    internal sealed class FixedRequestContext : IRequestContext
    {
        public string CorrelationId => "unit-test-trace";
    }

    internal sealed class FakeAccounts : IAccountDirectory
    {
        public static readonly string Fingerprint = Sha('s');
        public Guid UserId { get; } = Guid.NewGuid();
        public string Password { get; set; } = "synthetic-password";
        public string? CurrentFingerprint { get; set; } = Fingerprint;

        public Task<VerifiedAccount?> VerifyCredentialsAsync(string userName, string password, CancellationToken cancellationToken) =>
            Task.FromResult(userName == "synthetic-user" && password == Password ? new VerifiedAccount(UserId, Fingerprint) : null);

        public Task<string?> GetSecurityStampFingerprintAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(CurrentFingerprint);

        public Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == UserId ? new AccountProfile(UserId, "synthetic-user", "Synthetic", ["Buyer", "Auditor"]) : null);
    }

    internal sealed class FakeTokens : ISessionTokenService
    {
        private int counter;

        public GeneratedRefreshToken NewRefreshToken()
        {
            var token = $"refresh-{Interlocked.Increment(ref counter)}";
            return new GeneratedRefreshToken(token, Hash(token));
        }

        public string? HashRefreshToken(string? token) => string.IsNullOrEmpty(token) ? null : Hash(token);

        public string CreateCsrfToken(Guid sessionFamilyId) => $"csrf-{sessionFamilyId:N}";

        public bool IsValidCsrfToken(Guid sessionFamilyId, string? csrfToken) => csrfToken == CreateCsrfToken(sessionFamilyId);

        public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    internal sealed class FakeSessions : ISessionFamilyRepository
    {
        public List<SessionFamily> Families { get; } = [];

        public void Add(SessionFamily family) => Families.Add(family);

        public Task<SessionFamily?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Families.SingleOrDefault(family => family.Contains(tokenHash)));

        public Task<IReadOnlyList<SessionFamily>> ListActiveAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionFamily>>(Families.Where(family => family.UserId == userId && family.IsActive(nowUtc))
                .OrderBy(family => family.CreatedAtUtc).ToList());

        public Task<bool> IsActiveAsync(Guid sessionFamilyId, Guid userId, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Families.Any(family => family.Id == sessionFamilyId && family.UserId == userId && family.IsActive(nowUtc)));
    }

    internal sealed class FakeAccessTokens : IAccessTokenIssuer
    {
        public List<Guid> IssuedFor { get; } = [];

        public Task<IssuedAccessToken> IssueAsync(Guid userId, Guid sessionFamilyId, CancellationToken cancellationToken)
        {
            IssuedFor.Add(sessionFamilyId);
            return Task.FromResult(new IssuedAccessToken($"access-{IssuedFor.Count}", Start.AddMinutes(15)));
        }
    }

    internal sealed class FakeAuditTrail : IAuditTrail
    {
        public List<AuditRecord> Records { get; } = [];

        public void Append(AuditRecord record) => Records.Add(record);
    }

    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }
        public int Discards { get; private set; }
        public int ConflictsToThrow { get; set; }

        public Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ConflictsToThrow > 0)
            {
                ConflictsToThrow--;
                throw new ConcurrencyConflictException("synthetic conflict");
            }

            Saves++;
            return Task.FromResult(true);
        }

        public void DiscardChanges() => Discards++;

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal static string Sha(char seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString())));
}
