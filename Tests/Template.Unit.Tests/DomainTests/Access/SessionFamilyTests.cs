using Shouldly;
using Template.Domain.Access;

namespace Template.Unit.Tests.DomainTests.Access;

/// <summary>SEC-002/003/004/006 rules of <see cref="SessionFamily"/>, without mocks.</summary>
public sealed class SessionFamilyTests
{
    private static readonly DateTime Start = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SessionPolicy Policy = SessionPolicy.Version1;
    private static readonly string Stamp = Hash('s');

    [Fact]
    public void Start_CreatesActiveFamily_WithOneCurrentTokenAndVersion1()
    {
        var family = NewFamily(out var tokenHash);

        family.IsActive(Start).ShouldBeTrue();
        family.Version.ShouldBe(1);
        family.Tokens.Single().TokenHash.ShouldBe(tokenHash);
        family.Tokens.Single().Sequence.ShouldBe(1);
        family.AbsoluteExpiresAtUtc.ShouldBe(Start + Policy.AbsoluteLifetime);
        family.IdleExpiresAtUtc.ShouldBe(Start + Policy.IdleTimeout);
        family.CookieExpiresAtUtc.ShouldBe(family.AbsoluteExpiresAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")] // uppercase is not the stored form
    public void Start_RejectsValuesThatAreNotLowercaseSha256(string tokenHash) =>
        Should.Throw<ArgumentException>(() => SessionFamily.Start(Guid.NewGuid(), Guid.NewGuid(), Stamp, tokenHash, Start, Policy));

    [Fact]
    public void Start_RejectsNonUtcInstantsAndEmptyIdentifiers()
    {
        Should.Throw<ArgumentException>(() => SessionFamily.Start(Guid.NewGuid(), Guid.NewGuid(), Stamp, Hash('a'), DateTime.SpecifyKind(Start, DateTimeKind.Local), Policy));
        Should.Throw<ArgumentException>(() => SessionFamily.Start(Guid.Empty, Guid.NewGuid(), Stamp, Hash('a'), Start, Policy));
        Should.Throw<ArgumentException>(() => SessionFamily.Start(Guid.NewGuid(), Guid.Empty, Stamp, Hash('a'), Start, Policy));
    }

    [Fact]
    public void Refresh_CurrentToken_RotatesAndSlidesIdleExpiry()
    {
        var family = NewFamily(out var first);
        var now = Start.AddHours(1);

        family.Refresh(first, Hash('b'), Stamp, now, Policy).ShouldBe(SessionRefreshOutcome.Rotated);

        family.Version.ShouldBe(2);
        family.Tokens.Single(token => token.IsCurrent).TokenHash.ShouldBe(Hash('b'));
        family.Tokens.Single(token => token.TokenHash == first).ReplacedAtUtc.ShouldBe(now);
        family.Tokens.Single(token => token.IsCurrent).Sequence.ShouldBe(2);
        family.IdleExpiresAtUtc.ShouldBe(now + Policy.IdleTimeout);
        family.LastRefreshedAtUtc.ShouldBe(now);
    }

    [Fact]
    public void Refresh_IdleExpiryNeverPassesTheAbsoluteLimit()
    {
        var policy = new SessionPolicy(TimeSpan.FromHours(2), TimeSpan.FromMinutes(90), TimeSpan.FromSeconds(10), 5);
        var family = SessionFamily.Start(Guid.NewGuid(), Guid.NewGuid(), Stamp, Hash('a'), Start, policy);

        family.Refresh(Hash('a'), Hash('b'), Stamp, Start.AddHours(1), policy).ShouldBe(SessionRefreshOutcome.Rotated);

        family.IdleExpiresAtUtc.ShouldBe(family.AbsoluteExpiresAtUtc);
    }

    [Fact]
    public void Refresh_AfterIdleTimeout_IsExpiredWithoutChange()
    {
        var family = NewFamily(out var first);

        family.Refresh(first, Hash('b'), Stamp, Start + Policy.IdleTimeout, Policy).ShouldBe(SessionRefreshOutcome.Expired);

        family.Version.ShouldBe(1);
        family.IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public void Refresh_AfterAbsoluteLifetime_IsExpired()
    {
        var policy = new SessionPolicy(TimeSpan.FromHours(2), TimeSpan.FromHours(1), TimeSpan.FromSeconds(10), 5);
        var family = SessionFamily.Start(Guid.NewGuid(), Guid.NewGuid(), Stamp, Hash('a'), Start, policy);
        family.Refresh(Hash('a'), Hash('b'), Stamp, Start.AddMinutes(50), policy).ShouldBe(SessionRefreshOutcome.Rotated);
        family.Refresh(Hash('b'), Hash('c'), Stamp, Start.AddMinutes(100), policy).ShouldBe(SessionRefreshOutcome.Rotated);

        family.Refresh(Hash('c'), Hash('d'), Stamp, Start.AddHours(2), policy).ShouldBe(SessionRefreshOutcome.Expired);
    }

    [Fact]
    public void Refresh_ImmediatePredecessorInsideGrace_IsInProgressWithoutRevoking()
    {
        var family = NewFamily(out var first);
        family.Refresh(first, Hash('b'), Stamp, Start.AddMinutes(1), Policy);

        family.Refresh(first, Hash('c'), Stamp, Start.AddMinutes(1) + Policy.ReuseGraceWindow, Policy)
            .ShouldBe(SessionRefreshOutcome.RefreshInProgress);

        family.IsRevoked.ShouldBeFalse();
        family.Version.ShouldBe(2);
    }

    [Fact]
    public void Refresh_ReplacedTokenAfterGrace_RevokesTheWholeFamily()
    {
        var family = NewFamily(out var first);
        family.Refresh(first, Hash('b'), Stamp, Start.AddMinutes(1), Policy);

        family.Refresh(first, Hash('c'), Stamp, Start.AddMinutes(1) + Policy.ReuseGraceWindow + TimeSpan.FromTicks(1), Policy)
            .ShouldBe(SessionRefreshOutcome.ReuseDetected);

        family.IsRevoked.ShouldBeTrue();
        family.RevocationReason.ShouldBe(SessionRevocationReason.ReuseDetected);
        family.Refresh(Hash('b'), Hash('d'), Stamp, Start.AddMinutes(2), Policy).ShouldBe(SessionRefreshOutcome.Revoked);
    }

    [Fact]
    public void Refresh_OlderThanPredecessor_IsReuseEvenInsideGrace()
    {
        var family = NewFamily(out var first);
        family.Refresh(first, Hash('b'), Stamp, Start.AddSeconds(1), Policy);
        family.Refresh(Hash('b'), Hash('c'), Stamp, Start.AddSeconds(2), Policy);

        family.Refresh(first, Hash('d'), Stamp, Start.AddSeconds(3), Policy).ShouldBe(SessionRefreshOutcome.ReuseDetected);
    }

    [Fact]
    public void Refresh_StampChanged_RevokesFamily()
    {
        var family = NewFamily(out var first);

        family.Refresh(first, Hash('b'), Hash('z'), Start.AddMinutes(1), Policy).ShouldBe(SessionRefreshOutcome.SecurityStampChanged);

        family.RevocationReason.ShouldBe(SessionRevocationReason.SecurityStampChanged);
        family.Tokens.Count.ShouldBe(1);
    }

    [Fact]
    public void Refresh_TokenOfAnotherFamily_IsAProgrammingError()
    {
        var family = NewFamily(out _);

        Should.Throw<ArgumentException>(() => family.Refresh(Hash('x'), Hash('b'), Stamp, Start, Policy));
    }

    [Fact]
    public void Revoke_IsIdempotentAndKeepsTheFirstReason()
    {
        var family = NewFamily(out _);

        family.Revoke(SessionRevocationReason.Logout, Start.AddMinutes(1)).ShouldBeTrue();
        family.Revoke(SessionRevocationReason.RevokeAll, Start.AddMinutes(2)).ShouldBeFalse();

        family.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        family.RevokedAtUtc.ShouldBe(Start.AddMinutes(1));
        family.Version.ShouldBe(2);
        family.IsActive(Start.AddMinutes(3)).ShouldBeFalse();
    }

    [Fact]
    public void Policy_RejectsInconsistentLimits()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new SessionPolicy(TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.Zero, 5));
        Should.Throw<ArgumentOutOfRangeException>(() => new SessionPolicy(TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.Zero, 5));
        Should.Throw<ArgumentOutOfRangeException>(() => new SessionPolicy(TimeSpan.FromHours(2), TimeSpan.FromHours(1), TimeSpan.FromHours(1), 5));
        Should.Throw<ArgumentOutOfRangeException>(() => new SessionPolicy(TimeSpan.FromHours(2), TimeSpan.FromHours(1), TimeSpan.Zero, 0));
        SessionPolicy.Version1.MaxActiveFamilies.ShouldBe(5);
        SessionPolicy.Version1.ReuseGraceWindow.ShouldBe(TimeSpan.FromSeconds(10));
    }

    private static SessionFamily NewFamily(out string tokenHash)
    {
        tokenHash = Hash('a');
        return SessionFamily.Start(Guid.NewGuid(), Guid.NewGuid(), Stamp, tokenHash, Start, Policy);
    }

    internal static string Hash(char seed, long salt = 0) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{seed}{salt}")));
}
