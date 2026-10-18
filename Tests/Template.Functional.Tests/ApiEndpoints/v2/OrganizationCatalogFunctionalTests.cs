using System.Net;
using Shouldly;
using Template.Application.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// HTTP contract of departments, cost centers, organization settings, categories, units and catalog items
/// (ORG-001..006, CAT-001..004): Problem Details codes, ETag/If-Match (412/428), Idempotency-Key, paging and no delete.
/// </summary>
public sealed class OrganizationCatalogFunctionalTests(MasterDataWebApplicationFactory factory) : IClassFixture<MasterDataWebApplicationFactory>
{
    private const string Reason = "Synthetic reorganization reason";

    [Fact]
    public async Task T_ORG_001_c_DepartmentLifecycle_FollowsTheConventions()
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);
        var code = MasterDataRoutes.NewCode("fin");

        using var created = await admin.PostAsync(MasterDataRoutes.Departments, new { code = code.ToLowerInvariant(), name = "Finance" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await ApiUser.ReadAsync(created);
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("version").GetString().ShouldBe("1");
        created.Headers.ETag!.Tag.ShouldBe("\"1\"");
        created.Headers.Location!.OriginalString.ShouldBe($"/api/v2/departments/{id}");
        created.Headers.CacheControl!.NoStore.ShouldBeTrue();

        using var get = await admin.GetAsync($"{MasterDataRoutes.Departments}/{id}");
        get.Headers.ETag!.Tag.ShouldBe("\"1\"");

        using var missing = await admin.PutAsync($"{MasterDataRoutes.Departments}/{id}", new { name = "Finance & Control" }, ifMatch: null);
        using var stale = await admin.PutAsync($"{MasterDataRoutes.Departments}/{id}", new { name = "Finance & Control" }, "\"9\"");
        using var updated = await admin.PutAsync($"{MasterDataRoutes.Departments}/{id}", new { name = "Finance & Control" }, "\"1\"");
        missing.StatusCode.ShouldBe((HttpStatusCode)428);
        (await ApiUser.CodeAsync(missing)).ShouldBe("concurrency.precondition-required");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ApiUser.ReadAsync(stale)).GetProperty("version").GetString().ShouldBe("1");
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        updated.Headers.ETag!.Tag.ShouldBe("\"2\"");
        (await ApiUser.ReadAsync(updated)).GetProperty("code").GetString().ShouldBe(code);

        using var noReason = await admin.PostAsync($"{MasterDataRoutes.Departments}/{id}/deactivate", new { }, "\"2\"");
        using var noKey = await admin.SendAsync(HttpMethod.Post, $"{MasterDataRoutes.Departments}/{id}/deactivate", new { reason = Reason }, "\"2\"");
        using var deactivated = await admin.PostAsync($"{MasterDataRoutes.Departments}/{id}/deactivate", new { reason = Reason }, "\"2\"");
        using var again = await admin.PostAsync($"{MasterDataRoutes.Departments}/{id}/deactivate", new { reason = Reason }, "\"3\"");
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(noReason)).ShouldBe("reason-required");
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(noKey)).ShouldBe("idempotency.key-required");
        (await ApiUser.ReadAsync(deactivated)).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var conflict = await ApiUser.ReadAsync(again);
        conflict.GetProperty("code").GetString().ShouldBe("invalid-transition");
        conflict.GetProperty("currentState").GetString().ShouldBe("Inactive");
    }

    [Theory]
    [InlineData("A", "validation")]
    [InlineData("FIN_01", "validation")]
    public async Task T_ORG_001_c_InvalidCodes_Are400WithRuleId(string code, string expected)
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);

        using var response = await admin.PostAsync(MasterDataRoutes.Departments, new { code, name = "X" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ApiUser.ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe(expected);
        problem.GetProperty("ruleId").GetString().ShouldBe("ORG-001");
        problem.GetProperty("errors").TryGetProperty("code", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task T_ORG_001_c_DuplicateCode_Is409_AndIdempotentReplay_ReturnsTheFirstResponse()
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);
        var code = MasterDataRoutes.NewCode("DUP");
        var key = Guid.NewGuid().ToString();

        using var first = await admin.SendAsync(HttpMethod.Post, MasterDataRoutes.Departments, new { code, name = "First" }, idempotencyKey: key);
        using var replay = await admin.SendAsync(HttpMethod.Post, MasterDataRoutes.Departments, new { code, name = "First" }, idempotencyKey: key);
        using var duplicate = await admin.PostAsync(MasterDataRoutes.Departments, new { code = code.ToLowerInvariant(), name = "Second" });

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(duplicate)).ShouldBe("organization.code-duplicate");
    }

    [Fact]
    public async Task T_ORG_003_CostCenterRules_OverHttp()
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);
        var (department, _) = await admin.CreateAsync(MasterDataRoutes.Departments, new { code = MasterDataRoutes.NewCode("OPS"), name = "Operations" });
        var departmentId = department.GetProperty("id").GetGuid();
        var (costCenter, costCenterTag) = await admin.CreateAsync(MasterDataRoutes.CostCenters,
            new { code = MasterDataRoutes.NewCode("CC"), name = "Logistics", departmentId });

        using var blocked = await admin.PostAsync($"{MasterDataRoutes.Departments}/{departmentId}/deactivate", new { reason = Reason }, "\"1\"");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(blocked)).ShouldBe("organization.department-has-active-cost-centers");

        (await admin.PostAsync($"{MasterDataRoutes.CostCenters}/{costCenter.GetProperty("id").GetGuid()}/deactivate", new { reason = Reason }, costCenterTag))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PostAsync($"{MasterDataRoutes.Departments}/{departmentId}/deactivate", new { reason = Reason }, "\"1\"")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var inactive = await admin.PostAsync(MasterDataRoutes.CostCenters, new { code = MasterDataRoutes.NewCode("CC"), name = "New", departmentId });
        inactive.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ApiUser.CodeAsync(inactive)).ShouldBe("organization.inactive");

        using var list = await admin.GetAsync($"{MasterDataRoutes.CostCenters}?departmentId={departmentId}&isActive=false&sort=-code");
        var page = await ApiUser.ReadAsync(list);
        page.EnumerateObject().Select(property => property.Name).ShouldBe(["items", "page", "pageSize", "totalItems", "totalPages"]);
        page.GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task T_ORG_006_a_ManagerMustBeAnActiveUser_AndTheLookupExposesSafeFieldsOnly()
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);
        var (department, _) = await admin.CreateAsync(MasterDataRoutes.Departments, new { code = MasterDataRoutes.NewCode("HR"), name = "People" });

        using var unknown = await admin.PostAsync(MasterDataRoutes.CostCenters, new
        {
            code = MasterDataRoutes.NewCode("CC"), name = "Payroll", departmentId = department.GetProperty("id").GetGuid(), managerUserId = Guid.NewGuid()
        });
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.ReadAsync(unknown)).GetProperty("ruleId").GetString().ShouldBe("ORG-006");

        using var users = await admin.GetAsync($"{MasterDataRoutes.Users}?q=syn&pageSize=1");
        users.StatusCode.ShouldBe(HttpStatusCode.OK);
        var user = (await ApiUser.ReadAsync(users)).GetProperty("items")[0];
        user.EnumerateObject().Select(property => property.Name).ShouldBe(["userId", "userName", "displayName", "isActive"]);

        using var assigned = await admin.PostAsync(MasterDataRoutes.CostCenters, new
        {
            code = MasterDataRoutes.NewCode("CC"), name = "Payroll", departmentId = department.GetProperty("id").GetGuid(), managerUserId = user.GetProperty("userId").GetGuid()
        });
        assigned.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OrganizationSettings_ReadAndReplace_WithConcurrency()
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);

        using var get = await admin.GetAsync(MasterDataRoutes.Settings);
        var settings = await ApiUser.ReadAsync(get);
        settings.GetProperty("baseCurrency").GetString().ShouldBe("BRL");
        settings.GetProperty("timeZone").GetString().ShouldBe("America/Sao_Paulo");
        var tag = get.Headers.ETag!.Tag;

        using var invalid = await admin.PutAsync(MasterDataRoutes.Settings, new { displayName = "Acme", baseCurrency = "BRL", timeZone = "Mars/Olympus", supportedCurrencies = new[] { "BRL" } }, tag);
        using var stale = await admin.PutAsync(MasterDataRoutes.Settings, new { displayName = "Acme", baseCurrency = "BRL", timeZone = "America/Sao_Paulo", supportedCurrencies = new[] { "BRL" } }, "\"999\"");
        using var updated = await admin.PutAsync(MasterDataRoutes.Settings, new { displayName = "Acme Synthetic", baseCurrency = "BRL", timeZone = "America/Sao_Paulo", supportedCurrencies = new[] { "BRL", "USD", "EUR" } }, tag);

        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiUser.ReadAsync(updated)).GetProperty("displayName").GetString().ShouldBe("Acme Synthetic");
    }

    [Fact]
    public async Task T_CAT_CatalogItemFlow_MoneyAsString_InactiveReferences422_AndFilters()
    {
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);
        var (category, categoryTag) = await manager.CreateAsync(MasterDataRoutes.Categories, new { code = MasterDataRoutes.NewCode("FUR"), name = "Furniture" });
        var (unit, _) = await manager.CreateAsync(MasterDataRoutes.Units, new { code = MasterDataRoutes.NewCode("U"), name = "Unit", allowedDecimals = 0 });
        var categoryId = category.GetProperty("id").GetGuid();
        var unitId = unit.GetProperty("id").GetGuid();
        unit.GetProperty("allowedDecimals").GetInt32().ShouldBe(0);

        var (item, _) = await manager.CreateAsync(MasterDataRoutes.Items, new
        {
            code = MasterDataRoutes.NewCode("sku"), description = "Desk", kind = "Good", categoryId, unitOfMeasureId = unitId,
            referencePrice = new { amount = "1234.5600", currency = "brl" }
        });
        item.GetProperty("kind").GetString().ShouldBe("Good");
        item.GetProperty("referencePrice").GetProperty("amount").GetString().ShouldBe("1234.5600");
        item.GetProperty("referencePrice").GetProperty("currency").GetString().ShouldBe("BRL");

        using var badKind = await manager.PostAsync(MasterDataRoutes.Items, new { code = MasterDataRoutes.NewCode("SKU"), description = "X", kind = "Gadget", categoryId, unitOfMeasureId = unitId });
        badKind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(badKind)).ShouldBe("validation");

        (await manager.PostAsync($"{MasterDataRoutes.Categories}/{categoryId}/deactivate", new { reason = Reason }, categoryTag)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var inactive = await manager.PostAsync(MasterDataRoutes.Items, new { code = MasterDataRoutes.NewCode("SKU"), description = "Chair", kind = "Good", categoryId, unitOfMeasureId = unitId });
        inactive.StatusCode.ShouldBe((HttpStatusCode)422);
        var problem = await ApiUser.ReadAsync(inactive);
        (problem.GetProperty("code").GetString(), problem.GetProperty("ruleId").GetString()).ShouldBe(("catalog.inactive", "CAT-002"));

        using var list = await manager.GetAsync($"{MasterDataRoutes.Items}?kind=Good&categoryId={categoryId}&q=desk");
        (await ApiUser.ReadAsync(list)).GetProperty("totalItems").GetInt64().ShouldBe(1);
        using var badSort = await manager.GetAsync($"{MasterDataRoutes.Items}?sort=price");
        badSort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/v2/departments")]
    [InlineData("/api/v2/cost-centers")]
    [InlineData("/api/v2/suppliers")]
    [InlineData("/api/v2/categories")]
    [InlineData("/api/v2/units-of-measure")]
    [InlineData("/api/v2/catalog-items")]
    public async Task T_ORG_005_NoHardDeleteEndpointExists(string route)
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);

        using var response = await admin.SendAsync(HttpMethod.Delete, $"{route}/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task UnknownIds_Are404WithCode_InAnyLanguage()
    {
        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);

        using var response = await admin.SendAsync(HttpMethod.Get, $"{MasterDataRoutes.Departments}/{Guid.NewGuid()}", acceptLanguage: "pt-BR");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiUser.CodeAsync(response)).ShouldBe("not-found");
    }
}
