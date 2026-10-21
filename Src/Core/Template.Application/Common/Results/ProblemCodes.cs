#nullable enable
namespace Template.Application.Common.Results;

/// <summary>
/// Stable Problem Details <c>code</c> values (docs/product/api-guidelines.md §5). Clients branch on status and code
/// only; never rename a published value.
/// </summary>
public static class ProblemCodes
{
    public const string Validation = "validation";
    public const string NotFound = "not-found";
    public const string InvalidTransition = "invalid-transition";

    public const string InvalidCredentials = "auth.invalid-credentials";
    public const string SessionExpired = "auth.session-expired";
    public const string SessionRevoked = "auth.session-revoked";
    public const string RefreshInProgress = "auth.refresh-in-progress";
    public const string CsrfInvalid = "auth.csrf-invalid";
    public const string Forbidden = "auth.forbidden";
    public const string RateLimited = "auth.rate-limited";

    public const string VersionMismatch = "concurrency.version-mismatch";
    public const string PreconditionRequired = "concurrency.precondition-required";

    public const string IdempotencyKeyRequired = "idempotency.key-required";
    public const string IdempotencyKeyReused = "idempotency.key-reused";
    public const string IdempotencyInProgress = "idempotency.in-progress";

    // Phase 25: master data (ORG-*, SUP-*, CAT-*) and optional enrichment (INT-001).
    public const string ReasonRequired = "reason-required";
    public const string OrganizationCodeDuplicate = "organization.code-duplicate";
    public const string OrganizationInactive = "organization.inactive";
    public const string DepartmentHasActiveCostCenters = "organization.department-has-active-cost-centers";
    public const string OrganizationSettingsLocked = "organization.settings-locked";
    public const string SupplierTaxIdInvalid = "supplier.tax-id-invalid";
    public const string SupplierTaxIdDuplicate = "supplier.tax-id-duplicate";
    public const string SupplierActivationIncomplete = "supplier.activation-incomplete";
    public const string SupplierNotEligible = "supplier.not-eligible";
    public const string CatalogCodeDuplicate = "catalog.code-duplicate";
    public const string CatalogInactive = "catalog.inactive";
    public const string ProviderUnavailable = "integration.provider-unavailable";
    public const string ProviderRejected = "integration.provider-rejected";

    // Phase 27: budgeting (BUD-*), requisitions (REQ-*) and attachment metadata (FIL-002/004).
    public const string BudgetPeriodOverlap = "budget.period-overlap";
    public const string BudgetPeriodCodeDuplicate = "budget.period-code-duplicate";
    public const string BudgetPeriodClosed = "budget.period-closed";
    public const string BudgetPeriodHasOpenReservations = "budget.period-has-open-reservations";
    public const string BudgetAllocationDuplicate = "budget.allocation-duplicate";
    public const string BudgetAllocationHasOpenReservations = "budget.allocation-has-open-reservations";
    public const string BudgetBelowReserved = "budget.below-reserved";
    public const string BudgetNoOpenAllocation = "budget.no-open-allocation";
    public const string BudgetInsufficient = "budget.insufficient";
    public const string BudgetReservationExists = "budget.reservation-exists";
    public const string RequisitionNotSubmittable = "requisition.not-submittable";
    public const string RequisitionImmutable = "requisition.immutable";
    public const string FileTooLarge = "file.too-large";
    public const string FileTypeNotAllowed = "file.type-not-allowed";
    public const string FileLimitReached = "file.limit-reached";
    public const string FileImmutable = "file.immutable";
}
