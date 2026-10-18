using System.Reflection;
using Shouldly;
using Template.Domain.Auditing;

namespace Template.Unit.Tests.DomainTests.Auditing;

/// <summary>AUD-002 (append-only shape) and AUD-003 (allow-listed details) of <see cref="AuditRecord"/>.</summary>
public sealed class AuditRecordTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_KeepsAllowListedDetailsAndNormalizesEmptyActor()
    {
        var record = AuditRecord.Create(
            Guid.NewGuid(), Now, Guid.Empty, "security.session.revoked", "session-family", Guid.NewGuid().ToString(), "trace-1",
            toState: "Revoked", ruleIds: ["SEC-006"], reason: "logout",
            details: new Dictionary<string, string> { ["reason"] = "logout", ["revokedCount"] = "2" });

        record.ActorId.ShouldBeNull();
        record.Details.ShouldBe(new Dictionary<string, string> { ["reason"] = "logout", ["revokedCount"] = "2" });
        record.RuleIds.ShouldBe(["SEC-006"]);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("refreshToken")]
    [InlineData("cookie")]
    [InlineData("tokenHash")]
    [InlineData("address")]
    public void Create_RejectsDetailKeysOutsideTheAllowList(string key) =>
        Should.Throw<ArgumentException>(() => AuditRecord.Create(
            Guid.NewGuid(), Now, null, "security.session.created", "session-family", "id", "trace",
            details: new Dictionary<string, string> { [key] = "synthetic" }));

    [Fact]
    public void Create_RejectsMissingOrOversizedValuesAndLocalTime()
    {
        Should.Throw<ArgumentException>(() => AuditRecord.Create(Guid.NewGuid(), Now, null, "", "type", "id", "trace"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(Guid.NewGuid(), Now, null, new string('a', 101), "type", "id", "trace"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(Guid.Empty, Now, null, "action", "type", "id", "trace"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(Guid.NewGuid(), DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), null, "action", "type", "id", "trace"));
        Should.Throw<ArgumentException>(() => AuditRecord.Create(Guid.NewGuid(), Now, null, "action", "type", "id", "trace",
            details: new Dictionary<string, string> { ["reason"] = new string('x', AuditRecord.DetailValueMaxLength + 1) }));
    }

    [Fact]
    public void AuditRecord_HasNoPublicMutator()
    {
        var type = typeof(AuditRecord);

        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true)
            .ShouldBeEmpty();
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ShouldBeEmpty();
    }
}
