using System.Net;
using Shouldly;
using Template.Application.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Data-driven permission matrix of every Phase 25 endpoint family (docs/product/permissions.md §3 and §5, SEC-008,
/// SUP-006): for each seeded role an allowed call is never 403 and a denied call is always 403 <c>auth.forbidden</c>.
/// Denied writes are rejected before any validation or business rule runs.
/// </summary>
public sealed class MasterDataPermissionMatrixTests(MasterDataWebApplicationFactory factory) : IClassFixture<MasterDataWebApplicationFactory>
{
    private static readonly (string Method, string Route, string Permission)[] Endpoints =
    [
        ("GET", "/api/v2/departments", Permissions.OrganizationRead),
        ("POST", "/api/v2/departments", Permissions.OrganizationManage),
        ("GET", "/api/v2/cost-centers", Permissions.OrganizationRead),
        ("POST", $"/api/v2/cost-centers/{Guid.Empty}/deactivate", Permissions.OrganizationManage),
        ("GET", "/api/v2/organization-settings", Permissions.OrganizationRead),
        ("PUT", "/api/v2/organization-settings", Permissions.OrganizationSettingsManage),
        ("GET", "/api/v2/suppliers", Permissions.SuppliersRead),
        ("POST", "/api/v2/suppliers", Permissions.SuppliersManage),
        ("PUT", $"/api/v2/suppliers/{Guid.Empty}/contacts", Permissions.SuppliersManage),
        ("POST", $"/api/v2/suppliers/{Guid.Empty}/suspend", Permissions.SuppliersStatusChange),
        ("GET", "/api/v2/categories", Permissions.CatalogRead),
        ("POST", "/api/v2/categories", Permissions.CatalogManage),
        ("GET", "/api/v2/units-of-measure", Permissions.CatalogRead),
        ("GET", "/api/v2/catalog-items", Permissions.CatalogRead),
        ("POST", "/api/v2/catalog-items", Permissions.CatalogManage),
        ("GET", "/api/v2/enrichment/postal-addresses/01310100", Permissions.SuppliersManage),
        ("GET", "/api/v2/users", Permissions.AccessUsersManage)
    ];

    public static TheoryData<string> Roles => new(SeededRoles.All);

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task EveryEndpoint_FollowsThePermissionColumn(string role)
    {
        var user = await ApiUser.CreateAsync(factory, role);
        var granted = SeededRoles.PermissionsByRole[role];

        foreach (var (method, route, permission) in Endpoints)
        {
            using var response = method == "GET"
                ? await user.GetAsync(route)
                : await user.SendAsync(new HttpMethod(method), route, new { }, "\"1\"", Guid.NewGuid().ToString());

            if (granted.Contains(permission))
            {
                response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, $"{role} {method} {route}");
                response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, $"{role} {method} {route}");
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{role} {method} {route}");
                (await ApiUser.CodeAsync(response)).ShouldBe("auth.forbidden");
            }
        }
    }

    [Fact]
    public async Task Anonymous_Is401_OnEveryEndpoint()
    {
        var client = factory.CreateClient();

        foreach (var (method, route, _) in Endpoints)
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
        }
    }

    [Fact]
    public void Matrix_GivesSupplierStatusChangeToProcurementManagerOnly_AndAdministratorNoSupplierWrites()
    {
        SeededRoles.All.Where(role => SeededRoles.PermissionsByRole[role].Contains(Permissions.SuppliersStatusChange)).ShouldBe([SeededRoles.ProcurementManager]);
        SeededRoles.PermissionsByRole[SeededRoles.Administrator].ShouldNotContain(Permissions.SuppliersManage);
        SeededRoles.PermissionsByRole[SeededRoles.Administrator].ShouldContain(Permissions.SuppliersRead);
    }
}
