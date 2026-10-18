#nullable enable
using System.Reflection;

namespace Template.Application.Security;

/// <summary>
/// Stable permission names from docs/product/permissions.md §2 (SEC-008). Endpoints authorize these names through
/// policies of the same name; roles only group them. They are distinct from business-rule IDs (BUD-*, REQ-*, ...).
/// </summary>
public static class Permissions
{
    public const string AccessUsersManage = "access.users.manage";
    public const string AccessRolesManage = "access.roles.manage";
    public const string AuditRead = "audit.read";
    public const string OrganizationSettingsManage = "organization.settings.manage";
    public const string OrganizationManage = "organization.manage";
    public const string OrganizationRead = "organization.read";
    public const string SuppliersRead = "suppliers.read";
    public const string SuppliersManage = "suppliers.manage";
    public const string SuppliersStatusChange = "suppliers.status.change";
    public const string CatalogRead = "catalog.read";
    public const string CatalogManage = "catalog.manage";
    public const string BudgetRead = "budget.read";
    public const string BudgetPeriodsManage = "budget.periods.manage";
    public const string BudgetAllocationsManage = "budget.allocations.manage";
    public const string RequisitionsCreate = "requisitions.create";
    public const string RequisitionsSubmit = "requisitions.submit";
    public const string RequisitionsEditAny = "requisitions.edit-any";
    public const string RequisitionsCancelOwn = "requisitions.cancel-own";
    public const string RequisitionsCancelAny = "requisitions.cancel-any";
    public const string RequisitionsReadTeam = "requisitions.read-team";
    public const string RequisitionsReadAll = "requisitions.read-all";
    public const string SourcingRfqsManage = "sourcing.rfqs.manage";
    public const string SourcingRfqsCloseEarly = "sourcing.rfqs.close-early";
    public const string SourcingQuotesEnter = "sourcing.quotes.enter";
    public const string SourcingQuotesRead = "sourcing.quotes.read";
    public const string SourcingAwardsSubmit = "sourcing.awards.submit";
    public const string SourcingWaiversLowValue = "sourcing.waivers.low-value";
    public const string SourcingWaiversDirectPurchase = "sourcing.waivers.direct-purchase";
    public const string SourcingWaiversInsufficientQuotations = "sourcing.waivers.insufficient-quotations";
    public const string SourcingWaiversNonLeading = "sourcing.waivers.non-leading";
    public const string SourcingRatesManual = "sourcing.rates.manual";
    public const string ApprovalsDecide = "approvals.decide";
    public const string ApprovalsDelegate = "approvals.delegate";
    public const string ApprovalsAdminister = "approvals.administer";
    public const string ApprovalsReadAll = "approvals.read-all";
    public const string ApprovalsPoliciesManage = "approvals.policies.manage";
    public const string PurchasingOrdersRead = "purchasing.orders.read";
    public const string PurchasingOrdersManage = "purchasing.orders.manage";
    public const string PurchasingOrdersIssue = "purchasing.orders.issue";
    public const string PurchasingOrdersCancel = "purchasing.orders.cancel";
    public const string PurchasingOrdersShortClose = "purchasing.orders.short-close";
    public const string ReceivingReceiptsCreate = "receiving.receipts.create";
    public const string ReceivingReceiptsCorrect = "receiving.receipts.correct";
    public const string ReceivingReceiptsRead = "receiving.receipts.read";
    public const string ReportsRequest = "reports.request";
    public const string ReportsReadAll = "reports.read-all";
    public const string FilesUpload = "files.upload";

    /// <summary>Claim type under which role permissions are stored by the Identity adapter.</summary>
    public const string ClaimType = "permission";

    /// <summary>Every catalog entry, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } = typeof(Permissions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.Name != nameof(ClaimType))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsKnown(string? permission) => permission is not null && Known.Contains(permission);
}
