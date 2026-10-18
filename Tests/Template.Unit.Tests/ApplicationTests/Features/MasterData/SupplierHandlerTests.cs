using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Suppliers;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Wrappers;
using Template.Domain.Catalog;
using Template.Domain.Suppliers;

namespace Template.Unit.Tests.ApplicationTests.Features.MasterData;

/// <summary>Supplier and enrichment use cases against port fakes (SUP-001..005, INT-001, INT-002, AUD-001, AUD-003).</summary>
public sealed class SupplierHandlerTests
{
    private const string Cnpj = "11.222.333/0001-81";
    private const string Reason = "Delivery failures in two consecutive months";
    private readonly MasterDataTestDoubles doubles = new();

    private SupplierCommandHandlers Commands => new(doubles.Suppliers, doubles.Categories, doubles.Writer);

    private SupplierEnrichmentHandlers Enrichment => new(
        doubles.Registry, doubles.PostalCodes, doubles.Suppliers, doubles.Writer, MasterDataTestDoubles.Logger<SupplierEnrichmentHandlers>());

    [Fact]
    public async Task T_SUP_001_CreateDraft_NormalizesTheTaxId_AndAuditsWithoutIt()
    {
        var result = await Commands.Handle(new CreateSupplierCommand("Acme Comércio Ltda", "Acme", "br", Cnpj), CancellationToken.None);

        result.Value.Status.ShouldBe("Draft");
        result.Value.TaxId.ShouldBe("11222333000181");
        result.Value.FormattedTaxId.ShouldBe(Cnpj);
        result.Value.MissingForActivation.ShouldBe("contactWithEmail");
        var audit = doubles.Audit.Records.Single();
        (audit.Action, audit.ToState).ShouldBe((SupplierAudit.Created, "Draft"));
        audit.Details.ShouldBeEmpty(); // AUD-003: no tax ID, names, contacts or addresses in details
    }

    [Fact]
    public async Task T_SUP_001_DuplicateTaxId_IsRejected_AndInvalidIsTaxIdInvalid()
    {
        await Commands.Handle(new CreateSupplierCommand("Acme", null, "BR", Cnpj), CancellationToken.None);

        var duplicate = await Commands.Handle(new CreateSupplierCommand("Other", null, "BR", "11222333000181"), CancellationToken.None);
        var invalid = await Commands.Handle(new CreateSupplierCommand("Other", null, "BR", "11.222.333/0001-80"), CancellationToken.None);
        var otherCountry = await Commands.Handle(new CreateSupplierCommand("Other", null, "PT", "11222333000181"), CancellationToken.None);

        duplicate.Error!.Code.ShouldBe(ProblemCodes.SupplierTaxIdDuplicate);
        duplicate.Error.Category.ShouldBe(ErrorCategory.Conflict);
        invalid.Error!.Code.ShouldBe(ProblemCodes.SupplierTaxIdInvalid);
        invalid.Error.Category.ShouldBe(ErrorCategory.Validation);
        invalid.Error.FieldErrors!.Keys.ShouldBe(["taxId"]);
        otherCountry.IsSuccess.ShouldBeTrue(); // unique per country
    }

    [Fact]
    public async Task T_SUP_001_b_UniqueIndexRace_IsTheDuplicateError()
    {
        doubles.UnitOfWork.UniqueViolation = ISupplierRepository.TaxIdUniqueKey;

        var result = await Commands.Handle(new CreateSupplierCommand("Acme", null, "BR", Cnpj), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.SupplierTaxIdDuplicate);
    }

    [Fact]
    public async Task T_SUP_002_FullActivationFlow_WithAuditPerChange()
    {
        var supplier = await CompleteDraftAsync();

        var activated = await Commands.Handle(new ChangeSupplierStatusCommand(supplier.Id, long.Parse(supplier.Version), SupplierStatusTransition.Activate, null), CancellationToken.None);

        activated.Value.Status.ShouldBe("Active");
        activated.Value.IsEligibleForNewCommitments.ShouldBeTrue();
        activated.Value.StatusHistory.Single().To.ShouldBe("Active");
        doubles.Audit.Records.Select(record => record.Action).ShouldBe([
            SupplierAudit.Created, SupplierAudit.ContactsChanged, SupplierAudit.AddressesChanged, SupplierAudit.CategoriesChanged, SupplierAudit.Activated]);
        var audit = doubles.Audit.Records.Last();
        (audit.FromState, audit.ToState).ShouldBe(("Draft", "Active"));
        audit.RuleIds.ShouldBe(["SUP-002"]);
        doubles.Audit.Records.ShouldAllBe(record => !record.Details.Values.Any(value => value.Contains('@')));
    }

    [Fact]
    public async Task T_SUP_002_IncompleteActivation_Is422WithTheMissingElement()
    {
        var draft = (await Commands.Handle(new CreateSupplierCommand("Acme", null, "BR", Cnpj), CancellationToken.None)).Value;

        var result = await Commands.Handle(new ChangeSupplierStatusCommand(draft.Id, 1, SupplierStatusTransition.Activate, null), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.SupplierActivationIncomplete);
        result.Error.Category.ShouldBe(ErrorCategory.BusinessRule);
        result.Error.Extensions!["missing"].ShouldBe("contactWithEmail");
        doubles.UnitOfWork.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task T_SUP_005_SuspendWithoutReason_Is400_AndInvalidTransitionIs409WithCurrentState()
    {
        var supplier = await ActiveSupplierAsync();
        var version = long.Parse(supplier.Version);

        var noReason = await Commands.Handle(new ChangeSupplierStatusCommand(supplier.Id, version, SupplierStatusTransition.Suspend, " "), CancellationToken.None);
        var suspended = await Commands.Handle(new ChangeSupplierStatusCommand(supplier.Id, version, SupplierStatusTransition.Suspend, Reason), CancellationToken.None);
        var again = await Commands.Handle(new ChangeSupplierStatusCommand(supplier.Id, version + 1, SupplierStatusTransition.Suspend, Reason), CancellationToken.None);

        noReason.Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);
        suspended.Value.Status.ShouldBe("Suspended");
        suspended.Value.IsEligibleForNewCommitments.ShouldBeFalse();
        doubles.Audit.Records.Last().Reason.ShouldBe(Reason);
        doubles.Audit.Records.Last().RuleIds.ShouldBe(["SUP-004", "SUP-005"]);
        again.Error!.Code.ShouldBe(ProblemCodes.InvalidTransition);
        again.Error.Category.ShouldBe(ErrorCategory.Conflict);
        again.Error.Extensions!["currentState"].ShouldBe("Suspended");
    }

    [Fact]
    public async Task StatusChange_RequiresTheCurrentVersion()
    {
        var supplier = await ActiveSupplierAsync();

        (await Commands.Handle(new ChangeSupplierStatusCommand(supplier.Id, null, SupplierStatusTransition.Suspend, Reason), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.PreconditionRequired);
        (await Commands.Handle(new ChangeSupplierStatusCommand(supplier.Id, 1, SupplierStatusTransition.Suspend, Reason), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
    }

    [Fact]
    public async Task Categories_MustExist_AndNewOnesBeActive()
    {
        var draft = (await Commands.Handle(new CreateSupplierCommand("Acme", null, "BR", Cnpj), CancellationToken.None)).Value;
        var inactive = Category.Create(Guid.NewGuid(), "OLD", "Old", null, MasterDataTestDoubles.Now).Category!;
        inactive.Deactivate("Replaced by a new category", MasterDataTestDoubles.Now);
        doubles.Categories.Add(inactive);

        (await Commands.Handle(new SetSupplierCategoriesCommand(draft.Id, 1, [Guid.NewGuid()]), CancellationToken.None))
            .Error!.FieldErrors!.Keys.ShouldBe(["categoryIds"]);
        (await Commands.Handle(new SetSupplierCategoriesCommand(draft.Id, 1, [inactive.Id]), CancellationToken.None))
            .Error!.Code.ShouldBe(ProblemCodes.CatalogInactive);
    }

    [Fact]
    public async Task UpdateTaxId_OfDraft_ChecksUniqueness()
    {
        await Commands.Handle(new CreateSupplierCommand("Acme", null, "BR", Cnpj), CancellationToken.None);
        var other = (await Commands.Handle(new CreateSupplierCommand("Other", null, "BR", "12.ABC.345/01DE-35"), CancellationToken.None)).Value;

        var result = await Commands.Handle(new UpdateSupplierCommand(other.Id, 1, "Other", null, "BR", Cnpj), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.SupplierTaxIdDuplicate);
    }

    [Theory]
    [InlineData("Active,suspended", null)]
    [InlineData("Unknown", "status")]
    public async Task List_ParsesStatusesAndTaxIds(string status, string? errorField)
    {
        var handler = new SupplierQueryHandlers(doubles.Suppliers);

        var result = await handler.Handle(new ListSuppliersQuery(Status: status, TaxId: Cnpj), CancellationToken.None);

        if (errorField is null)
        {
            result.IsSuccess.ShouldBeTrue();
            doubles.Suppliers.LastFilter!.Statuses.ShouldBe([SupplierStatus.Active, SupplierStatus.Suspended]);
            (doubles.Suppliers.LastFilter.Country, doubles.Suppliers.LastFilter.TaxId).ShouldBe(("BR", "11222333000181"));
        }
        else
        {
            result.Error!.FieldErrors!.Keys.ShouldBe([errorField]);
        }
    }

    [Fact]
    public async Task List_RejectsInvalidTaxIdAndCountry()
    {
        var handler = new SupplierQueryHandlers(doubles.Suppliers);

        (await handler.Handle(new ListSuppliersQuery(TaxId: "123"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["taxId"]);
        (await handler.Handle(new ListSuppliersQuery(Country: "BRA"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["country"]);
    }

    [Theory]
    [InlineData(EnrichmentOutcome.Unavailable, ProblemCodes.ProviderUnavailable, ErrorCategory.Unavailable)]
    [InlineData(EnrichmentOutcome.Invalid, ProblemCodes.ProviderUnavailable, ErrorCategory.Unavailable)]
    [InlineData(EnrichmentOutcome.Rejected, ProblemCodes.ProviderRejected, ErrorCategory.BusinessRule)]
    [InlineData(EnrichmentOutcome.NotFound, ProblemCodes.NotFound, ErrorCategory.NotFound)]
    public async Task T_INT_001_ProviderFailures_HaveExplicitOutcomes(EnrichmentOutcome outcome, string code, ErrorCategory category)
    {
        doubles.Registry.Result = EnrichmentResult<CompanyRegistrationSuggestion>.Failed(outcome, "BrasilAPI", "synthetic");

        var result = await Enrichment.Handle(new LookupCompanyRegistrationQuery(Cnpj), CancellationToken.None);

        result.Error!.Code.ShouldBe(code);
        result.Error.Category.ShouldBe(category);
    }

    [Fact]
    public async Task T_INT_001_InvalidCnpj_NeverReachesTheProvider()
    {
        var result = await Enrichment.Handle(new LookupCompanyRegistrationQuery("11.222.333/0001-80"), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.SupplierTaxIdInvalid);
        doubles.Registry.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task T_INT_001_ProviderUnavailable_NeverBlocksManualEntry()
    {
        doubles.Registry.Result = EnrichmentResult<CompanyRegistrationSuggestion>.Failed(EnrichmentOutcome.Unavailable, "BrasilAPI", "timeout");
        (await Enrichment.Handle(new LookupCompanyRegistrationQuery(Cnpj), CancellationToken.None)).IsSuccess.ShouldBeFalse();

        var supplier = await ActiveSupplierAsync();

        supplier.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Lookup_ReturnsAnAdvisoryProposal_WithProvenance()
    {
        var retrieved = MasterDataTestDoubles.Now.AddSeconds(-1);
        doubles.Registry.Result = EnrichmentResult<CompanyRegistrationSuggestion>.Found("BrasilAPI", retrieved, new CompanyRegistrationSuggestion(
            "11222333000181", "ACME COMERCIO LTDA", "ACME", "ATIVA", new PostalAddressSuggestion("01310100", "AVENIDA PAULISTA", "BELA VISTA", "SAO PAULO", "SP", "1000")));

        var result = await Enrichment.Handle(new LookupCompanyRegistrationQuery(Cnpj), CancellationToken.None);

        result.Value.Provider.ShouldBe("BrasilAPI");
        result.Value.RetrievedAt.ShouldBe(retrieved);
        result.Value.Address!.Country.ShouldBe("BR");
        result.Value.Notice.ShouldContain("not a government verification");
        doubles.Registry.Calls.ShouldBe(["11222333000181"]);
        doubles.Audit.Records.ShouldBeEmpty(); // a lookup changes nothing
    }

    [Fact]
    public async Task T_INT_002_a_AcceptedEnrichment_IsPersistedAsSnapshot_ThroughTheInvariants()
    {
        var draft = (await Commands.Handle(new CreateSupplierCommand("Acme", null, "BR", Cnpj), CancellationToken.None)).Value;
        var retrieved = MasterDataTestDoubles.Now.AddMinutes(-10);

        var result = await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(
            draft.Id, 1, "BrasilAPI", retrieved, "Acme Comércio Ltda", "Acme (edited)", "ATIVA",
            new SupplierAddressInput(null, SupplierAddressType.Headquarters, "01310-100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR")),
            CancellationToken.None);

        result.Value.LegalName.ShouldBe("Acme Comércio Ltda");
        result.Value.TradeName.ShouldBe("Acme (edited)");
        result.Value.Addresses.Single().PostalCode.ShouldBe("01310100");
        result.Value.Enrichment!.Provider.ShouldBe("BrasilAPI");
        result.Value.Enrichment.RetrievedAt.ShouldBe(retrieved);
        result.Value.Enrichment.AcceptedBy.ShouldBe(doubles.User.Id);
        result.Value.Enrichment.AcceptedFields.ShouldBe(["address", "legalName", "tradeName"]);
        var audit = doubles.Audit.Records.Last();
        audit.Action.ShouldBe(SupplierAudit.EnrichmentAccepted);
        audit.Details.ShouldBe(new Dictionary<string, string> { ["provider"] = "BrasilAPI", ["fields"] = "legalName,tradeName,address" });
        doubles.Registry.Calls.ShouldBeEmpty(); // nothing is read live (INT-002)
    }

    [Fact]
    public async Task T_INT_002_StaleOrFutureProposals_AreRejected()
    {
        var draft = (await Commands.Handle(new CreateSupplierCommand("Acme", null, "BR", Cnpj), CancellationToken.None)).Value;

        var stale = await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(draft.Id, 1, "BrasilAPI", MasterDataTestDoubles.Now.AddHours(-25), "Acme", null, null, null), CancellationToken.None);
        var future = await Enrichment.Handle(new AcceptSupplierEnrichmentCommand(draft.Id, 1, "BrasilAPI", MasterDataTestDoubles.Now.AddMinutes(1), "Acme", null, null, null), CancellationToken.None);

        stale.Error!.FieldErrors!.Keys.ShouldBe(["retrievedAt"]);
        future.Error!.FieldErrors!.Keys.ShouldBe(["retrievedAt"]);
    }

    [Fact]
    public void AcceptEnrichmentValidator_RequiresAKnownProviderAndOneField()
    {
        var errors = new AcceptSupplierEnrichmentCommandValidator()
            .Validate(new AcceptSupplierEnrichmentCommand(Guid.NewGuid(), 1, "Unknown", null, null, null, null, null)).Errors;

        errors.Select(error => error.PropertyName).ShouldBe(["Provider", "RetrievedAt", "acceptedFields"], ignoreOrder: true);
    }

    [Fact]
    public async Task PostalCodeLookup_ValidatesTheCep()
    {
        (await Enrichment.Handle(new LookupPostalCodeQuery("1234"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["postalCode"]);
        (await Enrichment.Handle(new LookupPostalCodeQuery("01310-100"), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.ProviderUnavailable);
    }

    private async Task<SupplierView> CompleteDraftAsync()
    {
        var category = Category.Create(Guid.NewGuid(), "OFFICE", "Office", null, MasterDataTestDoubles.Now).Category!;
        doubles.Categories.Add(category);
        var draft = (await Commands.Handle(new CreateSupplierCommand("Acme Comércio Ltda", "Acme", "BR", Cnpj), CancellationToken.None)).Value;
        var contacts = await Commands.Handle(new ReplaceSupplierContactsCommand(draft.Id, 1,
            [new SupplierContactInput(null, "Maria Synthetic", "maria@example.invalid", null, "Sales")]), CancellationToken.None);
        var addresses = await Commands.Handle(new ReplaceSupplierAddressesCommand(draft.Id, long.Parse(contacts.Value.Version),
            [new SupplierAddressInput(null, SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR")]),
            CancellationToken.None);
        var categories = await Commands.Handle(new SetSupplierCategoriesCommand(draft.Id, long.Parse(addresses.Value.Version), [category.Id]), CancellationToken.None);
        return categories.Value;
    }

    private async Task<SupplierView> ActiveSupplierAsync()
    {
        var supplier = await CompleteDraftAsync();
        return (await Commands.Handle(new ChangeSupplierStatusCommand(supplier.Id, long.Parse(supplier.Version), SupplierStatusTransition.Activate, null), CancellationToken.None)).Value;
    }
}
