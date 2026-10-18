using Template.Domain.Common;

namespace Template.Domain.Catalog;

/// <summary>Stable rejections of the Catalog module (CAT-*).</summary>
public static class CatalogErrors
{
    public const string CodeDuplicateCode = "catalog.code-duplicate";
    public const string InactiveCode = "catalog.inactive";

    public static readonly DomainError InvalidName = new(
        "validation", DomainErrorKind.Validation, $"The name must have 1 to {MasterDataRules.NameMaxLength} characters.", Field: "name");

    public static DomainError InvalidCode(string pattern) => new(
        "validation", DomainErrorKind.Validation, $"The code must match {pattern}.", "CAT-001", "code");

    public static DomainError Inactive(string resource) => new(
        InactiveCode, DomainErrorKind.BusinessRule, $"The {resource} is inactive.", "CAT-002");
}
