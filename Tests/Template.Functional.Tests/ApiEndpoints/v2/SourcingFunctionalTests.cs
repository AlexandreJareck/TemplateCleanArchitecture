using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Security;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Phase 29 HTTP contract on the self-contained Test host (BCB PTAX disabled, no broker): the buyer journey from a
/// submitted requisition to an award pending approval, Problem Details codes and rule IDs, If-Match/ETag (412/428),
/// Idempotency-Key replay, SRC-021 visibility, waiver permissions and the Outbox events. EF InMemory has no row locks;
/// concurrency is proven by the PostgreSQL suite.
/// </summary>
public sealed class SourcingFunctionalTests(ProcurementWebApplicationFactory factory) : IClassFixture<ProcurementWebApplicationFactory>
{
    private const string Reason = "Documented sourcing justification";

    [Fact]
    public async Task SC_01_BuyerCreatesAndClosesAnRfq_CapturesComparableQuotes_AndSubmitsTheRecommendedAward()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory, allocation: 50_000m);
        var requisition = await sourcing.SubmittedRequisitionAsync("18", "1000.00"); // 18,000.00 BRL, above the threshold
        var suppliers = new[] { await sourcing.ActiveSupplierAsync(), await sourcing.ActiveSupplierAsync(), await sourcing.ActiveSupplierAsync() };

        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        var id = rfq.GetProperty("id").GetGuid();
        rfq.GetProperty("status").GetString().ShouldBe("Draft");
        rfq.GetProperty("aboveThreshold").GetBoolean().ShouldBeTrue();
        rfq.GetProperty("requiredInvitations").GetInt32().ShouldBe(3);
        rfq.GetProperty("policy").GetProperty("version").GetInt32().ShouldBe(1);
        rfq.GetProperty("lines")[0].GetProperty("quantity").GetString().ShouldBe("18");
        foreach (var supplier in suppliers)
            etag = await sourcing.InviteAsync(id, etag, supplier);

        using var tooSoon = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/publish", new { deadline = DateTime.UtcNow.AddHours(2) }, etag);
        tooSoon.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(tooSoon)).ShouldBe("rfq.deadline-invalid");
        etag = await sourcing.PublishAsync(id, etag);
        (await sourcing.RequisitionStatusAsync(requisition)).ShouldBe("InSourcing");

        decimal[] prices = [950m, 900m, 980m];
        for (var index = 0; index < suppliers.Length; index++)
            etag = await sourcing.EnterQuoteAsync(id, etag, suppliers[index], prices[index].ToString("0.00", CultureInfo.InvariantCulture));

        using var early = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/close", new { reason = Reason }, etag);
        early.StatusCode.ShouldBe(HttpStatusCode.OK, await early.Content.ReadAsStringAsync());
        var closed = await ApiUser.ReadAsync(early);
        closed.GetProperty("status").GetString().ShouldBe("Closed");
        closed.GetProperty("closedEarly").GetBoolean().ShouldBeTrue();
        var comparison = closed.GetProperty("comparison");
        comparison.GetProperty("status").GetString().ShouldBe("Complete");
        comparison.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("rank").GetInt32()).ShouldBe([1, 2, 3]);
        var first = comparison.GetProperty("entries")[0];
        first.GetProperty("supplierId").GetGuid().ShouldBe(suppliers[1]);
        first.GetProperty("rawTotal").GetProperty("amount").GetString().ShouldBe("16200.00");
        first.GetProperty("normalizedTotal").GetProperty("amount").GetString().ShouldBe("16200.00");
        var leading = comparison.GetProperty("recommendedQuoteRevisionId").GetGuid();
        Strings(closed.GetProperty("allowedActions")).ShouldContain("award");

        using var awarded = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/award", new { quoteRevisionId = leading, justification = "Lowest total" },
            early.Headers.ETag!.Tag);
        awarded.StatusCode.ShouldBe(HttpStatusCode.OK, await awarded.Content.ReadAsStringAsync());
        var award = (await ApiUser.ReadAsync(awarded)).GetProperty("awards")[0];
        award.GetProperty("status").GetString().ShouldBe("PendingApproval");
        award.GetProperty("requisitionCurrencyTotal").GetProperty("amount").GetString().ShouldBe("16200.00");
        (await sourcing.RequisitionStatusAsync(requisition)).ShouldBe("PendingApproval");
        (await sourcing.Scenario.AllocationAsync()).GetProperty("reserved").GetProperty("amount").GetString().ShouldBe("16200.00"); // BUD-009

        using var history = await sourcing.Buyer.GetAsync($"{SourcingRoutes.Rfqs}/{id}/history");
        var actions = (await ApiUser.ReadAsync(history)).GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("action").GetString()).ToArray();
        actions.ShouldBe(["created", "supplier-invited", "supplier-invited", "supplier-invited", "published", "quote-recorded", "quote-recorded",
            "quote-recorded", "closed", "award-submitted"]);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = await context.Set<OutboxMessage>().Where(message => message.AggregateId == id).Select(message => message.EventType).ToListAsync();
        events.ShouldBe(["procurement.rfq.published", "procurement.quote.recorded", "procurement.quote.recorded", "procurement.quote.recorded",
            "procurement.rfq.closed", "procurement.award.submitted"], ignoreOrder: true);
        var payload = await context.Set<OutboxMessage>().Where(message => message.AggregateId == id && message.EventType == "procurement.quote.recorded")
            .Select(message => message.PayloadJson).FirstAsync();
        payload.ShouldNotContain("30 days");
        payload.ShouldNotContain("Synthetic quote");
    }

    [Fact]
    public async Task T_SRC_013_b_ProviderDisabledInTest_GivesAnIncompleteComparison_AndTheManagerRecordsAManualRate()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory);
        var requisition = await sourcing.SubmittedRequisitionAsync();
        var supplier = await sourcing.ActiveSupplierAsync();
        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        var id = rfq.GetProperty("id").GetGuid();
        etag = await sourcing.InviteAsync(id, etag, supplier);
        etag = await sourcing.PublishAsync(id, etag);
        etag = await sourcing.EnterQuoteAsync(id, etag, supplier, "20.00", currency: "USD");
        using var closing = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/close", new { reason = Reason }, etag);
        var closed = await ApiUser.ReadAsync(closing);
        closed.GetProperty("comparison").GetProperty("status").GetString().ShouldBe("Incomplete");
        Strings(closed.GetProperty("comparison").GetProperty("missingRateCurrencies")).ShouldBe(["USD"]);
        closed.GetProperty("exchangeRates").GetArrayLength().ShouldBe(0); // never guessed
        etag = closing.Headers.ETag!.Tag;

        using var incomplete = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/award",
            new { quoteRevisionId = closed.GetProperty("comparison").GetProperty("entries")[0].GetProperty("quoteRevisionId").GetGuid() }, etag);
        incomplete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ApiUser.CodeAsync(incomplete)).ShouldBe("award.comparison-incomplete");
        using var buyerRate = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/exchange-rates",
            new { currency = "USD", rate = "5.4321", quoteDate = Today(), reason = Reason }, etag);
        buyerRate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var manual = await sourcing.Manager.PostAsync($"{SourcingRoutes.Rfqs}/{id}/exchange-rates",
            new { currency = "USD", rate = "5.4321", quoteDate = Today(), reason = Reason }, etag);
        manual.StatusCode.ShouldBe(HttpStatusCode.OK, await manual.Content.ReadAsStringAsync());
        var completed = await ApiUser.ReadAsync(manual);
        completed.GetProperty("comparison").GetProperty("status").GetString().ShouldBe("Complete");
        completed.GetProperty("comparison").GetProperty("sequence").GetInt32().ShouldBe(2);
        completed.GetProperty("comparison").GetProperty("entries")[0].GetProperty("normalizedTotal").GetProperty("amount").GetString().ShouldBe("1955.56"); // round(18 × 20.00 × 5.4321)
        var rate = completed.GetProperty("exchangeRates")[0];
        (rate.GetProperty("source").GetString(), rate.GetProperty("rate").GetString()).ShouldBe(("Manual", "5.4321"));
        completed.GetProperty("waivers")[0].GetProperty("type").GetString().ShouldBe("ManualExchangeRate");
    }

    [Fact]
    public async Task T_SRC_006_b_BuyerCannotPublishAnAboveThresholdDirectRfq_ButTheManagerCan()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory, allocation: 50_000m);
        var requisition = await sourcing.SubmittedRequisitionAsync("10", "1000.00");
        var supplier = await sourcing.ActiveSupplierAsync();
        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition, method = "Direct" });
        var id = rfq.GetProperty("id").GetGuid();
        rfq.GetProperty("requiredPublishWaiver").GetString().ShouldBe("DirectPurchase");
        etag = await sourcing.InviteAsync(id, etag, supplier);

        using var denied = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/publish", new { deadline = DateTime.UtcNow.AddDays(2), waiverReason = Reason }, etag);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var problem = await ApiUser.ReadAsync(denied);
        (problem.GetProperty("code").GetString(), problem.GetProperty("ruleId").GetString()).ShouldBe(("waiver.permission-required", "SRC-006"));

        using var published = await sourcing.Manager.PostAsync($"{SourcingRoutes.Rfqs}/{id}/publish", new { deadline = DateTime.UtcNow.AddDays(2), waiverReason = Reason }, etag);
        published.StatusCode.ShouldBe(HttpStatusCode.OK, await published.Content.ReadAsStringAsync());
        (await ApiUser.ReadAsync(published)).GetProperty("waivers")[0].GetProperty("permission").GetString().ShouldBe(Permissions.SourcingWaiversDirectPurchase);
    }

    [Fact]
    public async Task T_SRC_003_b_SuspendedSupplier_CannotBeInvited_AndLateOrUninvitedQuotesAreRejected()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory);
        var requisition = await sourcing.SubmittedRequisitionAsync();
        var supplier = await sourcing.ActiveSupplierAsync();
        var suspended = await sourcing.ActiveSupplierAsync(suspend: true);
        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        var id = rfq.GetProperty("id").GetGuid();

        using var notEligible = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/invitations", new { supplierId = suspended }, etag);
        notEligible.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ApiUser.ReadAsync(notEligible);
        (problem.GetProperty("code").GetString(), problem.GetProperty("ruleId").GetString()).ShouldBe(("supplier.not-eligible", "SRC-003"));
        etag = await sourcing.InviteAsync(id, etag, supplier);
        etag = await sourcing.PublishAsync(id, etag);

        using var uninvited = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/quotes", SourcingScenario.QuoteBody(suspended, "10.00"), etag);
        (await ApiUser.CodeAsync(uninvited)).ShouldBe("quote.supplier-not-invited");
        using var future = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/quotes",
            SourcingScenario.QuoteBody(supplier, "10.00", receivedAt: DateTime.UtcNow.AddDays(5)), etag);
        future.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task T_API_412_428_And_Idempotency_OnSourcingWrites()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory);
        var requisition = await sourcing.SubmittedRequisitionAsync();
        var supplier = await sourcing.ActiveSupplierAsync();
        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        var id = rfq.GetProperty("id").GetGuid();

        using var missing = await sourcing.Buyer.SendAsync(HttpMethod.Post, $"{SourcingRoutes.Rfqs}/{id}/invitations", new { supplierId = supplier }, null, Guid.NewGuid().ToString());
        missing.StatusCode.ShouldBe((HttpStatusCode)428);
        var key = Guid.NewGuid().ToString();
        using var first = await sourcing.Buyer.SendAsync(HttpMethod.Post, $"{SourcingRoutes.Rfqs}/{id}/invitations", new { supplierId = supplier }, etag, key);
        using var replay = await sourcing.Buyer.SendAsync(HttpMethod.Post, $"{SourcingRoutes.Rfqs}/{id}/invitations", new { supplierId = supplier }, etag, key);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        replay.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await ApiUser.ReadAsync(replay)).GetProperty("invitations").GetArrayLength().ShouldBe(1);
        using var stale = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/publish", new { deadline = DateTime.UtcNow.AddDays(2) }, etag);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ApiUser.ReadAsync(stale)).GetProperty("version").GetString().ShouldBe(first.Headers.ETag!.Tag.Trim('"'));

        using var duplicate = await sourcing.Buyer.PostAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(duplicate)).ShouldBe("rfq.already-active");
    }

    [Fact]
    public async Task T_SRC_021_a_RequesterSeesTheAwardedSupplierOnly_AndCannotReadTheRfq()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory, allocation: 50_000m);
        var requisition = await sourcing.SubmittedRequisitionAsync();
        var supplier = await sourcing.ActiveSupplierAsync();
        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        var id = rfq.GetProperty("id").GetGuid();
        etag = await sourcing.InviteAsync(id, etag, supplier);
        etag = await sourcing.PublishAsync(id, etag);
        etag = await sourcing.EnterQuoteAsync(id, etag, supplier, "90.00");
        using var closing = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/close", new { reason = Reason }, etag);
        var revision = (await ApiUser.ReadAsync(closing)).GetProperty("comparison").GetProperty("recommendedQuoteRevisionId").GetGuid();
        using var awarded = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/award", new { quoteRevisionId = revision }, closing.Headers.ETag!.Tag);
        awarded.StatusCode.ShouldBe(HttpStatusCode.OK, await awarded.Content.ReadAsStringAsync());

        using var rfqRead = await sourcing.Scenario.Requester.GetAsync($"{SourcingRoutes.Rfqs}/{id}");
        rfqRead.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var requisitionRead = await sourcing.Scenario.Requester.GetAsync($"{ProcurementRoutes.Requisitions}/{requisition}");
        var view = await ApiUser.ReadAsync(requisitionRead);
        view.GetProperty("status").GetString().ShouldBe("PendingApproval");
        view.GetProperty("award").GetProperty("supplierId").GetGuid().ShouldBe(supplier);
        view.GetProperty("award").TryGetProperty("amount", out _).ShouldBeFalse();
        view.GetProperty("activeRfqId").GetGuid().ShouldBe(id);

        var auditor = await ApiUser.CreateAsync(factory, Template.Application.Security.SeededRoles.Auditor);
        using var auditorRead = await auditor.GetAsync($"{SourcingRoutes.Rfqs}/{id}");
        var audited = await ApiUser.ReadAsync(auditorRead);
        audited.GetProperty("commercialDetailsVisible").GetBoolean().ShouldBeTrue();
        audited.GetProperty("quotes").GetArrayLength().ShouldBe(1);
        Strings(audited.GetProperty("allowedActions")).ShouldBeEmpty();
    }

    [Fact]
    public async Task T_SRC_020_b_CancellingTheRfq_ReturnsTheRequisitionToSubmitted()
    {
        var sourcing = await SourcingScenario.CreateAsync(factory);
        var requisition = await sourcing.SubmittedRequisitionAsync();
        var supplier = await sourcing.ActiveSupplierAsync();
        var (rfq, etag) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        var id = rfq.GetProperty("id").GetGuid();
        etag = await sourcing.InviteAsync(id, etag, supplier);
        etag = await sourcing.PublishAsync(id, etag);

        using var noReason = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/cancel", new { reason = "short" }, etag);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(noReason)).ShouldBe("reason-required");
        using var cancelled = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/cancel", new { reason = Reason }, etag);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await sourcing.RequisitionStatusAsync(requisition)).ShouldBe("Submitted");
        using var again = await sourcing.Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{id}/cancel", new { reason = Reason }, cancelled.Headers.ETag!.Tag);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(again)).ShouldBe("invalid-transition");

        var (round2, _) = await sourcing.Buyer.CreateAsync(SourcingRoutes.Rfqs, new { requisitionId = requisition });
        round2.GetProperty("roundNumber").GetInt32().ShouldBe(2);
        using var list = await sourcing.Buyer.GetAsync($"{SourcingRoutes.Rfqs}?requisitionId={requisition}&sort=roundNumber");
        (await ApiUser.ReadAsync(list)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString()).ShouldBe(["Cancelled", "Draft"]);
    }

    private static string Today() => DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToArray();
}

internal static class SourcingRoutes
{
    public const string Rfqs = "/api/v2/requests-for-quotation";
}

/// <summary>Procurement scenario plus a buyer, a procurement manager and synthetic Active suppliers.</summary>
internal sealed class SourcingScenario
{
    private SourcingScenario(ProcurementScenario scenario, ApiUser buyer, ApiUser manager)
    {
        Scenario = scenario;
        Buyer = buyer;
        Manager = manager;
    }

    public ProcurementScenario Scenario { get; }
    public ApiUser Buyer { get; }
    public ApiUser Manager { get; }

    public static async Task<SourcingScenario> CreateAsync(ProcurementWebApplicationFactory factory, decimal allocation = 1000m) => new(
        await ProcurementScenario.CreateAsync(factory, allocation),
        await ApiUser.CreateAsync(factory, Template.Application.Security.SeededRoles.Buyer),
        await ApiUser.CreateAsync(factory, Template.Application.Security.SeededRoles.ProcurementManager));

    public async Task<Guid> SubmittedRequisitionAsync(string quantity = "1", string price = "100.00") =>
        (await Scenario.SubmittedAsync(quantity, price)).Body.GetProperty("id").GetGuid();

    public async Task<string> RequisitionStatusAsync(Guid id)
    {
        using var response = await Buyer.GetAsync($"{ProcurementRoutes.Requisitions}/{id}");
        return (await ApiUser.ReadAsync(response)).GetProperty("status").GetString()!;
    }

    /// <summary>Draft → contacts/address/category → Active (optionally suspended afterwards); returns the supplier ID.</summary>
    public async Task<Guid> ActiveSupplierAsync(bool suspend = false)
    {
        var (supplier, tag) = await Manager.CreateAsync(MasterDataRoutes.Suppliers,
            new { legalName = $"Synthetic {Guid.NewGuid():N}", tradeName = "Synthetic", country = "BR", taxId = MasterDataRoutes.NewCnpj() });
        var id = supplier.GetProperty("id").GetGuid();
        var (category, _) = await Manager.CreateAsync(MasterDataRoutes.Categories, new { code = MasterDataRoutes.NewCode("SRC"), name = "Supplies" });
        using var contacts = await Manager.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/contacts",
            new { contacts = new[] { new { name = "Synthetic Contact", email = "contact@example.invalid", role = "Sales" } } }, tag);
        using var addresses = await Manager.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/addresses", new
        {
            addresses = new[] { new { type = "Headquarters", postalCode = "01310-100", street = "Avenida Paulista", number = "1000", district = "Bela Vista", city = "São Paulo", state = "SP", country = "BR" } }
        }, contacts.Headers.ETag!.Tag);
        using var categories = await Manager.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/categories",
            new { categoryIds = new[] { category.GetProperty("id").GetGuid() } }, addresses.Headers.ETag!.Tag);
        using var activated = await Manager.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/activate", null, categories.Headers.ETag!.Tag);
        activated.StatusCode.ShouldBe(HttpStatusCode.OK, await activated.Content.ReadAsStringAsync());
        if (suspend)
        {
            using var suspended = await Manager.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/suspend",
                new { reason = "Synthetic suspension for sourcing tests" }, activated.Headers.ETag!.Tag);
            suspended.StatusCode.ShouldBe(HttpStatusCode.OK, await suspended.Content.ReadAsStringAsync());
        }

        return id;
    }

    public async Task<string> InviteAsync(Guid rfqId, string etag, Guid supplierId)
    {
        using var response = await Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{rfqId}/invitations", new { supplierId }, etag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.Tag;
    }

    public async Task<string> PublishAsync(Guid rfqId, string etag)
    {
        using var response = await Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{rfqId}/publish", new { deadline = DateTime.UtcNow.AddDays(3) }, etag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.Tag;
    }

    public async Task<string> EnterQuoteAsync(Guid rfqId, string etag, Guid supplierId, string unitPrice, string currency = "BRL")
    {
        using var response = await Buyer.PostAsync($"{SourcingRoutes.Rfqs}/{rfqId}/quotes", QuoteBody(supplierId, unitPrice, currency: currency), etag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response.Headers.ETag!.Tag;
    }

    public static object QuoteBody(Guid supplierId, string unitPrice, string currency = "BRL", DateTime? receivedAt = null) => new
    {
        supplierId,
        currency,
        lines = new[] { new { rfqLineNumber = 1, quantityOffered = "18", unitPrice } },
        freight = "0",
        validUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        deliveryDays = 7,
        paymentTerms = "30 days",
        notes = "Synthetic quote",
        source = "Email",
        receivedAt = receivedAt ?? DateTime.UtcNow.AddMinutes(-1),
        total = new { amount = "1.00", currency }
    };
}
