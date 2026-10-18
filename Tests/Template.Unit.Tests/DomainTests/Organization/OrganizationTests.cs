using Shouldly;
using Template.Domain.Common;
using Template.Domain.Organization;

namespace Template.Unit.Tests.DomainTests.Organization;

/// <summary>ORG-001..004 rules of departments, cost centers and organization settings, without mocks.</summary>
public sealed class OrganizationTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    private const string Reason = "Reorganization approved by the board";

    [Theory]
    [InlineData("fin", "FIN")]
    [InlineData(" ops-01 ", "OPS-01")]
    [InlineData("AB", "AB")]
    [InlineData("ABCDEFGHIJ0123456789", "ABCDEFGHIJ0123456789")]
    public void T_ORG_001_a_CodeIsNormalizedToUpperCase(string code, string expected)
    {
        var (department, error) = Department.Create(Guid.NewGuid(), code, "Finance", null, null, Now);

        error.ShouldBeNull();
        department!.Code.ShouldBe(expected);
        department.IsActive.ShouldBeTrue();
        department.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJ01234567890")]
    [InlineData("FIN_01")]
    [InlineData("FIN 01")]
    [InlineData("ÇÃO")]
    [InlineData("")]
    [InlineData(null)]
    public void T_ORG_001_a_InvalidCodes_AreValidationErrors(string? code)
    {
        var (department, error) = Department.Create(Guid.NewGuid(), code, "Finance", null, null, Now);

        department.ShouldBeNull();
        error!.Kind.ShouldBe(DomainErrorKind.Validation);
        error.RuleId.ShouldBe("ORG-001");
        error.Field.ShouldBe("code");
    }

    [Fact]
    public void T_ORG_001_a_CodeIsImmutable_UpdateChangesNameAndHeadOnly()
    {
        var department = NewDepartment();
        var head = Guid.NewGuid();

        department.Update("Finance and Controlling", head, Now.AddMinutes(1)).ShouldBeNull();

        department.Code.ShouldBe("FIN");
        department.Name.ShouldBe("Finance and Controlling");
        department.HeadUserId.ShouldBe(head);
        department.Version.ShouldBe(2);
        department.UpdatedAtUtc.ShouldBe(Now.AddMinutes(1));
        typeof(Department).GetProperty(nameof(Department.Code))!.SetMethod!.IsPublic.ShouldBeFalse();
    }

    [Fact]
    public void UnchangedUpdate_DoesNotBumpTheVersion()
    {
        var department = NewDepartment();

        department.Update(" Finance ", null, Now.AddMinutes(1)).ShouldBeNull();

        department.Version.ShouldBe(1);
    }

    [Fact]
    public void T_ORG_002_a_CostCenterBelongsToItsDepartmentForever()
    {
        var department = NewDepartment();
        var (costCenter, error) = CostCenter.Create(Guid.NewGuid(), "cc-100", "Treasury", department, null, null, Now);

        error.ShouldBeNull();
        costCenter!.DepartmentId.ShouldBe(department.Id);
        costCenter.Code.ShouldBe("CC-100");
        typeof(CostCenter).GetProperty(nameof(CostCenter.DepartmentId))!.SetMethod!.IsPublic.ShouldBeFalse();
        typeof(CostCenter).GetMethods().ShouldNotContain(method => method.Name.Contains("Department") && method.Name.StartsWith("Move"));
    }

    [Fact]
    public void T_ORG_003_a_InactiveDepartment_RejectsNewCostCentersAndNewHead()
    {
        var department = NewDepartment();
        department.Deactivate(Reason, hasActiveCostCenters: false, Now).ShouldBeNull();

        var (costCenter, error) = CostCenter.Create(Guid.NewGuid(), "CC-1", "Treasury", department, null, null, Now);
        costCenter.ShouldBeNull();
        error!.Code.ShouldBe(OrganizationErrors.InactiveCode);
        error.RuleId.ShouldBe("ORG-003");
        department.Update("Finance", Guid.NewGuid(), Now)!.Code.ShouldBe(OrganizationErrors.InactiveCode);
    }

    [Fact]
    public void T_ORG_003_a_DepartmentWithActiveCostCenters_CannotBeDeactivated()
    {
        var department = NewDepartment();

        var error = department.Deactivate(Reason, hasActiveCostCenters: true, Now);

        error!.Code.ShouldBe(OrganizationErrors.DepartmentHasActiveCostCentersCode);
        error.Kind.ShouldBe(DomainErrorKind.Conflict);
        department.IsActive.ShouldBeTrue();
        department.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too short")]
    public void T_ORG_003_a_DeactivationRequiresAReason(string? reason)
    {
        var department = NewDepartment();

        department.Deactivate(reason, false, Now)!.Code.ShouldBe(DomainError.ReasonRequiredCode);
        department.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void T_ORG_003_a_InactiveCostCenter_KeepsHistory_RejectsUseAndManager_AndReactivatesOnlyInActiveDepartment()
    {
        var department = NewDepartment();
        var (costCenter, _) = CostCenter.Create(Guid.NewGuid(), "CC-1", "Treasury", department, Guid.NewGuid(), null, Now);

        costCenter!.Deactivate(Reason, Now).ShouldBeNull();
        costCenter.EnsureUsable()!.Code.ShouldBe(OrganizationErrors.InactiveCode);
        costCenter.Update("Treasury", Guid.NewGuid(), Now)!.Code.ShouldBe(OrganizationErrors.InactiveCode);
        costCenter.Deactivate(Reason, Now)!.Code.ShouldBe(DomainError.InvalidTransitionCode);
        costCenter.Code.ShouldBe("CC-1");

        department.Deactivate(Reason, hasActiveCostCenters: false, Now).ShouldBeNull();
        costCenter.Reactivate(department, Now)!.Code.ShouldBe(OrganizationErrors.InactiveCode);
        department.Reactivate(Now).ShouldBeNull();
        costCenter.Reactivate(department, Now).ShouldBeNull();
        costCenter.IsActive.ShouldBeTrue();
        costCenter.EnsureUsable().ShouldBeNull();
    }

    [Fact]
    public void BehaviorMethods_RejectNonUtcInstantsAndEmptyIds()
    {
        Should.Throw<ArgumentException>(() => Department.Create(Guid.Empty, "FIN", "Finance", null, null, Now));
        Should.Throw<ArgumentException>(() => Department.Create(Guid.NewGuid(), "FIN", "Finance", null, null, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void T_ORG_004_a_SettingsDefaults_AreBrlAndSaoPaulo()
    {
        var settings = OrganizationSettings.CreateDefault(Now);

        settings.Id.ShouldBe(OrganizationSettings.SingletonId);
        settings.BaseCurrency.ShouldBe("BRL");
        settings.TimeZone.ShouldBe("America/Sao_Paulo");
        settings.SupportedCurrencies.ShouldBe(["BRL", "EUR", "USD"]);
    }

    [Fact]
    public void T_ORG_004_a_BaseCurrencyAndTimeZone_LockAfterTheFirstAllocation()
    {
        var settings = OrganizationSettings.CreateDefault(Now);

        settings.Change("Acme", "USD", "America/Sao_Paulo", ["USD", "BRL"], true, hasBudgetAllocations: true, Now)!
            .Code.ShouldBe(OrganizationErrors.SettingsLockedCode);
        settings.Change("Acme", "BRL", "Europe/Lisbon", ["BRL"], true, hasBudgetAllocations: true, Now)!
            .RuleId.ShouldBe("ORG-004");
        settings.BaseCurrency.ShouldBe("BRL");

        // Display name and the supported list stay editable.
        settings.Change("Acme", "brl", "America/Sao_Paulo", ["brl", "usd"], true, hasBudgetAllocations: true, Now).ShouldBeNull();
        settings.DisplayName.ShouldBe("Acme");
        settings.SupportedCurrencies.ShouldBe(["BRL", "USD"]);
        settings.Version.ShouldBe(2);
    }

    [Fact]
    public void Settings_Validation()
    {
        var settings = OrganizationSettings.CreateDefault(Now);

        settings.Change("Acme", "REAL", "America/Sao_Paulo", ["BRL"], true, false, Now)!.Field.ShouldBe("baseCurrency");
        settings.Change("Acme", "BRL", "Mars/Olympus", ["BRL"], isTimeZoneKnown: false, false, Now)!.Field.ShouldBe("timeZone");
        settings.Change("Acme", "BRL", "America/Sao_Paulo", ["USD"], true, false, Now)!.Field.ShouldBe("supportedCurrencies");
        settings.Change("", "BRL", "America/Sao_Paulo", ["BRL"], true, false, Now)!.Field.ShouldBe("displayName");
        settings.Version.ShouldBe(1);
    }

    private static Department NewDepartment() => Department.Create(Guid.NewGuid(), "FIN", "Finance", null, null, Now).Department!;
}
