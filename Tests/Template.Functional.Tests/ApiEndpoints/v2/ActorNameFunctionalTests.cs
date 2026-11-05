using System.Net;
using System.Text.Json;
using Shouldly;
using Template.Application.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// F-06 (functional UI/UX report 2026-11-05): the requisition timeline, the RFQ history and the audit query return
/// <c>actorName</c> next to <c>actorId</c> so roles without <c>access.users.manage</c> see who acted. The name is the
/// display name only (never the email or user name of a named user), and it is returned only on resources the caller may
/// already read: an outsider still gets 404 and a caller without <c>audit.read</c> still gets 403.
/// </summary>
public sealed class ActorNameFunctionalTests(ProcurementWebApplicationFactory factory) : IClassFixture<ProcurementWebApplicationFactory>
{
    [Theory]
    [InlineData(SeededRoles.Requester)]
    [InlineData(SeededRoles.Buyer)]
    [InlineData(SeededRoles.Auditor)]
    public void TheReadersInTheseTests_DoNotHoldTheUserDirectoryPermission(string role) =>
        SeededRoles.PermissionsByRole[role].ShouldNotContain(Permissions.AccessUsersManage);

    [Fact]
    public async Task RequisitionTimeline_NamesTheActor_ForAReaderWithoutTheUserDirectory_AndHidesItFromAnOutsider()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (submitted, _) = await scenario.SubmittedAsync();
        var id = submitted.GetProperty("id").GetGuid();
        var requester = await MeAsync(scenario.Requester);
        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);
        var outsider = await ApiUser.CreateAsync(factory, SeededRoles.Requester);

        foreach (var reader in new[] { scenario.Requester, auditor })
        {
            using var response = await reader.GetAsync($"{ProcurementRoutes.Requisitions}/{id}/history");
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var raw = await response.Content.ReadAsStringAsync();
            raw.ShouldNotContain("@example.invalid"); // never an email
            var entries = (await ApiUser.ReadAsync(response)).GetProperty("entries").EnumerateArray().ToArray();
            var submittedEntry = entries.Single(entry => entry.GetProperty("kind").GetString() == "state" && entry.GetProperty("to").GetString() == "Submitted");
            submittedEntry.GetProperty("actorId").GetGuid().ShouldBe(requester.UserId);
            submittedEntry.GetProperty("actorName").GetString().ShouldBe(requester.DisplayName);
        }

        using var hidden = await outsider.GetAsync($"{ProcurementRoutes.Requisitions}/{id}/history");
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).ShouldNotContain("actorName");
    }

    [Fact]
    public async Task RfqHistory_NamesTheBuyer_ForAnAuditor_AndStaysForbiddenForTheRequester()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory);
        var requisition = await sourcing.SubmittedRequisitionAsync();
        var (rfq, _) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        var id = rfq.GetProperty("id").GetGuid();
        var buyer = await MeAsync(sourcing.Buyer);
        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);

        using var response = await auditor.GetAsync($"{SourcingRoutes.Rfqs}/{id}/history");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("@example.invalid");
        var created = (await ApiUser.ReadAsync(response)).GetProperty("entries")[0];
        created.GetProperty("action").GetString().ShouldBe("created");
        created.GetProperty("actorId").GetGuid().ShouldBe(buyer.UserId);
        created.GetProperty("actorName").GetString().ShouldBe(buyer.DisplayName);

        using var forbidden = await sourcing.Scenario.Requester.GetAsync($"{SourcingRoutes.Rfqs}/{id}/history");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadAsStringAsync()).ShouldNotContain("actorName");
    }

    [Fact]
    public async Task AuditRecords_NameTheActor_ForAuditRead_AndStayForbiddenWithoutIt()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, _) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        var requester = await MeAsync(scenario.Requester);
        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);
        var url = $"/api/v2/audit-records?resourceType=purchase-requisition&resourceId={id}";

        using var response = await auditor.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("@example.invalid");
        var records = (await ApiUser.ReadAsync(response)).GetProperty("items").EnumerateArray().ToArray();
        records.ShouldNotBeEmpty();
        foreach (var record in records.Where(record => record.GetProperty("actorId").ValueKind != JsonValueKind.Null))
        {
            record.GetProperty("actorId").GetGuid().ShouldBe(requester.UserId);
            record.GetProperty("actorName").GetString().ShouldBe(requester.DisplayName);
        }

        using var forbidden = await scenario.Requester.GetAsync(url);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await forbidden.Content.ReadAsStringAsync()).ShouldNotContain("actorName");
    }

    private static async Task<(Guid UserId, string DisplayName)> MeAsync(ApiUser user)
    {
        using var response = await user.GetAsync("/api/v2/account/me");
        var me = await ApiUser.ReadAsync(response);
        var displayName = me.GetProperty("displayName").GetString();
        displayName.ShouldNotBeNullOrWhiteSpace();
        return (me.GetProperty("userId").GetGuid(), displayName!);
    }
}
