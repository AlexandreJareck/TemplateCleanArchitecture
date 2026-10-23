namespace Template.WebApi.Controllers.v2;

/// <summary>
/// Report request (RPT-001/009). <c>reportType</c> is one of <c>RequisitionPdf</c> (<c>requisitionId</c>),
/// <c>QuoteComparisonPdf</c> (<c>rfqId</c>), <c>BudgetConsumptionXlsx</c> (<c>fiscalPeriodId</c>, optional <c>currency</c>
/// and <c>costCenterId</c>) or <c>SupplierQuotesXlsx</c> (<c>from</c>, <c>to</c> local dates, at most 366 days, optional
/// <c>supplierId</c>). Parameters of other types are rejected; templates, HTML and scripts are never accepted (RPT-008).
/// </summary>
public sealed record RequestReportBody(
    string? ReportType,
    Guid? RequisitionId = null,
    Guid? RfqId = null,
    Guid? FiscalPeriodId = null,
    Guid? CostCenterId = null,
    string? Currency = null,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? SupplierId = null);
