using System.Text.Json.Serialization;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Domain.Requisitions;

namespace Template.WebApi.Controllers.v2;

/// <summary>Fiscal period body; dates are local dates (<c>YYYY-MM-DD</c>) in the organization time zone.</summary>
public sealed record FiscalPeriodRequest(string? Code, string? Name, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Allocation body; the amount is a decimal string with at most the currency minor units.</summary>
public sealed record CreateBudgetAllocationRequest(Guid FiscalPeriodId, Guid CostCenterId, string? Currency, string? Amount);

/// <summary>New allocation amount (decimal string) and a 10–500 character reason (BUD-003).</summary>
public sealed record ChangeBudgetAllocationAmountRequest(string? Amount, string? Reason);

/// <summary>Draft header; <c>neededBy</c> is a local date; purpose 10–2,000 characters when present.</summary>
public sealed record RequisitionDraftRequest(Guid CostCenterId, string? Currency, string? Purpose, DateOnly? NeededBy);

/// <summary>
/// Line body; <c>kind</c> is <c>Good</c> or <c>Service</c>; quantity and amount are decimal strings; totals are never
/// accepted from the client (CALC-002).
/// </summary>
public sealed record RequisitionLineBody(
    Guid? CatalogItemId,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<RequisitionLineKind>))] RequisitionLineKind? Kind,
    Guid? CategoryId,
    Guid? UnitOfMeasureId,
    string? Quantity,
    MoneyInput? EstimatedUnitPrice)
{
    public RequisitionLineRequest ToRequest() => new(CatalogItemId, Description, Kind, CategoryId, UnitOfMeasureId, Quantity, EstimatedUnitPrice);
}

/// <summary>Attachment metadata (FIL-002): file name, declared media type and length in bytes; no content.</summary>
public sealed record RequisitionAttachmentRequest(string? FileName, string? MediaType, long Length);
