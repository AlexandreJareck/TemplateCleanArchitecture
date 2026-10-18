using System.Net;
using System.Text.Json;
using Shouldly;
using Template.Application.Interfaces.Integrations;
using Template.Application.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// HTTP contract of suppliers and enrichment (SUP-001..006, INT-001, INT-002, AUD-001, AUD-003): draft → active
/// flow, reasons, eligibility, data visibility, explicit provider outcomes and accepted snapshots. The registry port is a
/// scripted fake; no external provider is contacted.
/// </summary>
public sealed class SupplierFunctionalTests(MasterDataWebApplicationFactory factory) : IClassFixture<MasterDataWebApplicationFactory>
{
    private const string Reason = "Two late deliveries in this quarter";

    [Fact]
    public async Task T_SUP_002_DraftToActive_ThenSuspendAndReactivate_WithAuditTrail()
    {
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);
        var (supplier, tag) = await CreateDraftAsync(manager);
        var id = supplier.GetProperty("id").GetGuid();
        supplier.GetProperty("status").GetString().ShouldBe("Draft");
        supplier.GetProperty("isEligibleForNewCommitments").GetBoolean().ShouldBeFalse();
        supplier.GetProperty("missingForActivation").GetString().ShouldBe("contactWithEmail");

        using var incomplete = await manager.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/activate", null, tag);
        incomplete.StatusCode.ShouldBe((HttpStatusCode)422);
        var problem = await ApiUser.ReadAsync(incomplete);
        (problem.GetProperty("code").GetString(), problem.GetProperty("ruleId").GetString(), problem.GetProperty("missing").GetString())
            .ShouldBe(("supplier.activation-incomplete", "SUP-002", "contactWithEmail"));

        tag = await CompleteAsync(manager, id, tag);
        using var activated = await manager.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/activate", null, tag);
        activated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var active = await ApiUser.ReadAsync(activated);
        active.GetProperty("status").GetString().ShouldBe("Active");
        active.GetProperty("isEligibleForNewCommitments").GetBoolean().ShouldBeTrue();
        tag = activated.Headers.ETag!.Tag;

        using var noReason = await manager.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/suspend", new { reason = "short" }, tag);
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(noReason)).ShouldBe("reason-required");

        using var suspended = await manager.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/suspend", new { reason = Reason }, tag);
        var suspendedBody = await ApiUser.ReadAsync(suspended);
        suspendedBody.GetProperty("status").GetString().ShouldBe("Suspended");
        suspendedBody.GetProperty("isEligibleForNewCommitments").GetBoolean().ShouldBeFalse();
        using var reactivated = await manager.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/reactivate", new { reason = Reason }, suspended.Headers.ETag!.Tag);
        var history = (await ApiUser.ReadAsync(reactivated)).GetProperty("statusHistory").EnumerateArray()
            .Select(change => change.GetProperty("to").GetString()).ToArray();
        history.ShouldBe(["Active", "Suspended", "Active"]);

        var auditor = await ApiUser.CreateAsync(factory, SeededRoles.Auditor);
        using var audit = await auditor.GetAsync($"/api/v2/audit-records?resourceType=supplier&resourceId={id}&sort=occurredAt&pageSize=100");
        var records = (await ApiUser.ReadAsync(audit)).GetProperty("items").EnumerateArray().ToArray();
        records.Select(record => record.GetProperty("action").GetString()).ShouldBe([
            "supplier.created", "supplier.contacts-changed", "supplier.addresses-changed", "supplier.categories-changed",
            "supplier.activated", "supplier.suspended", "supplier.reactivated"]);
        var raw = await audit.Content.ReadAsStringAsync();
        raw.ShouldNotContain("maria@example.invalid"); // AUD-003
        raw.ShouldNotContain("Paulista");
    }

    [Fact]
    public async Task T_SUP_001_DuplicateAndInvalidTaxIds_AndNormalization()
    {
        var buyer = await ApiUser.CreateAsync(factory, SeededRoles.Buyer);
        var cnpj = MasterDataRoutes.NewCnpj();
        var formatted = $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";

        var (created, _) = await buyer.CreateAsync(MasterDataRoutes.Suppliers, new { legalName = "Acme", country = "br", taxId = formatted });
        created.GetProperty("taxId").GetString().ShouldBe(cnpj);
        created.GetProperty("formattedTaxId").GetString().ShouldBe(formatted);

        using var duplicate = await buyer.PostAsync(MasterDataRoutes.Suppliers, new { legalName = "Other", country = "BR", taxId = cnpj });
        using var invalid = await buyer.PostAsync(MasterDataRoutes.Suppliers, new { legalName = "Other", country = "BR", taxId = "11.222.333/0001-80" });
        using var alphanumeric = await buyer.PostAsync(MasterDataRoutes.Suppliers, new { legalName = "Alpha", country = "BR", taxId = "12.ABC.345/01DE-35" });

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiUser.CodeAsync(duplicate)).ShouldBe("supplier.tax-id-duplicate");
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiUser.CodeAsync(invalid)).ShouldBe("supplier.tax-id-invalid");
        alphanumeric.StatusCode.ShouldBeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict); // the same fixture may have created it already

        using var byTaxId = await buyer.GetAsync($"{MasterDataRoutes.Suppliers}?taxId={Uri.EscapeDataString(formatted)}");
        (await ApiUser.ReadAsync(byTaxId)).GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(created.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task T_SUP_006_a_ListIsASummary_DetailCarriesContacts_AndReadNeedsSuppliersRead()
    {
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);
        var (supplier, tag) = await CreateDraftAsync(manager);
        var id = supplier.GetProperty("id").GetGuid();
        await CompleteAsync(manager, id, tag);

        using var list = await manager.GetAsync($"{MasterDataRoutes.Suppliers}?status=Draft&q={Uri.EscapeDataString(supplier.GetProperty("legalName").GetString()!)}");
        var summary = (await ApiUser.ReadAsync(list)).GetProperty("items").EnumerateArray().Single();
        summary.TryGetProperty("contacts", out _).ShouldBeFalse();
        summary.TryGetProperty("addresses", out _).ShouldBeFalse();
        using var detail = await manager.GetAsync($"{MasterDataRoutes.Suppliers}/{id}");
        (await ApiUser.ReadAsync(detail)).GetProperty("contacts")[0].GetProperty("email").GetString().ShouldBe("maria@example.invalid");

        var requester = await ApiUser.CreateAsync(factory, SeededRoles.Requester);
        (await requester.GetAsync($"{MasterDataRoutes.Suppliers}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await requester.GetAsync(MasterDataRoutes.Suppliers)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StatusChange_NeedsTheDedicatedPermission_AndAdministratorCannotChangeSupplierData()
    {
        var buyer = await ApiUser.CreateAsync(factory, SeededRoles.Buyer);
        var (supplier, tag) = await CreateDraftAsync(buyer);
        var id = supplier.GetProperty("id").GetGuid();

        using var activate = await buyer.PostAsync($"{MasterDataRoutes.Suppliers}/{id}/activate", null, tag);
        activate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiUser.CodeAsync(activate)).ShouldBe("auth.forbidden");

        var admin = await ApiUser.CreateAsync(factory, SeededRoles.Administrator);
        (await admin.GetAsync($"{MasterDataRoutes.Suppliers}/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsync($"{MasterDataRoutes.Suppliers}/{id}", new { legalName = "X", country = "BR", taxId = supplier.GetProperty("taxId").GetString() }, tag))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Concurrency_StaleSupplierVersion_Is412()
    {
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);
        var (supplier, tag) = await CreateDraftAsync(manager);
        var id = supplier.GetProperty("id").GetGuid();
        (await manager.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/contacts",
            new { contacts = new[] { new { name = "First", email = "first@example.invalid" } } }, tag)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var stale = await manager.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/contacts", new { contacts = Array.Empty<object>() }, tag);

        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ApiUser.ReadAsync(stale)).GetProperty("version").GetString().ShouldBe("2");
    }

    [Fact]
    public async Task T_INT_001_DisabledOrFailingProvider_Is503_AndNeverBlocksManualEntry()
    {
        var buyer = await ApiUser.CreateAsync(factory, SeededRoles.Buyer);
        var cnpj = MasterDataRoutes.NewCnpj();
        factory.Registry.Answer = _ => EnrichmentResult<CompanyRegistrationSuggestion>.Failed(EnrichmentOutcome.Unavailable, "BrasilAPI", "timeout");

        using var unavailable = await buyer.GetAsync($"{MasterDataRoutes.Enrichment}/company-registrations/{cnpj}");
        unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = await ApiUser.ReadAsync(unavailable);
        (problem.GetProperty("code").GetString(), problem.GetProperty("ruleId").GetString()).ShouldBe(("integration.provider-unavailable", "INT-001"));
        using var cep = await buyer.GetAsync($"{MasterDataRoutes.Enrichment}/postal-addresses/01310100");
        cep.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable); // the Test host registers the disabled adapter
        using var invalid = await buyer.GetAsync($"{MasterDataRoutes.Enrichment}/company-registrations/11222333000180");
        (await ApiUser.CodeAsync(invalid)).ShouldBe("supplier.tax-id-invalid");

        using var manual = await buyer.PostAsync(MasterDataRoutes.Suppliers, new { legalName = "Manual entry Ltda", country = "BR", taxId = cnpj });
        manual.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task T_INT_002_a_ProposalIsAdvisory_AndTheAcceptedSnapshotIsPersisted()
    {
        var buyer = await ApiUser.CreateAsync(factory, SeededRoles.Buyer);
        var cnpj = MasterDataRoutes.NewCnpj();
        factory.Registry.Answer = requested => requested == cnpj
            ? EnrichmentResult<CompanyRegistrationSuggestion>.Found("BrasilAPI", DateTime.UtcNow.AddSeconds(-2), new CompanyRegistrationSuggestion(
                cnpj, "ACME COMERCIO LTDA", "ACME", "ATIVA", new PostalAddressSuggestion("01310100", "AVENIDA PAULISTA", "BELA VISTA", "SAO PAULO", "SP", "1000")))
            : EnrichmentResult<CompanyRegistrationSuggestion>.Failed(EnrichmentOutcome.NotFound, "BrasilAPI", "not-found");

        using var lookup = await buyer.GetAsync($"{MasterDataRoutes.Enrichment}/company-registrations/{cnpj}");
        lookup.StatusCode.ShouldBe(HttpStatusCode.OK);
        var proposal = await ApiUser.ReadAsync(lookup);
        proposal.GetProperty("notice").GetString()!.ShouldContain("not a government verification");
        using var notFound = await buyer.GetAsync($"{MasterDataRoutes.Enrichment}/company-registrations/{MasterDataRoutes.NewCnpj()}");
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var (draft, tag) = await buyer.CreateAsync(MasterDataRoutes.Suppliers, new { legalName = "To be enriched", country = "BR", taxId = cnpj });
        var address = proposal.GetProperty("address");
        using var accepted = await buyer.PostAsync($"{MasterDataRoutes.Suppliers}/{draft.GetProperty("id").GetGuid()}/enrichment-acceptances", new
        {
            provider = proposal.GetProperty("provider").GetString(),
            retrievedAt = proposal.GetProperty("retrievedAt").GetDateTime(),
            legalName = "Acme Comércio Ltda", // edited by the user before accepting
            registrationStatus = proposal.GetProperty("registrationStatus").GetString(),
            address = new
            {
                type = "Headquarters", postalCode = address.GetProperty("postalCode").GetString(), street = address.GetProperty("street").GetString(),
                number = address.GetProperty("number").GetString(), district = address.GetProperty("district").GetString(),
                city = address.GetProperty("city").GetString(), state = address.GetProperty("state").GetString(), country = "BR"
            }
        }, tag);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var supplier = await ApiUser.ReadAsync(accepted);
        supplier.GetProperty("legalName").GetString().ShouldBe("Acme Comércio Ltda");
        supplier.GetProperty("addresses").GetArrayLength().ShouldBe(1);
        var enrichment = supplier.GetProperty("enrichment");
        enrichment.GetProperty("provider").GetString().ShouldBe("BrasilAPI");
        enrichment.GetProperty("acceptedFields").EnumerateArray().Select(field => field.GetString()).ShouldBe(["address", "legalName"]);
        enrichment.GetProperty("registrationStatus").GetString().ShouldBe("ATIVA");

        using var unknownProvider = await buyer.PostAsync($"{MasterDataRoutes.Suppliers}/{draft.GetProperty("id").GetGuid()}/enrichment-acceptances",
            new { provider = "Elsewhere", retrievedAt = DateTime.UtcNow, legalName = "X" }, accepted.Headers.ETag!.Tag);
        unknownProvider.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<(JsonElement Supplier, string ETag)> CreateDraftAsync(ApiUser user) =>
        await user.CreateAsync(MasterDataRoutes.Suppliers, new { legalName = $"Synthetic {Guid.NewGuid():N}", tradeName = "Synthetic", country = "BR", taxId = MasterDataRoutes.NewCnpj() });

    /// <summary>Adds a contact with email, an address and a category; returns the latest ETag.</summary>
    private async Task<string> CompleteAsync(ApiUser user, Guid id, string tag)
    {
        var manager = await ApiUser.CreateAsync(factory, SeededRoles.ProcurementManager);
        var (category, _) = await manager.CreateAsync(MasterDataRoutes.Categories, new { code = MasterDataRoutes.NewCode("SUP"), name = "Supplies" });

        using var contacts = await user.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/contacts",
            new { contacts = new[] { new { name = "Maria Synthetic", email = "maria@example.invalid", role = "Sales" } } }, tag);
        contacts.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var addresses = await user.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/addresses", new
        {
            addresses = new[] { new { type = "Headquarters", postalCode = "01310-100", street = "Avenida Paulista", number = "1000", district = "Bela Vista", city = "São Paulo", state = "SP", country = "BR" } }
        }, contacts.Headers.ETag!.Tag);
        addresses.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var categories = await user.PutAsync($"{MasterDataRoutes.Suppliers}/{id}/categories",
            new { categoryIds = new[] { category.GetProperty("id").GetGuid() } }, addresses.Headers.ETag!.Tag);
        categories.StatusCode.ShouldBe(HttpStatusCode.OK);
        return categories.Headers.ETag!.Tag;
    }
}
