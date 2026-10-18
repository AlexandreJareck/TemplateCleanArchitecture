using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Features.Access.Commands;
using Template.Application.Features.Access.Queries;
using Template.Application.Interfaces;
using Template.Application.Security;
using Template.Application.Services.Access;
using Template.Domain.Access;

namespace Template.Unit.Tests.ApplicationTests.Features.Access;

/// <summary>Handler behavior of SEC-001..006, SEC-009, SEC-010 and AUD-001/003 against port fakes.</summary>
public sealed class SessionHandlerTests
{
    private readonly AccessTestDoubles doubles = new();

    [Fact]
    public async Task Create_InvalidCredentials_FailsWithoutStateOrAudit()
    {
        var result = await CreateHandler().Handle(new CreateSessionCommand("synthetic-user", "wrong"), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.InvalidCredentials);
        doubles.Sessions.Families.ShouldBeEmpty();
        doubles.Audit.Records.ShouldBeEmpty();
        doubles.UnitOfWork.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Create_ValidCredentials_StartsFamily_AuditsAndIssuesTokens()
    {
        var result = await CreateHandler().Handle(new CreateSessionCommand("synthetic-user", "synthetic-password"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var family = doubles.Sessions.Families.Single();
        family.UserId.ShouldBe(doubles.Accounts.UserId);
        family.Tokens.Single().TokenHash.ShouldBe(AccessTestDoubles.FakeTokens.Hash(result.Value.RefreshToken));
        result.Value.SessionFamilyId.ShouldBe(family.Id);
        result.Value.CsrfToken.ShouldBe(doubles.Tokens.CreateCsrfToken(family.Id));
        result.Value.SessionExpiresAtUtc.ShouldBe(family.AbsoluteExpiresAtUtc);
        doubles.AccessTokens.IssuedFor.ShouldBe([family.Id]);
        doubles.UnitOfWork.Saves.ShouldBe(1);

        var audit = doubles.Audit.Records.Single();
        audit.Action.ShouldBe(SecurityAuditActions.SessionCreated);
        audit.ResourceId.ShouldBe(family.Id.ToString());
        audit.ActorId.ShouldBe(doubles.Accounts.UserId);
        audit.CorrelationId.ShouldBe("unit-test-trace");
        AssertNoSecrets(result.Value.RefreshToken, result.Value.AccessToken, result.Value.CsrfToken);
    }

    [Fact]
    public async Task Create_SixthFamily_RevokesTheOldest()
    {
        for (var index = 0; index < 5; index++)
            doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash($"old-{index}"), AccessTestDoubles.Start.AddMinutes(index - 10));
        var oldest = doubles.Sessions.Families[0];

        var result = await CreateHandler().Handle(new CreateSessionCommand("synthetic-user", "synthetic-password"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        oldest.RevocationReason.ShouldBe(SessionRevocationReason.FamilyLimitExceeded);
        doubles.Sessions.Families.Count(family => family.IsActive(doubles.Clock.UtcNow)).ShouldBe(5);
        doubles.Audit.Records.Select(record => record.Action)
            .ShouldBe([SecurityAuditActions.SessionRevoked, SecurityAuditActions.SessionCreated]);
        doubles.Audit.Records[0].Reason.ShouldBe("family-limit-exceeded");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-token")]
    public async Task Refresh_MissingOrUnknownToken_IsSessionExpired(string? token)
    {
        var result = await RefreshHandler().Handle(new RefreshSessionCommand(token, "any"), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.SessionExpired);
        doubles.Audit.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refresh_InvalidCsrf_IsForbidden_AuditedAndDoesNotRotate()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));

        var result = await RefreshHandler().Handle(new RefreshSessionCommand("current", "csrf-of-someone-else"), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.CsrfInvalid);
        family.Version.ShouldBe(1);
        doubles.Audit.Records.Single().Action.ShouldBe(SecurityAuditActions.CsrfRejected);
    }

    [Fact]
    public async Task Refresh_CurrentToken_RotatesAndAudits()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));

        var result = await RefreshHandler().Handle(new RefreshSessionCommand("current", Csrf(family)), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        family.Tokens.Single(token => token.IsCurrent).TokenHash.ShouldBe(AccessTestDoubles.FakeTokens.Hash(result.Value.RefreshToken));
        doubles.Audit.Records.Single().Action.ShouldBe(SecurityAuditActions.SessionRefreshed);
        doubles.UnitOfWork.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Refresh_ReplacedTokenAfterGrace_RevokesFamily_AndAuditsReuse()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("first"));
        (await RefreshHandler().Handle(new RefreshSessionCommand("first", Csrf(family)), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        doubles.Clock.UtcNow = doubles.Clock.UtcNow.AddSeconds(11);

        var result = await RefreshHandler().Handle(new RefreshSessionCommand("first", Csrf(family)), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.SessionRevoked);
        family.RevocationReason.ShouldBe(SessionRevocationReason.ReuseDetected);
        doubles.Audit.Records.Last().Action.ShouldBe(SecurityAuditActions.SessionReuseDetected);
    }

    [Fact]
    public async Task Refresh_PredecessorInsideGrace_IsRefreshInProgress()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("first"));
        (await RefreshHandler().Handle(new RefreshSessionCommand("first", Csrf(family)), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var result = await RefreshHandler().Handle(new RefreshSessionCommand("first", Csrf(family)), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.RefreshInProgress);
        family.IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_LosingTheRotationRace_IsRefreshInProgress_AndNeverIssuesATokenPair()
    {
        doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));
        doubles.UnitOfWork.ConflictsToThrow = 1;

        var result = await RefreshHandler().Handle(new RefreshSessionCommand("current", Csrf(doubles.Sessions.Families[0])), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.RefreshInProgress);
        doubles.UnitOfWork.Discards.ShouldBe(1);
        doubles.AccessTokens.IssuedFor.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refresh_AfterStampChange_RevokesFamily()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));
        doubles.Accounts.CurrentFingerprint = AccessTestDoubles.Sha('z');

        var result = await RefreshHandler().Handle(new RefreshSessionCommand("current", Csrf(family)), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.SessionRevoked);
        family.RevocationReason.ShouldBe(SessionRevocationReason.SecurityStampChanged);
        doubles.Audit.Records.Single().Reason.ShouldBe("security-stamp-changed");
    }

    [Fact]
    public async Task Refresh_DeletedUser_RevokesFamily()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));
        doubles.Accounts.CurrentFingerprint = null;

        (await RefreshHandler().Handle(new RefreshSessionCommand("current", Csrf(family)), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.SessionRevoked);
        family.IsRevoked.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_ExpiredFamily_IsSessionExpired()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));
        doubles.Clock.UtcNow = family.IdleExpiresAtUtc;

        (await RefreshHandler().Handle(new RefreshSessionCommand("current", Csrf(family)), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.SessionExpired);
    }

    [Fact]
    public async Task Logout_RevokesFamily_AndIsRepeatable()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));

        (await LogoutHandler().Handle(new RevokeCurrentSessionCommand("current", Csrf(family)), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await LogoutHandler().Handle(new RevokeCurrentSessionCommand("current", Csrf(family)), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await LogoutHandler().Handle(new RevokeCurrentSessionCommand(null, null), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        family.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        doubles.Audit.Records.Single().Action.ShouldBe(SecurityAuditActions.SessionRevoked);
    }

    [Fact]
    public async Task Logout_WithInvalidCsrf_IsForbidden()
    {
        var family = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("current"));

        (await LogoutHandler().Handle(new RevokeCurrentSessionCommand("current", null), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.CsrfInvalid);
        family.IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task RevokeAll_RevokesEveryActiveFamilyOfTheUserOnly()
    {
        var first = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("a"));
        var second = doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("b"));
        var other = doubles.AddFamily(Guid.NewGuid(), AccessTestDoubles.FakeTokens.Hash("c"));
        var handler = new RevokeAllSessionsCommandHandler(
            new CurrentUser(doubles.Accounts.UserId.ToString()), Revocation(), NullLogger<RevokeAllSessionsCommandHandler>.Instance);

        var result = await handler.Handle(new RevokeAllSessionsCommand(), CancellationToken.None);

        result.Value.ShouldBe(2);
        first.RevocationReason.ShouldBe(SessionRevocationReason.RevokeAll);
        second.IsRevoked.ShouldBeTrue();
        other.IsRevoked.ShouldBeFalse();
        doubles.Audit.Records.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RevokeAll_RetriesOnceAfterAConcurrencyConflict()
    {
        doubles.AddFamily(doubles.Accounts.UserId, AccessTestDoubles.FakeTokens.Hash("a"));
        doubles.UnitOfWork.ConflictsToThrow = 1;

        await Revocation().RevokeAllAsync(doubles.Accounts.UserId, SessionRevocationReason.PasswordChanged, CancellationToken.None);

        doubles.UnitOfWork.Discards.ShouldBe(1);
        doubles.UnitOfWork.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task RevokeAll_WithoutUser_IsNotAuthenticated()
    {
        var handler = new RevokeAllSessionsCommandHandler(new CurrentUser(null), Revocation(), NullLogger<RevokeAllSessionsCommandHandler>.Instance);

        (await handler.Handle(new RevokeAllSessionsCommand(), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.SessionExpired);
    }

    [Fact]
    public async Task CurrentUser_ReturnsApprovedFieldsSorted()
    {
        var permissions = new FakePermissions(["requisitions.create", "audit.read"]);
        var handler = new GetCurrentUserQueryHandler(new CurrentUser(doubles.Accounts.UserId.ToString()), doubles.Accounts, permissions);

        var result = await handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Value.ShouldBe(result.Value with { UserId = doubles.Accounts.UserId, UserName = "synthetic-user", DisplayName = "Synthetic" });
        result.Value.Roles.ShouldBe(["Auditor", "Buyer"]);
        result.Value.Permissions.ShouldBe(["audit.read", "requisitions.create"]);
        typeof(CurrentUserDto).GetProperties().Select(property => property.Name)
            .ShouldBe(["UserId", "UserName", "DisplayName", "Roles", "Permissions"], ignoreOrder: true);
    }

    [Fact]
    public async Task CurrentUser_UnknownOrMissingUser_IsNotAuthenticated()
    {
        var permissions = new FakePermissions([]);
        (await new GetCurrentUserQueryHandler(new CurrentUser(Guid.NewGuid().ToString()), doubles.Accounts, permissions)
            .Handle(new GetCurrentUserQuery(), CancellationToken.None)).IsSuccess.ShouldBeFalse();
        (await new GetCurrentUserQueryHandler(new CurrentUser(null), doubles.Accounts, permissions)
            .Handle(new GetCurrentUserQuery(), CancellationToken.None)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task RecordSecurityEvent_AcceptsOnlyBoundaryActions()
    {
        var handler = new RecordSecurityEventCommandHandler(new CurrentUser(null), doubles.Recorder, doubles.UnitOfWork);

        (await handler.Handle(new RecordSecurityEventCommand(SecurityAuditActions.RateLimited, "POST /api/v2/auth/sessions",
            new Dictionary<string, string> { ["policy"] = "Login" }), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        await Should.ThrowAsync<ArgumentException>(() => handler.Handle(
            new RecordSecurityEventCommand(SecurityAuditActions.SessionCreated, "endpoint"), CancellationToken.None));

        doubles.Audit.Records.Single().ActorId.ShouldBeNull();
        doubles.Audit.Records.Single().ResourceType.ShouldBe(SecurityAuditActions.EndpointResource);
    }

    [Fact]
    public void Commands_DoNotPrintCredentials()
    {
        new CreateSessionCommand("synthetic-user", "synthetic-password").ToString().ShouldNotContain("synthetic-password");
        new RefreshSessionCommand("refresh-value", "csrf-value").ToString().ShouldNotContain("refresh-value");
        new RevokeCurrentSessionCommand("refresh-value", "csrf-value").ToString().ShouldNotContain("csrf-value");
    }

    private void AssertNoSecrets(params string[] secrets)
    {
        foreach (var record in doubles.Audit.Records)
        {
            var text = string.Join("|", record.Action, record.ResourceId, record.Reason, record.CorrelationId,
                string.Join(",", record.Details.Select(pair => $"{pair.Key}={pair.Value}")));
            foreach (var secret in secrets)
                text.Contains(secret, StringComparison.Ordinal).ShouldBeFalse();
        }
    }

    private string Csrf(SessionFamily family) => doubles.Tokens.CreateCsrfToken(family.Id);

    private CreateSessionCommandHandler CreateHandler() => new(
        doubles.Accounts, doubles.Tokens, doubles.Sessions, doubles.AccessTokens, doubles.UnitOfWork, doubles.Recorder,
        doubles.Clock, doubles.Options, NullLogger<CreateSessionCommandHandler>.Instance);

    private RefreshSessionCommandHandler RefreshHandler() => new(
        doubles.Accounts, doubles.Tokens, doubles.Sessions, doubles.AccessTokens, doubles.UnitOfWork, doubles.Recorder,
        doubles.Clock, doubles.Options, NullLogger<RefreshSessionCommandHandler>.Instance);

    private RevokeCurrentSessionCommandHandler LogoutHandler() => new(
        doubles.Tokens, doubles.Sessions, doubles.UnitOfWork, doubles.Recorder, doubles.Clock,
        NullLogger<RevokeCurrentSessionCommandHandler>.Instance);

    private SessionRevocationService Revocation() => new(doubles.Sessions, doubles.UnitOfWork, doubles.Recorder, doubles.Clock);

    private sealed record CurrentUser(string? UserId) : IAuthenticatedUserService
    {
        public string? UserName => null;
    }

    private sealed class FakePermissions(IEnumerable<string> permissions) : Template.Application.Interfaces.Access.IPermissionReader
    {
        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(permissions.ToHashSet());
    }
}
