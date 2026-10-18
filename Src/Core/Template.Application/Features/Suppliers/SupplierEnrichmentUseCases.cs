#nullable enable
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Logging;
using Template.Application.Services.Integrations;
using Template.Application.Services.MasterData;
using Template.Application.Wrappers;
using Template.Domain.Suppliers;

namespace Template.Application.Features.Suppliers;

/// <summary>Advisory proposal returned to the user, who accepts, edits or ignores it (ADR-0007).</summary>
public sealed record CompanyRegistrationProposal(
    string Provider,
    DateTime RetrievedAt,
    string TaxId,
    string? LegalName,
    string? TradeName,
    string? RegistrationStatus,
    PostalAddressProposal? Address,
    string Notice);

public sealed record PostalAddressProposal(
    string Provider, DateTime RetrievedAt, string PostalCode, string? Street, string? Number, string? Complement,
    string? District, string City, string State, string Country);

/// <summary>Looks up a CNPJ at the optional registry provider (INT-001). Permission <c>suppliers.manage</c>.</summary>
public sealed record LookupCompanyRegistrationQuery(string? TaxId) : IRequest<Result<CompanyRegistrationProposal>>;

/// <summary>Looks up a Brazilian postal code (CEP) at the optional provider. Permission <c>suppliers.manage</c>.</summary>
public sealed record LookupPostalCodeQuery(string? PostalCode) : IRequest<Result<PostalAddressProposal>>;

/// <summary>
/// Accepts (possibly edited) enrichment values into a supplier (INT-002). The values go through the ordinary
/// supplier invariants; the snapshot records provider, retrieval instant, accepting user and accepted fields. The
/// provider is never called again for this supplier.
/// </summary>
public sealed record AcceptSupplierEnrichmentCommand(
    Guid Id,
    long? ExpectedVersion,
    string? Provider,
    DateTime? RetrievedAt,
    string? LegalName,
    string? TradeName,
    string? RegistrationStatus,
    SupplierAddressInput? Address) : IRequest<Result<SupplierView>>;

public sealed class AcceptSupplierEnrichmentCommandValidator : AbstractValidator<AcceptSupplierEnrichmentCommand>
{
    public AcceptSupplierEnrichmentCommandValidator()
    {
        RuleFor(command => command.Provider).NotEmpty().Must(provider => EnrichmentProviders.SupplierEnrichment.Contains(provider))
            .WithMessage($"Provider must be one of: {string.Join(", ", EnrichmentProviders.SupplierEnrichment)}.");
        RuleFor(command => command.RetrievedAt).NotNull();
        RuleFor(command => command.LegalName).MaximumLength(Supplier.LegalNameMaxLength);
        RuleFor(command => command.TradeName).MaximumLength(Supplier.TradeNameMaxLength);
        RuleFor(command => command.RegistrationStatus).MaximumLength(SupplierEnrichmentSnapshot.RegistrationStatusMaxLength);
        RuleFor(command => command)
            .Must(command => command.LegalName is not null || command.TradeName is not null || command.Address is not null)
            .WithName("acceptedFields")
            .WithMessage("Accept at least one of legalName, tradeName or address.");
    }
}

public static class EnrichmentErrors
{
    /// <summary>Accepted proposals must be recent; older data is looked up again (freshness, ADR-0007).</summary>
    public static readonly TimeSpan MaxProposalAge = TimeSpan.FromHours(24);

    public const string Notice = "Advisory data from an optional external provider; review it before accepting. It is not a government verification.";

    public static ApplicationError From(EnrichmentOutcome outcome) => outcome switch
    {
        EnrichmentOutcome.NotFound => new ApplicationError(ProblemCodes.NotFound, ErrorCategory.NotFound, "The provider has no record for this key."),
        EnrichmentOutcome.Rejected => new ApplicationError(ProblemCodes.ProviderRejected, ErrorCategory.BusinessRule,
            "The provider rejected the lookup; enter the data manually.", "INT-001"),
        _ => new ApplicationError(ProblemCodes.ProviderUnavailable, ErrorCategory.Unavailable,
            "The optional provider is unavailable; enter the data manually.", "INT-001")
    };
}

public sealed class SupplierEnrichmentHandlers(
    ISupplierRegistryLookup registry,
    IPostalCodeLookup postalCodes,
    ISupplierRepository suppliers,
    MasterDataWriter writer,
    ILogger<SupplierEnrichmentHandlers> logger) :
    IRequestHandler<LookupCompanyRegistrationQuery, Result<CompanyRegistrationProposal>>,
    IRequestHandler<LookupPostalCodeQuery, Result<PostalAddressProposal>>,
    IRequestHandler<AcceptSupplierEnrichmentCommand, Result<SupplierView>>
{
    public async Task<Result<CompanyRegistrationProposal>> Handle(LookupCompanyRegistrationQuery request, CancellationToken cancellationToken)
    {
        var cnpj = TaxIdentifier.Normalize(TaxIdentifier.Brazil, request.TaxId);
        if (cnpj is null)
            return SupplierErrors.TaxIdInvalid.ToApplicationError();

        var result = await registry.LookupAsync(cnpj, cancellationToken);
        // Only the provider and the outcome are logged: never the CNPJ, names or addresses (NFR §9).
        logger.LogInformation(LogEvents.EnrichmentLookupCompleted, "Enrichment lookup {Operation} at {Provider}: {Outcome} ({Reason})",
            "company-registration", result.Provider, result.Outcome, result.Reason ?? "none");
        if (result.Outcome != EnrichmentOutcome.Found)
            return EnrichmentErrors.From(result.Outcome);

        var value = result.Value!;
        var retrievedAt = result.RetrievedAtUtc!.Value;
        return new CompanyRegistrationProposal(
            result.Provider, retrievedAt, value.TaxId, value.LegalName, value.TradeName, value.RegistrationStatus,
            value.Address is null ? null : ToProposal(result.Provider, retrievedAt, value.Address),
            EnrichmentErrors.Notice);
    }

    public async Task<Result<PostalAddressProposal>> Handle(LookupPostalCodeQuery request, CancellationToken cancellationToken)
    {
        var postalCode = Supplier.NormalizePostalCode(TaxIdentifier.Brazil, request.PostalCode);
        if (postalCode is null)
            return ApplicationError.Validation("postalCode", "The postal code must have 8 digits.");

        var result = await postalCodes.LookupAsync(postalCode, cancellationToken);
        logger.LogInformation(LogEvents.EnrichmentLookupCompleted, "Enrichment lookup {Operation} at {Provider}: {Outcome} ({Reason})",
            "postal-code", result.Provider, result.Outcome, result.Reason ?? "none");
        return result.Outcome == EnrichmentOutcome.Found
            ? ToProposal(result.Provider, result.RetrievedAtUtc!.Value, result.Value!)
            : EnrichmentErrors.From(result.Outcome);
    }

    public async Task<Result<SupplierView>> Handle(AcceptSupplierEnrichmentCommand request, CancellationToken cancellationToken)
    {
        var supplier = await suppliers.FindAsync(request.Id, cancellationToken);
        if (supplier is null)
            return SupplierAudit.NotFound;
        if (VersionPrecondition.Check(request.ExpectedVersion, supplier.Version) is { } precondition)
            return precondition;

        var now = writer.UtcNow;
        var retrievedAt = ToUtc(request.RetrievedAt!.Value);
        if (retrievedAt > now || now - retrievedAt > EnrichmentErrors.MaxProposalAge)
            return ApplicationError.Validation("retrievedAt", "The proposal must have been retrieved within the last 24 hours.");

        var accepted = new List<string>();
        if (request.LegalName is not null || request.TradeName is not null)
        {
            var error = supplier.UpdateDetails(
                request.LegalName ?? supplier.LegalName, request.TradeName ?? supplier.TradeName, supplier.Country, supplier.TaxId, now);
            if (error is not null)
                return error.ToApplicationError();
            if (request.LegalName is not null)
                accepted.Add("legalName");
            if (request.TradeName is not null)
                accepted.Add("tradeName");
        }

        string? postalCode = null;
        if (request.Address is not null)
        {
            var (address, error) = supplier.AddAddress(Guid.NewGuid(), request.Address.ToValues(), now);
            if (error is not null)
                return (error with { Field = $"address.{error.Field?.Replace("address.", string.Empty, StringComparison.Ordinal)}" }).ToApplicationError();
            postalCode = address!.PostalCode;
            accepted.Add("address");
        }

        var recorded = supplier.RecordEnrichment(request.Provider, retrievedAt, accepted, request.RegistrationStatus, postalCode, writer.ActorId, now);
        if (recorded is not null)
            return recorded.ToApplicationError();

        writer.Audit(SupplierAudit.EnrichmentAccepted, SupplierAudit.Resource, supplier.Id, ruleIds: ["INT-002"],
            details: new Dictionary<string, string> { ["provider"] = request.Provider!, ["fields"] = string.Join(',', accepted) });
        var commit = await writer.CommitAsync(token => suppliers.GetVersionAsync(supplier.Id, token), null, cancellationToken);
        return commit is null ? SupplierView.From(supplier) : commit;
    }

    private static PostalAddressProposal ToProposal(string provider, DateTime retrievedAt, PostalAddressSuggestion address) => new(
        provider, retrievedAt, address.PostalCode, address.Street, address.Number, address.Complement, address.District,
        address.City, address.State, TaxIdentifier.Brazil);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
