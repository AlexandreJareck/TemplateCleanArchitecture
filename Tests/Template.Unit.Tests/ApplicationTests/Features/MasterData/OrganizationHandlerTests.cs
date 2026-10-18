using Shouldly;
using Template.Application.Common.Results;
using Template.Application.Features.Organization;
using Template.Application.Interfaces.Organization;
using Template.Application.Wrappers;
using Template.Domain.Organization;

namespace Template.Unit.Tests.ApplicationTests.Features.MasterData;

/// <summary>Organization use cases against port fakes (ORG-001..006, AUD-001, SP-1).</summary>
public sealed class OrganizationHandlerTests
{
    private const string Reason = "Merged into the finance department";
    private readonly MasterDataTestDoubles doubles = new();

    private DepartmentCommandHandlers Departments => new(doubles.Departments, doubles.CostCenters, doubles.Accounts, doubles.Writer);
    private CostCenterCommandHandlers CostCenters => new(doubles.CostCenters, doubles.Departments, doubles.Accounts, doubles.Writer);

    [Fact]
    public async Task CreateDepartment_PersistsAndAuditsInTheSameCommit()
    {
        var result = await Departments.Handle(new CreateDepartmentCommand("fin", "Finance", doubles.Accounts.UserId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("FIN");
        result.Value.Version.ShouldBe("1");
        doubles.Departments.Items.Single().CreatedBy.ShouldBe(doubles.User.Id);
        doubles.UnitOfWork.Saves.ShouldBe(1);
        var audit = doubles.Audit.Records.Single();
        (audit.Action, audit.ResourceType, audit.ResourceId, audit.ActorId, audit.ToState)
            .ShouldBe((OrganizationAudit.DepartmentCreated, "department", result.Value.Id.ToString(), (Guid?)doubles.User.Id, "Active"));
        audit.RuleIds.ShouldBe(["ORG-001"]);
        audit.Details["code"].ShouldBe("FIN");
        audit.CorrelationId.ShouldBe("unit-test-trace");
    }

    [Fact]
    public async Task T_ORG_001_c_DuplicateCode_IsRejected_CaseInsensitively_WithoutWriting()
    {
        await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None);

        var result = await Departments.Handle(new CreateDepartmentCommand("fin", "Other", null), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.OrganizationCodeDuplicate);
        result.Error.Category.ShouldBe(ErrorCategory.Conflict);
        result.Error.RuleId.ShouldBe("ORG-001");
        doubles.UnitOfWork.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task T_ORG_001_b_UniqueIndexRace_IsTheSameDuplicateError()
    {
        doubles.UnitOfWork.UniqueViolation = IDepartmentRepository.CodeUniqueKey;

        var result = await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.OrganizationCodeDuplicate);
        doubles.UnitOfWork.Discards.ShouldBe(1);
    }

    [Fact]
    public async Task UnknownUniqueViolation_IsNotSwallowed()
    {
        doubles.UnitOfWork.UniqueViolation = "IX_Something_Else";

        await Should.ThrowAsync<Template.Application.Common.Concurrency.UniqueConstraintViolationException>(
            () => Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None));
    }

    [Fact]
    public async Task T_ORG_006_a_HeadAndManager_MustBeActiveUsers()
    {
        var unknown = Guid.NewGuid();

        var department = await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", unknown), CancellationToken.None);
        department.Error!.FieldErrors!.Keys.ShouldBe(["headUserId"]);
        department.Error.RuleId.ShouldBe("ORG-006");

        var created = await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None);
        var costCenter = await CostCenters.Handle(new CreateCostCenterCommand("CC-1", "Treasury", created.Value.Id, unknown), CancellationToken.None);
        costCenter.Error!.FieldErrors!.Keys.ShouldBe(["managerUserId"]);

        // The same active user may manage several cost centers (ORG-006).
        (await CostCenters.Handle(new CreateCostCenterCommand("CC-1", "Treasury", created.Value.Id, doubles.Accounts.UserId), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await CostCenters.Handle(new CreateCostCenterCommand("CC-2", "Payroll", created.Value.Id, doubles.Accounts.UserId), CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_RequiresTheCurrentVersion()
    {
        var created = await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None);

        var missing = await Departments.Handle(new UpdateDepartmentCommand(created.Value.Id, null, "X", null), CancellationToken.None);
        var stale = await Departments.Handle(new UpdateDepartmentCommand(created.Value.Id, 7, "X", null), CancellationToken.None);
        var ok = await Departments.Handle(new UpdateDepartmentCommand(created.Value.Id, 1, "Finance & Control", null), CancellationToken.None);

        missing.Error!.Code.ShouldBe(ProblemCodes.PreconditionRequired);
        stale.Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        stale.Error.Extensions!["version"].ShouldBe("1");
        ok.Value.Version.ShouldBe("2");
        doubles.Audit.Records.Last().Action.ShouldBe(OrganizationAudit.DepartmentUpdated);
    }

    [Fact]
    public async Task ConcurrentWriter_Wins_And_TheLoserGets412WithTheCurrentVersion()
    {
        var created = await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None);
        doubles.UnitOfWork.ThrowConflict = true;
        doubles.Departments.ConcurrentVersion = 2;

        var result = await Departments.Handle(new UpdateDepartmentCommand(created.Value.Id, 1, "Changed", null), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.VersionMismatch);
        result.Error.Category.ShouldBe(ErrorCategory.PreconditionFailed);
        result.Error.Extensions!["version"].ShouldBe("2");
        doubles.UnitOfWork.Discards.ShouldBe(1);
    }

    [Fact]
    public async Task T_ORG_003_b_DepartmentWithActiveCostCenter_CannotBeDeactivated_UntilTheCostCenterIs()
    {
        var department = (await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None)).Value;
        var costCenter = (await CostCenters.Handle(new CreateCostCenterCommand("CC-1", "Treasury", department.Id, null), CancellationToken.None)).Value;

        var blocked = await Departments.Handle(new DeactivateDepartmentCommand(department.Id, 1, Reason), CancellationToken.None);
        blocked.Error!.Code.ShouldBe(ProblemCodes.DepartmentHasActiveCostCenters);
        blocked.Error.Category.ShouldBe(ErrorCategory.Conflict);

        (await CostCenters.Handle(new DeactivateCostCenterCommand(costCenter.Id, 1, Reason), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var deactivated = await Departments.Handle(new DeactivateDepartmentCommand(department.Id, 1, Reason), CancellationToken.None);

        deactivated.Value.IsActive.ShouldBeFalse();
        var audit = doubles.Audit.Records.Last();
        (audit.Action, audit.FromState, audit.ToState, audit.Reason).ShouldBe((OrganizationAudit.DepartmentDeactivated, "Active", "Inactive", Reason));
        audit.RuleIds.ShouldBe(["ORG-003"]);
    }

    [Fact]
    public async Task T_ORG_003_b_CostCenterCannotBeCreatedOrReactivatedInAnInactiveDepartment()
    {
        var department = (await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None)).Value;
        var costCenter = (await CostCenters.Handle(new CreateCostCenterCommand("CC-1", "Treasury", department.Id, null), CancellationToken.None)).Value;
        await CostCenters.Handle(new DeactivateCostCenterCommand(costCenter.Id, 1, Reason), CancellationToken.None);
        await Departments.Handle(new DeactivateDepartmentCommand(department.Id, 1, Reason), CancellationToken.None);

        var created = await CostCenters.Handle(new CreateCostCenterCommand("CC-2", "Payroll", department.Id, null), CancellationToken.None);
        var reactivated = await CostCenters.Handle(new ReactivateCostCenterCommand(costCenter.Id, 2, null), CancellationToken.None);

        created.Error!.Code.ShouldBe(ProblemCodes.OrganizationInactive);
        created.Error.Category.ShouldBe(ErrorCategory.BusinessRule);
        reactivated.Error!.Code.ShouldBe(ProblemCodes.OrganizationInactive);
    }

    [Fact]
    public async Task DeactivateWithoutReason_IsReasonRequired400()
    {
        var department = (await Departments.Handle(new CreateDepartmentCommand("FIN", "Finance", null), CancellationToken.None)).Value;

        var result = await Departments.Handle(new DeactivateDepartmentCommand(department.Id, 1, null), CancellationToken.None);

        result.Error!.Code.ShouldBe(ProblemCodes.ReasonRequired);
        result.Error.Category.ShouldBe(ErrorCategory.Validation);
        result.Error.FieldErrors!.Keys.ShouldBe(["reason"]);
    }

    [Fact]
    public async Task UnknownIds_AreNotFound()
    {
        (await Departments.Handle(new UpdateDepartmentCommand(Guid.NewGuid(), 1, "X", null), CancellationToken.None)).Error!.Category.ShouldBe(ErrorCategory.NotFound);
        (await new DepartmentQueryHandlers(doubles.Departments).Handle(new GetDepartmentQuery(Guid.NewGuid()), CancellationToken.None)).Error!.Code.ShouldBe(ProblemCodes.NotFound);
        (await CostCenters.Handle(new CreateCostCenterCommand("CC-1", "X", Guid.NewGuid(), null), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["departmentId"]);
    }

    [Theory]
    [InlineData(0, null, "page")]
    [InlineData(null, 101, "pageSize")]
    public async Task List_ValidatesPaging(int? page, int? pageSize, string field)
    {
        var result = await new DepartmentQueryHandlers(doubles.Departments).Handle(new ListDepartmentsQuery(page, pageSize), CancellationToken.None);

        result.Error!.FieldErrors!.Keys.ShouldBe([field]);
    }

    [Fact]
    public async Task List_RejectsUnknownSortAndLongQ_AndPassesTrimmedFilters()
    {
        var handler = new DepartmentQueryHandlers(doubles.Departments);

        (await handler.Handle(new ListDepartmentsQuery(Sort: "password"), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["sort"]);
        (await handler.Handle(new ListDepartmentsQuery(Q: new string('x', 101)), CancellationToken.None)).Error!.FieldErrors!.Keys.ShouldBe(["q"]);
        (await handler.Handle(new ListDepartmentsQuery(Q: "  fin ", IsActive: true, Sort: "-name"), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        doubles.Departments.LastFilter.ShouldBe(new DepartmentFilter("fin", true));
    }

    [Fact]
    public async Task T_ORG_004_a_Settings_LockAfterTheFirstAllocation()
    {
        var handler = new OrganizationSettingsHandlers(doubles.Settings, doubles.SettingsLock, doubles.Writer);
        doubles.SettingsLock.HasAllocations = true;

        var locked = await handler.Handle(new UpdateOrganizationSettingsCommand(1, "Acme", "USD", "America/Sao_Paulo", ["USD", "BRL"]), CancellationToken.None);
        var nameOnly = await handler.Handle(new UpdateOrganizationSettingsCommand(1, "Acme", "BRL", "America/Sao_Paulo", ["BRL", "USD"]), CancellationToken.None);

        locked.Error!.Code.ShouldBe(ProblemCodes.OrganizationSettingsLocked);
        locked.Error.Category.ShouldBe(ErrorCategory.Conflict);
        nameOnly.Value.DisplayName.ShouldBe("Acme");
        doubles.Audit.Records.Single().Action.ShouldBe(OrganizationAudit.SettingsChanged);
    }

    [Fact]
    public async Task Settings_RejectUnknownTimeZones_AndAcceptIana()
    {
        var handler = new OrganizationSettingsHandlers(doubles.Settings, doubles.SettingsLock, doubles.Writer);

        var unknown = await handler.Handle(new UpdateOrganizationSettingsCommand(1, "Acme", "BRL", "Mars/Olympus", ["BRL"]), CancellationToken.None);
        var lisbon = await handler.Handle(new UpdateOrganizationSettingsCommand(1, "Acme", "EUR", "Europe/Lisbon", ["EUR", "BRL"]), CancellationToken.None);

        unknown.Error!.FieldErrors!.Keys.ShouldBe(["timeZone"]);
        lisbon.Value.TimeZone.ShouldBe("Europe/Lisbon");
        lisbon.Value.BaseCurrency.ShouldBe("EUR");
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new DepartmentQueryHandlers(doubles.Departments).Handle(new GetDepartmentQuery(Guid.NewGuid()), source.Token));
    }

    [Fact]
    public void Validators_RejectMissingFields()
    {
        new CreateDepartmentCommandValidator().Validate(new CreateDepartmentCommand(null, null, null)).Errors
            .Select(error => error.PropertyName).ShouldBe(["Code", "Name"], ignoreOrder: true);
        new CreateCostCenterCommandValidator().Validate(new CreateCostCenterCommand("C1", "N", Guid.Empty, null)).Errors
            .Select(error => error.PropertyName).ShouldBe(["DepartmentId"]);
    }
}
