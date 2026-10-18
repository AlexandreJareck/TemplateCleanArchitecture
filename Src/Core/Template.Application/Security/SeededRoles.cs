#nullable enable
using static Template.Application.Security.Permissions;

namespace Template.Application.Security;

/// <summary>
/// Policy-version-1 role → permission matrix from docs/product/permissions.md §3 (B23-9, B23-10). Roles are editable
/// data: the Identity adapter creates a role with these permissions only when the role does not exist yet, so later
/// edits are never overwritten. <see cref="Administrator"/> deliberately holds no procurement decision permission.
/// </summary>
public static class SeededRoles
{
    public const string Requester = "Requester";
    public const string CostCenterManager = "CostCenterManager";
    public const string DepartmentHead = "DepartmentHead";
    public const string FinanceApprover = "FinanceApprover";
    public const string BudgetController = "BudgetController";
    public const string Buyer = "Buyer";
    public const string ProcurementManager = "ProcurementManager";
    public const string Receiver = "Receiver";
    public const string Auditor = "Auditor";
    public const string Administrator = "Administrator";

    public static IReadOnlyList<string> All { get; } =
    [
        Requester, CostCenterManager, DepartmentHead, FinanceApprover, BudgetController,
        Buyer, ProcurementManager, Receiver, Auditor, Administrator
    ];

    private static readonly string[] Requesting =
        [Requester, CostCenterManager, DepartmentHead, FinanceApprover, BudgetController, Buyer, ProcurementManager, Receiver];

    private static readonly (string[] Permissions, string[] Roles)[] Matrix =
    [
        ([AccessUsersManage, AccessRolesManage], [Administrator]),
        ([AuditRead], [Auditor, Administrator]),
        ([OrganizationSettingsManage, OrganizationManage], [Administrator]),
        ([OrganizationRead, CatalogRead], [.. All]),
        ([SuppliersRead], [Buyer, ProcurementManager, Auditor, Administrator]),
        ([SuppliersManage], [Buyer, ProcurementManager]),
        ([SuppliersStatusChange], [ProcurementManager]),
        ([CatalogManage], [ProcurementManager, Administrator]),
        ([BudgetRead], [CostCenterManager, DepartmentHead, FinanceApprover, BudgetController, ProcurementManager, Auditor]),
        ([BudgetPeriodsManage, BudgetAllocationsManage], [BudgetController]),
        ([RequisitionsCreate, RequisitionsSubmit, RequisitionsCancelOwn], Requesting),
        ([RequisitionsEditAny, RequisitionsCancelAny], [ProcurementManager]),
        ([RequisitionsReadTeam], [CostCenterManager, DepartmentHead]),
        ([RequisitionsReadAll], [FinanceApprover, BudgetController, Buyer, ProcurementManager, Auditor]),
        ([SourcingRfqsManage, SourcingQuotesEnter, SourcingAwardsSubmit, SourcingRfqsCloseEarly], [Buyer, ProcurementManager]),
        ([SourcingQuotesRead], [Buyer, ProcurementManager, Auditor]),
        ([SourcingWaiversLowValue], [Buyer, ProcurementManager]),
        ([SourcingWaiversDirectPurchase, SourcingWaiversInsufficientQuotations, SourcingWaiversNonLeading, SourcingRatesManual], [ProcurementManager]),
        ([ApprovalsDecide, ApprovalsDelegate], [CostCenterManager, DepartmentHead, FinanceApprover, ProcurementManager]),
        ([ApprovalsAdminister, ApprovalsPoliciesManage], [ProcurementManager]),
        ([ApprovalsReadAll], [FinanceApprover, ProcurementManager, Auditor]),
        ([PurchasingOrdersRead], [Buyer, ProcurementManager, Auditor]),
        ([PurchasingOrdersManage, PurchasingOrdersIssue, PurchasingOrdersCancel], [Buyer, ProcurementManager]),
        ([PurchasingOrdersShortClose], [ProcurementManager]),
        ([ReceivingReceiptsCreate, ReceivingReceiptsCorrect], [ProcurementManager, Receiver]),
        ([ReceivingReceiptsRead], [ProcurementManager, Receiver, Auditor]),
        ([ReportsRequest], [.. Requesting, Auditor]),
        ([FilesUpload], Requesting),
        ([ReportsReadAll], [Auditor])
    ];

    /// <summary>Permissions of each seeded role, sorted ordinally.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PermissionsByRole { get; } = All.ToDictionary(
        role => role,
        role => (IReadOnlyList<string>)Matrix
            .Where(row => row.Roles.Contains(role))
            .SelectMany(row => row.Permissions)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray(),
        StringComparer.Ordinal);
}
