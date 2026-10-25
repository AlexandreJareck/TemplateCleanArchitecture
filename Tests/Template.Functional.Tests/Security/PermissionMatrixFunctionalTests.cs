using System.Net;
using Shouldly;
using Template.Application.Security;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.Security;

/// <summary>
/// Data-driven permission matrix for the Phase 24 endpoints (docs/product/permissions.md §5, SEC-008): every seeded
/// role either passes or gets 403 according to the matrix; the decision is made by permission, never by role name.
/// </summary>
public sealed class PermissionMatrixFunctionalTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    public static TheoryData<string> Roles => new(SeededRoles.All);

    /// <summary>AUD-004: the global audit query requires <c>audit.read</c>, per the seeded permission matrix.</summary>
    [Theory]
    [MemberData(nameof(Roles))]
    public async Task AuditRecords_FollowsTheAuditReadColumn(string role)
    {
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, role));

        using var response = await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.AuditRoute);

        var allowed = SeededRoles.PermissionsByRole[role].Contains(Permissions.AuditRead);
        response.StatusCode.ShouldBe(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        if (!allowed)
            (await SessionClient.ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("auth.forbidden");
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Me_IsAvailableToEveryAuthenticatedRole(string role)
    {
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, role));

        using var response = await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SessionClient.ReadJsonAsync(response)).GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString())
            .ShouldBe(SeededRoles.PermissionsByRole[role]);
    }

    [Fact]
    public async Task AuditRecords_Anonymous_Is401_AndUserWithoutRoles_Is403()
    {
        var session = SessionClient.Create(factory);
        using var anonymous = await session.Client.GetAsync(SessionClient.AuditRoute);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services));
        using var noRoles = await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.AuditRoute);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SessionClient.ReadJsonAsync(anonymous)).GetProperty("code").GetString().ShouldBe("auth.session-expired");
        noRoles.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegacyAdminRole_GrantsNoPermission()
    {
        // The pre-Phase 24 "Admin" role is a role name only; permissions come from role claims (B23-9).
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, "Admin"));

        (await session.SendWithBearerAsync(HttpMethod.Get, SessionClient.AuditRoute)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Forbidden_IsAuditedWithThePermission_AndSampled()
    {
        var denied = SessionClient.Create(factory);
        await denied.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, SeededRoles.Buyer));
        (await denied.SendWithBearerAsync(HttpMethod.Get, SessionClient.AuditRoute)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.SendWithBearerAsync(HttpMethod.Get, SessionClient.AuditRoute)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var me = await denied.SendWithBearerAsync(HttpMethod.Get, SessionClient.MeRoute);
        var deniedUserId = (await SessionClient.ReadJsonAsync(me)).GetProperty("userId").GetString();

        var auditor = SessionClient.Create(factory);
        await auditor.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, SeededRoles.Auditor));
        using var response = await auditor.SendWithBearerAsync(
            HttpMethod.Get, $"{SessionClient.AuditRoute}?action={SecurityAuditActions.Forbidden}&actorId={deniedUserId}");

        var items = (await SessionClient.ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToArray();
        items.Length.ShouldBe(1);
        items[0].GetProperty("details").GetProperty("permission").GetString().ShouldBe(Permissions.AuditRead);
        items[0].GetProperty("resourceId").GetString().ShouldBe("GET /api/v{version:apiVersion}/audit-records");
    }

    [Theory]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("sort=password", "sort")]
    [InlineData("from=2026-10-18T10:00:00Z&to=2026-10-18T09:00:00Z", "from")]
    public async Task AuditRecords_InvalidQuery_Is400WithValidationCode(string query, string field)
    {
        var auditor = SessionClient.Create(factory);
        await auditor.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, SeededRoles.Auditor));

        using var response = await auditor.SendWithBearerAsync(HttpMethod.Get, $"{SessionClient.AuditRoute}?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await SessionClient.ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation");
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task AuditRecords_ReturnsThePagedEnvelope()
    {
        var auditor = SessionClient.Create(factory);
        await auditor.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, SeededRoles.Auditor));

        using var response = await auditor.SendWithBearerAsync(HttpMethod.Get, $"{SessionClient.AuditRoute}?pageSize=1&sort=-occurredAt,action");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await SessionClient.ReadJsonAsync(response);
        page.EnumerateObject().Select(property => property.Name).ShouldBe(["items", "page", "pageSize", "totalItems", "totalPages"]);
        page.GetProperty("items").GetArrayLength().ShouldBe(1);
        page.GetProperty("totalPages").GetInt64().ShouldBe(page.GetProperty("totalItems").GetInt64());
    }
}
