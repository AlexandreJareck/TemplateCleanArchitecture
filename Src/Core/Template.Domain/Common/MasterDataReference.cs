using System;

namespace Template.Domain.Common;

/// <summary>
/// Identity and active state of master data owned by another module, so an aggregate can enforce "new references
/// must be active" without referencing that module's Domain types (ADR-0001).
/// </summary>
public sealed record MasterDataReference(Guid Id, bool IsActive)
{
    /// <summary>Same value as <c>CatalogErrors.InactiveCode</c> (CAT-002).</summary>
    public const string InactiveCatalogCode = "catalog.inactive";
}
