using Shouldly;
using Template.Application.Security;
using static Template.Application.Security.Permissions;
using static Template.Application.Security.SeededRoles;

namespace Template.Unit.Tests.ApplicationTests.Security;

/// <summary>Permission catalog and policy-v1 role matrix of docs/product/permissions.md (SEC-008, SOD-5..7, B23-9).</summary>
public sealed class PermissionCatalogTests
{
    [Fact]
    public void Catalog_HasTheDocumentedPermissions_WithStableLowercaseNames()
    {
        Permissions.All.Count.ShouldBe(47);
        Permissions.All.ShouldBeUnique();
        Permissions.All.ShouldAllBe(permission => permission == permission.ToLowerInvariant() && permission.Contains('.'));
        Permissions.All.ShouldNotContain(ClaimType);
        IsKnown("audit.read").ShouldBeTrue();
        IsKnown("AUDIT.READ").ShouldBeFalse();
        IsKnown("BUD-005").ShouldBeFalse();
    }

    [Fact]
    public void Matrix_CoversTenRoles_AndOnlyKnownPermissions()
    {
        PermissionsByRole.Keys.ShouldBe(SeededRoles.All, ignoreOrder: true);
        PermissionsByRole.Values.SelectMany(permissions => permissions).ShouldAllBe(permission => IsKnown(permission));
        PermissionsByRole.Values.SelectMany(permissions => permissions).Distinct().Count().ShouldBe(Permissions.All.Count);
    }

    [Theory]
    [InlineData(Auditor, true)]
    [InlineData(Administrator, true)]
    [InlineData(Requester, false)]
    [InlineData(Buyer, false)]
    [InlineData(ProcurementManager, false)]
    public void AuditRead_IsHeldOnlyByAuditorAndAdministrator(string role, bool expected) =>
        PermissionsByRole[role].Contains(AuditRead).ShouldBe(expected);

    [Fact]
    public void Administrator_HoldsNoProcurementDecisionPermission()
    {
        // SOD-6 / B23-9: access and master data only.
        PermissionsByRole[Administrator].ShouldBe(
            [AccessRolesManage, AccessUsersManage, AuditRead, CatalogManage, CatalogRead, OrganizationManage, OrganizationRead,
             OrganizationSettingsManage, SuppliersRead],
            ignoreOrder: true);
    }

    [Fact]
    public void BudgetController_HoldsNoSourcingOrPurchasingPermission() =>
        PermissionsByRole[BudgetController].ShouldNotContain(permission => permission.StartsWith("sourcing.") || permission.StartsWith("purchasing."));

    [Fact]
    public void HighValueWaivers_AreHeldOnlyByProcurementManager()
    {
        // SOD-5
        foreach (var waiver in new[] { SourcingWaiversDirectPurchase, SourcingWaiversInsufficientQuotations, SourcingWaiversNonLeading, SourcingRatesManual, PurchasingOrdersShortClose })
            PermissionsByRole.Where(pair => pair.Value.Contains(waiver)).Select(pair => pair.Key).ShouldBe([ProcurementManager]);

        PermissionsByRole.Where(pair => pair.Value.Contains(SourcingWaiversLowValue)).Select(pair => pair.Key)
            .ShouldBe([Buyer, ProcurementManager], ignoreOrder: true);
    }

    [Fact]
    public void Auditor_ReadsButNeverDecides()
    {
        PermissionsByRole[Auditor].ShouldContain(ReceivingReceiptsRead);
        PermissionsByRole[Auditor].ShouldContain(ReportsRequest);
        PermissionsByRole[Auditor].ShouldNotContain(ReceivingReceiptsCreate);
        PermissionsByRole[Auditor].ShouldNotContain(FilesUpload);
        PermissionsByRole[Auditor].ShouldNotContain(ApprovalsDecide);
    }
}
