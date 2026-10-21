using System.Net;
using System.Text.Json;
using Shouldly;
using Template.Application.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Phase 27 HTTP contract on the self-contained Test host: the requisition journey with budget reservation, totals,
/// Problem Details codes and rule IDs, If-Match/ETag (412/428), Idempotency-Key replay, ownership and visibility
/// (REQ-001, REQ-011, BUD-012), release paths and attachment metadata. EF InMemory has no row locks, so concurrency
/// semantics are proven by the PostgreSQL suite; these tests prove the contract.
/// </summary>
public sealed class ProcurementFunctionalTests(ProcurementWebApplicationFactory factory) : IClassFixture<ProcurementWebApplicationFactory>
{
    private const string Reason = "Duplicated request, cancelled by the requester";

    [Fact]
    public async Task T_REQ_005_b_EndToEnd_CreateDraft_AddLine_Submit_ReservesBudget_AndExposesTheTimeline()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        draft.GetProperty("status").GetString().ShouldBe("Draft");
        draft.GetProperty("number").GetString()!.ShouldStartWith("REQ-");

        var (withLine, lineETag) = await scenario.AddLineAsync(id, etag, quantity: "3", price: "33.3333");
        withLine.GetProperty("estimatedTotal").GetProperty("amount").GetString().ShouldBe("100.00");
        withLine.GetProperty("lines")[0].GetProperty("lineTotal").GetProperty("amount").GetString().ShouldBe("100.00");

        using var submit = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/submit", null, lineETag);
        submit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var submitted = await ApiUser.ReadAsync(submit);
        submitted.GetProperty("status").GetString().ShouldBe("Submitted");
        submitted.GetProperty("version").GetString().ShouldBe(submit.Headers.ETag!.Tag.Trim('"'));
        submitted.GetProperty("submissions")[0].GetProperty("revision").GetInt32().ShouldBe(1);
        Strings(submitted.GetProperty("allowedActions")).ShouldBe(["attach", "withdraw", "cancel"]);

        var allocation = await scenario.AllocationAsync();
        allocation.GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("100.00");
        allocation.GetProperty("available").GetProperty("amount").GetString().ShouldBe("900.00");

        using var history = await scenario.Requester.GetAsync($"{ProcurementRoutes.Requisitions}/{id}/history");
        var entries = (await ApiUser.ReadAsync(history)).GetProperty("entries").EnumerateArray()
            .Select(entry => $"{entry.GetProperty("kind").GetString()}:{entry.GetProperty("to").GetString()}").ToArray();
        entries.ShouldContain("state:Submitted");
        entries.ShouldContain("reservation:Reserved");

        using var reservations = await scenario.Budget.GetAsync($"{ProcurementRoutes.Allocations}/{scenario.AllocationId}/reservations");
        var reservation = (await ApiUser.ReadAsync(reservations)).GetProperty("items")[0];
        reservation.GetProperty("requisitionId").GetGuid().ShouldBe(id);
        reservation.GetProperty("status").GetString().ShouldBe("Reserved");
    }

    [Fact]
    public async Task T_CALC_002_c_ClientSentTotals_AreIgnored()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();

        using var response = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/lines", new
        {
            description = "Monitor 27 inch",
            kind = "Good",
            categoryId = scenario.CategoryId,
            unitOfMeasureId = scenario.UnitId,
            quantity = "2",
            estimatedUnitPrice = new { amount = "10.00", currency = "BRL" },
            lineTotal = new { amount = "1.00", currency = "BRL" },
            estimatedTotal = new { amount = "1.00", currency = "BRL" }
        }, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiUser.ReadAsync(response)).GetProperty("estimatedTotal").GetProperty("amount").GetString().ShouldBe("20.00");
    }

    [Fact]
    public async Task T_API_412_And_428_VersionPreconditions_OnRequisitionWrites()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();

        using var missing = await scenario.Requester.SendAsync(HttpMethod.Post, $"{ProcurementRoutes.Requisitions}/{id}/lines", scenario.Line(), null, Guid.NewGuid().ToString());
        missing.StatusCode.ShouldBe((HttpStatusCode)428);
        (await ApiUser.CodeAsync(missing)).ShouldBe("concurrency.precondition-required");

        await scenario.AddLineAsync(id, etag);
        using var stale = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/submit", null, etag);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var problem = await ApiUser.ReadAsync(stale);
        problem.GetProperty("code").GetString().ShouldBe("concurrency.version-mismatch");
        problem.GetProperty("version").GetString().ShouldBe("2");
    }

    [Fact]
    public async Task IdempotentSubmit_ReplaysTheStoredResponse_AndReservesOnce()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        (_, etag) = await scenario.AddLineAsync(id, etag);
        var key = Guid.NewGuid().ToString();

        using var first = await scenario.Requester.SendAsync(HttpMethod.Post, $"{ProcurementRoutes.Requisitions}/{id}/submit", new { }, etag, key);
        using var replay = await scenario.Requester.SendAsync(HttpMethod.Post, $"{ProcurementRoutes.Requisitions}/{id}/submit", new { }, etag, key);
        using var missingKey = await scenario.Requester.SendAsync(HttpMethod.Post, $"{ProcurementRoutes.Requisitions}/{id}/submit", new { }, etag);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await ApiUser.CodeAsync(missingKey)).ShouldBe("idempotency.key-required");
        (await scenario.AllocationAsync()).GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("300.00");
    }

    [Fact]
    public async Task T_BUD_005_b_InsufficientBudget_Is422_WithRuleId_AndTheDraftStaysUnchanged()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory, allocation: 100m);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        (_, etag) = await scenario.AddLineAsync(id, etag, quantity: "1", price: "100.01");

        using var response = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/submit", null, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ApiUser.ReadAsync(response);
        (problem.GetProperty("code").GetString(), problem.GetProperty("ruleId").GetString()).ShouldBe(("budget.insufficient", "BUD-005"));
        problem.GetProperty("available").GetString().ShouldBe("100");
        using var reread = await scenario.Requester.GetAsync($"{ProcurementRoutes.Requisitions}/{id}");
        (await ApiUser.ReadAsync(reread)).GetProperty("status").GetString().ShouldBe("Draft");
        reread.Headers.ETag!.Tag.ShouldBe(etag);
    }

    [Fact]
    public async Task T_REQ_004_a_UnmetGuards_Are422_WithFieldList()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync(purpose: null);

        using var response = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{draft.GetProperty("id").GetGuid()}/submit", null, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ApiUser.ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("requisition.not-submittable");
        Strings(problem.GetProperty("fields")).ShouldBe(["lines", "purpose"], ignoreOrder: true);
    }

    [Fact]
    public async Task T_REQ_002_b_LineValidation_IsA400WithFieldErrors()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();

        using var response = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/lines", scenario.Line(quantity: "1.5"), etag);
        using var currency = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/lines",
            new { description = "Laptop", kind = "Good", categoryId = scenario.CategoryId, unitOfMeasureId = scenario.UnitId, quantity = "1",
                estimatedUnitPrice = new { amount = "1", currency = "USD" } }, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.ReadAsync(response)).GetProperty("errors").TryGetProperty("quantity", out _).ShouldBeTrue();
        (await ApiUser.ReadAsync(currency)).GetProperty("errors").TryGetProperty("estimatedUnitPrice.currency", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task T_REQ_001_a_OtherRequester_Gets404_AndAProxyWithEditAny_IsAudited()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        var other = await ApiUser.CreateAsync(factory, SeededRoles.Requester);
        var proxy = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);
        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);

        using var foreign = await other.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/lines", scenario.Line(), etag);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var (edited, _) = await scenario.AddLineAsync(id, etag, proxy);
        edited.GetProperty("lines").GetArrayLength().ShouldBe(1);

        using var audit = await auditor.GetAsync($"/api/v2/audit-records?resourceType=purchase-requisition&resourceId={id}&action=requisition.draft.updated");
        var record = (await ApiUser.ReadAsync(audit)).GetProperty("items")[0];
        record.GetProperty("actorId").GetGuid().ShouldBe(await ProcurementScenario.UserIdAsync(proxy));
        record.GetProperty("details").TryGetProperty("lineNumber", out _).ShouldBeTrue();
        record.GetProperty("details").TryGetProperty("description", out _).ShouldBeFalse(); // AUD-003
    }

    [Fact]
    public async Task T_REQ_011_a_VisibilityMatrix_OwnTeamAllAndOthers()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (submitted, _) = await scenario.SubmittedAsync();
        var id = submitted.GetProperty("id").GetGuid();
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.CostCenterManager);
        var outsider = await ApiUser.CreateAsync(factory, SeededRoles.CostCenterManager);
        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);
        var administrator = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);
        await scenario.AssignManagerAsync(await ProcurementScenario.UserIdAsync(manager));

        (await Ids(scenario.Requester, "scope=mine")).ShouldContain(id);
        (await Ids(manager, "scope=visible")).ShouldContain(id);
        (await Ids(manager, "scope=mine")).ShouldNotContain(id);
        (await Ids(outsider, "scope=visible")).ShouldNotContain(id);
        (await Ids(auditor, "scope=visible")).ShouldContain(id);
        using (var hidden = await outsider.GetAsync($"{ProcurementRoutes.Requisitions}/{id}"))
            hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var forbidden = await administrator.GetAsync(ProcurementRoutes.Requisitions))
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var detail = await manager.GetAsync($"{ProcurementRoutes.Requisitions}/{id}"))
            Strings((await ApiUser.ReadAsync(detail)).GetProperty("allowedActions")).ShouldBeEmpty();
    }

    [Fact]
    public async Task T_REQ_008_b_Cancel_ReleasesTheReservation_AndASecondCancelIsRejected()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (submitted, etag) = await scenario.SubmittedAsync();
        var id = submitted.GetProperty("id").GetGuid();

        using var noReason = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/cancel", new { reason = "short" }, etag);
        using var cancelled = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/cancel", new { reason = Reason }, etag);
        using var again = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/cancel", new { reason = Reason }, cancelled.Headers.ETag!.Tag);

        (await ApiUser.CodeAsync(noReason)).ShouldBe("reason-required");
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiUser.ReadAsync(cancelled)).GetProperty("status").GetString().ShouldBe("Cancelled");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.ReadAsync(again)).GetProperty("currentState").GetString().ShouldBe("Cancelled");
        var allocation = await scenario.AllocationAsync();
        allocation.GetProperty("available").GetProperty("amount").GetString().ShouldBe("1000.00");
        using var reservations = await scenario.Budget.GetAsync($"{ProcurementRoutes.Allocations}/{scenario.AllocationId}/reservations?status=Released");
        var reservation = (await ApiUser.ReadAsync(reservations)).GetProperty("items")[0];
        reservation.GetProperty("releaseReason").GetString().ShouldBe("Cancelled");
        reservation.GetProperty("history").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task T_REQ_008_a_RoleWithoutCancelPermission_IsForbidden()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (submitted, etag) = await scenario.SubmittedAsync();
        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);

        using var response = await auditor.PostAsync($"{ProcurementRoutes.Requisitions}/{submitted.GetProperty("id").GetGuid()}/cancel", new { reason = Reason }, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiUser.CodeAsync(response)).ShouldBe("auth.forbidden");
    }

    [Fact]
    public async Task T_REQ_007_Withdraw_ReturnsToDraftRevisionTwo_AndCopyCreatesANewDraftFromACancelledOne()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (submitted, etag) = await scenario.SubmittedAsync();
        var id = submitted.GetProperty("id").GetGuid();

        using var withdrawn = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/withdraw", null, etag);
        var body = await ApiUser.ReadAsync(withdrawn);
        (body.GetProperty("status").GetString(), body.GetProperty("revision").GetInt32()).ShouldBe(("Draft", 2));
        Strings(body.GetProperty("allowedActions")).ShouldNotContain("discard"); // REQ-010: submitted history is kept
        (await scenario.AllocationAsync()).GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("0.00");

        using var cancelled = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/cancel", new { reason = Reason }, withdrawn.Headers.ETag!.Tag);
        using var copy = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/copy", null);
        copy.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copied = await ApiUser.ReadAsync(copy);
        copied.GetProperty("sourceRequisitionId").GetGuid().ShouldBe(id);
        copy.Headers.Location!.ToString().ShouldBe($"/api/v2/purchase-requisitions/{copied.GetProperty("id").GetGuid()}");
    }

    [Fact]
    public async Task T_REQ_010_a_DiscardNeverSubmittedDraft_Is204_ThenGone()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();

        using var discarded = await scenario.Requester.SendAsync(HttpMethod.Delete, $"{ProcurementRoutes.Requisitions}/{id}", null, etag);
        using var gone = await scenario.Requester.GetAsync($"{ProcurementRoutes.Requisitions}/{id}");

        discarded.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task T_BUD_012_a_AvailabilityVisibility_OwnDraftOrBudgetRead()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();

        using var own = await scenario.Requester.GetAsync($"{ProcurementRoutes.Requisitions}/{id}/budget-availability");
        var availability = await ApiUser.ReadAsync(own);
        availability.GetProperty("hasOpenAllocation").GetBoolean().ShouldBeTrue();
        availability.GetProperty("available").GetProperty("amount").GetString().ShouldBe("1000.00");

        (_, etag) = await scenario.AddLineAsync(id, etag);
        using (var submit = await scenario.Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/submit", null, etag))
            submit.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var afterSubmit = await scenario.Requester.GetAsync($"{ProcurementRoutes.Requisitions}/{id}/budget-availability");
        afterSubmit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var global = await scenario.Requester.GetAsync($"{ProcurementRoutes.Allocations}/availability?costCenterId={scenario.CostCenterId}&currency=BRL");
        global.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var controller = await scenario.Budget.GetAsync($"{ProcurementRoutes.Allocations}/availability?costCenterId={scenario.CostCenterId}&currency=BRL");
        (await ApiUser.ReadAsync(controller)).GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("300.00");
    }

    [Fact]
    public async Task T_FIL_002_b_AttachmentMetadata_Maps413_415_And_422()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        var (draft, etag) = await scenario.DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        var route = $"{ProcurementRoutes.Requisitions}/{id}/attachments";

        using var tooLarge = await scenario.Requester.PostAsync(route, new { fileName = "big.pdf", mediaType = "application/pdf", length = 10_485_761 }, etag);
        using var type = await scenario.Requester.PostAsync(route, new { fileName = "x.exe", mediaType = "application/x-msdownload", length = 10 }, etag);
        using var ok = await scenario.Requester.PostAsync(route, new { fileName = "quote.pdf", mediaType = "application/pdf", length = 2048 }, etag);

        tooLarge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await ApiUser.CodeAsync(tooLarge)).ShouldBe("file.too-large");
        type.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var attachment = (await ApiUser.ReadAsync(ok)).GetProperty("attachments")[0];
        (attachment.GetProperty("status").GetString(), attachment.GetProperty("fileName").GetString()).ShouldBe(("Pending", "quote.pdf"));
    }

    [Fact]
    public async Task T_BUD_001_FiscalPeriodOverlap_Is409_AndClosingWithReservations_Is409()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        await scenario.SubmittedAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var overlap = await scenario.Budget.PostAsync(ProcurementRoutes.Periods, new
        {
            code = MasterDataRoutes.NewCode("OV"),
            name = "Overlapping",
            startDate = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            endDate = today.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        });
        using var period = await scenario.Budget.GetAsync($"{ProcurementRoutes.Periods}/{scenario.PeriodId}");
        using var close = await scenario.Budget.PostAsync($"{ProcurementRoutes.Periods}/{scenario.PeriodId}/close", null, period.Headers.ETag!.Tag);

        overlap.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(overlap)).ShouldBe("budget.period-overlap");
        close.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.ReadAsync(close)).GetProperty("ruleId").GetString().ShouldBe("BUD-011");
    }

    [Fact]
    public async Task T_BUD_003_AmountChange_BelowReserved_Is422_AndAValidChangeIsVersioned()
    {
        var scenario = await ProcurementScenario.CreateAsync(factory);
        await scenario.SubmittedAsync();
        using var current = await scenario.Budget.GetAsync($"{ProcurementRoutes.Allocations}/{scenario.AllocationId}");
        var etag = current.Headers.ETag!.Tag;

        using var below = await scenario.Budget.PostAsync($"{ProcurementRoutes.Allocations}/{scenario.AllocationId}/amount-changes",
            new { amount = "299.99", reason = "Budget cut approved by finance" }, etag);
        using var ok = await scenario.Budget.PostAsync($"{ProcurementRoutes.Allocations}/{scenario.AllocationId}/amount-changes",
            new { amount = "1500.00", reason = "Budget increase approved by finance" }, etag);

        (await ApiUser.CodeAsync(below)).ShouldBe("budget.below-reserved");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ApiUser.ReadAsync(ok);
        body.GetProperty("available").GetProperty("amount").GetString().ShouldBe("1200.00");
        body.GetProperty("amountHistory").GetArrayLength().ShouldBe(2);
        ok.Headers.ETag!.Tag.ShouldNotBe(etag);
    }

    [Fact]
    public async Task OpenApi_DocumentsTheProcurementEnumsAsStrings_AndTheNewOperations()
    {
        var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v2/swagger.json");
        var document = await ApiUser.ReadAsync(response);

        var paths = document.GetProperty("paths");
        paths.TryGetProperty("/api/v2/purchase-requisitions/{id}/submit", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v2/budget-allocations/{id}/amount-changes", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v2/fiscal-periods/{id}/close", out _).ShouldBeTrue();
        var kind = document.GetProperty("components").GetProperty("schemas").GetProperty("RequisitionLineKind");
        kind.GetProperty("type").GetString().ShouldBe("string");
    }

    private async Task<Guid[]> Ids(ApiUser user, string query)
    {
        using var response = await user.GetAsync($"{ProcurementRoutes.Requisitions}?{query}&pageSize=100");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ApiUser.ReadAsync(response)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
    }

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToArray();
}
