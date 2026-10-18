#nullable enable
namespace Template.Application.Interfaces.Integrations;

/// <summary>Explicit result kinds of an optional provider call (ADR-0007, INT-001).</summary>
public enum EnrichmentOutcome
{
    /// <summary>The provider answered with data that passed validation and normalization.</summary>
    Found,

    /// <summary>The provider answered that the key does not exist.</summary>
    NotFound,

    /// <summary>Disabled, unreachable, timed out, circuit open, rate limited (429) or 5xx.</summary>
    Unavailable,

    /// <summary>The provider refused the request (4xx other than 404/429).</summary>
    Rejected,

    /// <summary>The provider answered with a body that does not match the expected schema.</summary>
    Invalid
}

/// <summary>
/// Provider-neutral answer. <see cref="Value"/> is set only for <see cref="EnrichmentOutcome.Found"/>;
/// <see cref="Reason"/> is a short safe classification (for example <c>timeout</c>, <c>rate-limited</c>,
/// <c>circuit-open</c>, <c>disabled</c>), never provider text or request data.
/// </summary>
public sealed record EnrichmentResult<T>(
    EnrichmentOutcome Outcome,
    string Provider,
    DateTime? RetrievedAtUtc,
    T? Value = default,
    string? Reason = null)
    where T : class
{
    public static EnrichmentResult<T> Found(string provider, DateTime retrievedAtUtc, T value) =>
        new(EnrichmentOutcome.Found, provider, retrievedAtUtc, value);

    public static EnrichmentResult<T> Failed(EnrichmentOutcome outcome, string provider, string reason) =>
        outcome == EnrichmentOutcome.Found
            ? throw new ArgumentException("Use Found for successful results.", nameof(outcome))
            : new(outcome, provider, null, null, reason);
}

/// <summary>Address suggested by a provider; every field is already normalized (CEP 8 digits, state 2 letters).</summary>
public sealed record PostalAddressSuggestion(
    string PostalCode,
    string? Street,
    string? District,
    string City,
    string State,
    string? Number = null,
    string? Complement = null);

/// <summary>Company registration data suggested for a CNPJ (advisory only, never a legal verification).</summary>
public sealed record CompanyRegistrationSuggestion(
    string TaxId,
    string? LegalName,
    string? TradeName,
    string? RegistrationStatus,
    PostalAddressSuggestion? Address);

/// <summary>One PTAX bulletin rate of a foreign currency, in BRL per foreign unit.</summary>
public sealed record ExchangeRateBulletin(
    string Currency,
    DateOnly QuoteDate,
    decimal BuyRate,
    decimal SellRate,
    string BulletinType,
    bool IsClosing);

/// <summary>Supplier registry lookup by CNPJ (BrasilAPI in Infrastructure).</summary>
public interface ISupplierRegistryLookup
{
    /// <param name="cnpj">A normalized, structurally valid CNPJ (14 characters).</param>
    Task<EnrichmentResult<CompanyRegistrationSuggestion>> LookupAsync(string cnpj, CancellationToken cancellationToken);
}

/// <summary>Brazilian postal-code lookup (BrasilAPI in Infrastructure).</summary>
public interface IPostalCodeLookup
{
    /// <param name="postalCode">A normalized CEP (8 digits).</param>
    Task<EnrichmentResult<PostalAddressSuggestion>> LookupAsync(string postalCode, CancellationToken cancellationToken);
}

/// <summary>PTAX bulletins of the Banco Central do Brasil (spike SP-5), used by quote comparison in Phase 29.</summary>
public interface IExchangeRateProvider
{
    /// <summary>Every bulletin of <paramref name="currency"/> published between the two local dates (inclusive).</summary>
    Task<EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>> GetPtaxBulletinsAsync(
        string currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}
