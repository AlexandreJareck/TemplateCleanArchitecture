using Template.Domain.Common;

namespace Template.Domain.Suppliers;

/// <summary>Stable rejections of the Suppliers module (SUP-*).</summary>
public static class SupplierErrors
{
    public const string TaxIdInvalidCode = "supplier.tax-id-invalid";
    public const string TaxIdDuplicateCode = "supplier.tax-id-duplicate";
    public const string ActivationIncompleteCode = "supplier.activation-incomplete";
    public const string NotEligibleCode = "supplier.not-eligible";

    public static readonly DomainError TaxIdInvalid = new(
        TaxIdInvalidCode, DomainErrorKind.Validation,
        "The tax identifier is structurally invalid for the country (structural check only, not a government verification).",
        "SUP-001", "taxId");

    public static readonly DomainError CountryInvalid = new(
        "validation", DomainErrorKind.Validation, "The country must be an ISO 3166-1 alpha-2 code.", Field: "country");

    public static readonly DomainError NotEligible = new(
        NotEligibleCode, DomainErrorKind.BusinessRule, "Only Active suppliers are eligible for new commitments.", "SUP-003");

    public static DomainError ActivationIncomplete(string missing) => new(
        ActivationIncompleteCode, DomainErrorKind.BusinessRule,
        $"An active supplier needs a legal name, a tax ID, a contact with email, an address and a category; missing: {missing}.",
        "SUP-002", Details: new System.Collections.Generic.Dictionary<string, string> { ["missing"] = missing });

    public static DomainError Validation(string field, string message, string? ruleId = null) =>
        new("validation", DomainErrorKind.Validation, message, ruleId, field);

    public static DomainError NotFound(string what) =>
        new("not-found", DomainErrorKind.NotFound, $"The {what} does not belong to this supplier.");
}
