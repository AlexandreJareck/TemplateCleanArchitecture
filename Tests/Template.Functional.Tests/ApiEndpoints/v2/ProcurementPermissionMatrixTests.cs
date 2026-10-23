using System.Net;
using Shouldly;
using Template.Application.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Data-driven permission matrix of every Phase 27 endpoint family (docs/product/permissions.md §3 and §5, SEC-008):
/// for each seeded role an allowed call is never 403 and a denied call is always 403 <c>auth.forbidden</c>. Endpoints
/// whose authorization is resource-scoped (REQ-011, REQ-008) accept any of the listed permissions.
/// </summary>
public sealed class ProcurementPermissionMatrixTests(ProcurementWebApplicationFactory factory) : IClassFixture<ProcurementWebApplicationFactory>
{
    private static readonly (string Method, string Route, string[] AnyOf)[] Endpoints =
    [
        ("GET", "/api/v2/fiscal-periods", [Permissions.BudgetRead]),
        ("POST", "/api/v2/fiscal-periods", [Permissions.BudgetPeriodsManage]),
        ("POST", $"/api/v2/fiscal-periods/{Guid.Empty}/close", [Permissions.BudgetPeriodsManage]),
        ("GET", "/api/v2/budget-allocations", [Permissions.BudgetRead]),
        ("GET", $"/api/v2/budget-allocations/availability?costCenterId={Guid.NewGuid()}&currency=BRL", [Permissions.BudgetRead]),
        ("GET", $"/api/v2/budget-allocations/{Guid.Empty}/reservations", [Permissions.BudgetRead]),
        ("POST", "/api/v2/budget-allocations", [Permissions.BudgetAllocationsManage]),
        ("POST", $"/api/v2/budget-allocations/{Guid.Empty}/amount-changes", [Permissions.BudgetAllocationsManage]),
        ("GET", "/api/v2/purchase-requisitions", [Permissions.RequisitionsCreate, Permissions.RequisitionsReadTeam, Permissions.RequisitionsReadAll]),
        ("GET", $"/api/v2/purchase-requisitions/{Guid.Empty}", [Permissions.RequisitionsCreate, Permissions.RequisitionsReadTeam, Permissions.RequisitionsReadAll]),
        ("POST", "/api/v2/purchase-requisitions", [Permissions.RequisitionsCreate]),
        ("POST", $"/api/v2/purchase-requisitions/{Guid.Empty}/lines", [Permissions.RequisitionsCreate]),
        ("DELETE", $"/api/v2/purchase-requisitions/{Guid.Empty}", [Permissions.RequisitionsCreate]),
        ("POST", $"/api/v2/purchase-requisitions/{Guid.Empty}/attachments", [Permissions.RequisitionsCreate]),
        ("POST", $"/api/v2/purchase-requisitions/{Guid.Empty}/submit", [Permissions.RequisitionsSubmit]),
        ("POST", $"/api/v2/purchase-requisitions/{Guid.Empty}/withdraw", [Permissions.RequisitionsSubmit]),
        ("POST", $"/api/v2/purchase-requisitions/{Guid.Empty}/copy", [Permissions.RequisitionsCreate]),
        // Phase 29 sourcing (SRC-021 reads are use-case authorized: rfqs.manage or quotes.read).
        ("GET", "/api/v2/requests-for-quotation", [Permissions.SourcingRfqsManage, Permissions.SourcingQuotesRead]),
        ("GET", $"/api/v2/requests-for-quotation/{Guid.Empty}", [Permissions.SourcingRfqsManage, Permissions.SourcingQuotesRead]),
        ("GET", $"/api/v2/requests-for-quotation/{Guid.Empty}/history", [Permissions.SourcingRfqsManage, Permissions.SourcingQuotesRead]),
        ("POST", "/api/v2/requests-for-quotation", [Permissions.SourcingRfqsManage]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/invitations", [Permissions.SourcingRfqsManage]),
        ("DELETE", $"/api/v2/requests-for-quotation/{Guid.Empty}/invitations/{Guid.Empty}", [Permissions.SourcingRfqsManage]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/publish", [Permissions.SourcingRfqsManage]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/extend-deadline", [Permissions.SourcingRfqsManage]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/reminders", [Permissions.SourcingRfqsManage]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/close", [Permissions.SourcingRfqsManage]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/cancel", [Permissions.SourcingRfqsManage]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/quotes", [Permissions.SourcingQuotesEnter]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/quotes/{Guid.Empty}/revisions", [Permissions.SourcingQuotesEnter]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/quotes/{Guid.Empty}/withdraw", [Permissions.SourcingQuotesEnter]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/exchange-rates", [Permissions.SourcingRatesManual]),
        ("POST", $"/api/v2/requests-for-quotation/{Guid.Empty}/award", [Permissions.SourcingAwardsSubmit])
    ];

    public static TheoryData<string> Roles => new(SeededRoles.All);

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task EveryEndpoint_FollowsThePermissionColumn(string role)
    {
        var user = await ApiUser.CreateAsync(factory, role);
        var granted = SeededRoles.PermissionsByRole[role];

        foreach (var (method, route, anyOf) in Endpoints)
        {
            using var response = method == "GET"
                ? await user.GetAsync(route)
                : await user.SendAsync(new HttpMethod(method), route, new { }, "\"1\"", Guid.NewGuid().ToString());

            if (anyOf.Any(granted.Contains))
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
    public void Matrix_SeparatesBudgetAdministrationFromSourcing_AndAdministratorHoldsNoProcurementDecision()
    {
        // SOD-6, SOD-7.
        SeededRoles.All.Where(role => SeededRoles.PermissionsByRole[role].Contains(Permissions.BudgetAllocationsManage)).ShouldBe([SeededRoles.BudgetController]);
        SeededRoles.PermissionsByRole[SeededRoles.BudgetController].ShouldNotContain(Permissions.SourcingRfqsManage);
        SeededRoles.PermissionsByRole[SeededRoles.Administrator].ShouldNotContain(Permissions.RequisitionsCreate);
        SeededRoles.PermissionsByRole[SeededRoles.Administrator].ShouldNotContain(Permissions.BudgetRead);
        // SOD-5: high-value and post-competition sourcing waivers are held only by ProcurementManager.
        foreach (var waiver in new[] { Permissions.SourcingWaiversDirectPurchase, Permissions.SourcingWaiversInsufficientQuotations,
                     Permissions.SourcingWaiversNonLeading, Permissions.SourcingRatesManual })
            SeededRoles.All.Where(role => SeededRoles.PermissionsByRole[role].Contains(waiver)).ShouldBe([SeededRoles.ProcurementManager]);
        SeededRoles.PermissionsByRole[SeededRoles.Administrator].ShouldNotContain(Permissions.SourcingQuotesRead);
    }
}
