#nullable enable
using System.Globalization;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Results;
using Template.Application.Wrappers;
using Template.Domain.Catalog;

namespace Template.Application.Features.Catalog;

public sealed record CategoryView(Guid Id, string Code, string Name, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt, string Version)
{
    public static CategoryView From(Category category) => new(
        category.Id, category.Code, category.Name, category.IsActive, category.CreatedAtUtc, category.UpdatedAtUtc,
        VersionPrecondition.Format(category.Version));
}

public sealed record UnitOfMeasureView(
    Guid Id, string Code, string Name, int AllowedDecimals, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt, string Version)
{
    public static UnitOfMeasureView From(UnitOfMeasure unit) => new(
        unit.Id, unit.Code, unit.Name, unit.AllowedDecimals, unit.IsActive, unit.CreatedAtUtc, unit.UpdatedAtUtc,
        VersionPrecondition.Format(unit.Version));
}

/// <summary>Money on the wire: the amount is a JSON string to avoid binary floating point (api-guidelines §2).</summary>
public sealed record MoneyView(string Amount, string Currency)
{
    public static MoneyView? From(decimal? amount, string? currency) =>
        amount is null || currency is null ? null : new(amount.Value.ToString(CultureInfo.InvariantCulture), currency);
}

public sealed record CatalogItemView(
    Guid Id,
    string Code,
    string Description,
    string Kind,
    Guid CategoryId,
    Guid UnitOfMeasureId,
    MoneyView? ReferencePrice,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Version)
{
    public static CatalogItemView From(CatalogItem item) => new(
        item.Id, item.Code, item.Description, item.Kind.ToString(), item.CategoryId, item.UnitOfMeasureId,
        MoneyView.From(item.ReferencePriceAmount, item.ReferencePriceCurrency), item.IsActive, item.CreatedAtUtc,
        item.UpdatedAtUtc, VersionPrecondition.Format(item.Version));
}

/// <summary>Audit actions and failures of the Catalog module (CAT-*).</summary>
public static class CatalogAudit
{
    public const string CategoryResource = "category";
    public const string UnitResource = "unit-of-measure";
    public const string ItemResource = "catalog-item";

    public const string CategoryCreated = "catalog.category.created";
    public const string CategoryUpdated = "catalog.category.updated";
    public const string CategoryDeactivated = "catalog.category.deactivated";
    public const string CategoryReactivated = "catalog.category.reactivated";
    public const string UnitCreated = "catalog.unit.created";
    public const string UnitUpdated = "catalog.unit.updated";
    public const string UnitDeactivated = "catalog.unit.deactivated";
    public const string UnitReactivated = "catalog.unit.reactivated";
    public const string ItemCreated = "catalog.item.created";
    public const string ItemUpdated = "catalog.item.updated";
    public const string ItemDeactivated = "catalog.item.deactivated";
    public const string ItemReactivated = "catalog.item.reactivated";

    public static readonly ApplicationError CodeDuplicate = new(
        ProblemCodes.CatalogCodeDuplicate, ErrorCategory.Conflict, "Another record already uses this code.", "CAT-001");

    public static ApplicationError NotFound(string resource) =>
        new(ProblemCodes.NotFound, ErrorCategory.NotFound, $"The {resource} was not found.");

    /// <summary>Parses the wire money amount (string, invariant culture); <c>null</c> input stays <c>null</c>.</summary>
    public static bool TryParseAmount(string? value, out decimal? amount)
    {
        amount = null;
        if (value is null)
            return true;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            return false;
        amount = parsed;
        return true;
    }
}
