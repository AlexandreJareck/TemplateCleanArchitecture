using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Common.Paging;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Requisitions;
using Template.Domain.Budgeting;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Infrastructure.Persistence;

namespace Template.Integration.Tests.Data;

/// <summary>
/// Phase 27 budget and requisition repositories on EF InMemory through the real Persistence registration: aggregate round
/// trips with owned rows, the filtered reservation load, visibility filters, the ORG-004 lock adapter, number allocation
/// and the logical transaction used when no relational provider exists. Row locks, exclusion/unique/check constraints and
/// concurrency are PostgreSQL semantics covered by the PostgreSQL suite.
/// </summary>
public sealed class ProcurementPersistenceAdapterTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2027, 3, 1);
    private readonly ServiceProvider provider;

    public ProcurementPersistenceAdapterTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TestDatabaseName"] = Guid.NewGuid().ToString("N") })
            .Build();
        var services = new ServiceCollection();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: true);
        services.AddScoped<IAuthenticatedUserService>(_ => new AuthenticatedUserService(Guid.NewGuid().ToString(), "integration"));
        provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Allocation_RoundTrips_AndTheReservationLoadIsFilteredByRequisition_WithExactTotals()
    {
        var (period, allocation) = NewAllocation();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        allocation.Reserve(Guid.NewGuid(), first, 1, new Money(100m, "BRL"), period, Today, null, Now, "trace").Error.ShouldBeNull();
        allocation.Reserve(Guid.NewGuid(), second, 1, new Money(250m, "BRL"), period, Today, null, Now, "trace").Error.ShouldBeNull();
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<IFiscalPeriodRepository>().Add(period);
            scope.GetRequiredService<IBudgetAllocationRepository>().Add(allocation);
        });

        await using var read = provider.CreateAsyncScope();
        var allocations = read.ServiceProvider.GetRequiredService<IBudgetAllocationRepository>();
        var unitOfWork = read.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.BeginTransactionAsync();
        var locked = await allocations.FindForUpdateAsync(allocation.Id, first, CancellationToken.None);

        locked!.Reservations.Select(reservation => reservation.RequisitionId).ShouldBe([first]);
        (locked.ReservedAmount, locked.AvailableAmount).ShouldBe((350m, 650m));
        locked.Reservations.Single().Changes.Single().Operation.ShouldBe(BudgetReservationOperation.Reserved);
        locked.Release(first, BudgetReleaseReason.Cancelled, null, Now, "trace").Changed.ShouldBeTrue();
        (await unitOfWork.CommitTransactionAsync()).ShouldBeTrue();

        await using var verify = provider.CreateAsyncScope();
        var reloaded = await verify.ServiceProvider.GetRequiredService<IBudgetAllocationRepository>().FindAsync(allocation.Id, CancellationToken.None);
        (reloaded!.ReservedAmount, reloaded.AvailableAmount).ShouldBe((250m, 750m));
        (await verify.ServiceProvider.GetRequiredService<IBudgetAllocationRepository>().ListReservationsOfRequisitionAsync(first, CancellationToken.None))
            .Single().Status.ShouldBe(BudgetReservationStatus.Released);
    }

    [Fact]
    public async Task LogicalTransaction_RollsBackTrackedChanges_OnInMemory()
    {
        var (period, allocation) = NewAllocation();
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<IFiscalPeriodRepository>().Add(period);
            scope.GetRequiredService<IBudgetAllocationRepository>().Add(allocation);
        });

        await using (var scope = provider.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginTransactionAsync();
            var tracked = await scope.ServiceProvider.GetRequiredService<IBudgetAllocationRepository>().FindForUpdateAsync(allocation.Id, Guid.Empty, CancellationToken.None);
            tracked!.ChangeAmount(5m, "Synthetic reduction reason", null, Now).ShouldBeNull();
            await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.BeginTransactionAsync());
            await unitOfWork.RollbackTransactionAsync();
            (await unitOfWork.SaveChangesAsync()).ShouldBeFalse();
        }

        await using var verify = provider.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<IBudgetAllocationRepository>().FindAsync(allocation.Id, CancellationToken.None))!.Amount.ShouldBe(1000m);
    }

    [Fact]
    public async Task Periods_OverlapAndOpenContaining_TranslateOnInMemory()
    {
        var (period, _) = NewAllocation();
        await SaveAsync(scope => scope.GetRequiredService<IFiscalPeriodRepository>().Add(period));
        await using var scope = provider.CreateAsyncScope();
        var periods = scope.ServiceProvider.GetRequiredService<IFiscalPeriodRepository>();

        (await periods.OverlapsAsync(new DateOnly(2027, 12, 31), new DateOnly(2028, 1, 31), null, CancellationToken.None)).ShouldBeTrue();
        (await periods.OverlapsAsync(new DateOnly(2028, 1, 1), new DateOnly(2028, 1, 31), null, CancellationToken.None)).ShouldBeFalse();
        (await periods.OverlapsAsync(period.StartDate, period.EndDate, period.Id, CancellationToken.None)).ShouldBeFalse();
        (await periods.FindOpenContainingAsync(Today, CancellationToken.None))!.Id.ShouldBe(period.Id);
        (await periods.FindOpenContainingAsync(new DateOnly(2028, 1, 1), CancellationToken.None)).ShouldBeNull();
        (await periods.ListAsync(new FiscalPeriodFilter("fy", [FiscalPeriodStatus.Open]), Page("code"), CancellationToken.None)).TotalItems.ShouldBe(1);
    }

    [Fact]
    public async Task SettingsLock_FollowsTheAllocationTable()
    {
        await using (var scope = provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IOrganizationSettingsLock>().HasBudgetAllocationsAsync(CancellationToken.None)).ShouldBeFalse();

        var (period, allocation) = NewAllocation();
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<IFiscalPeriodRepository>().Add(period);
            scope.GetRequiredService<IBudgetAllocationRepository>().Add(allocation);
        });

        await using var verify = provider.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<IOrganizationSettingsLock>().HasBudgetAllocationsAsync(CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Requisition_RoundTripsWithLinesAttachmentsHistoryAndSnapshot_AndListsHonorVisibility()
    {
        var requester = Guid.NewGuid();
        var costCenter = new CostCenterSnapshot(Guid.NewGuid(), "CC-1", "Support", Guid.NewGuid(), true);
        var requisition = PurchaseRequisition.CreateDraft(Guid.NewGuid(), "REQ-2027-000001", requester, costCenter, "BRL", true,
            "Synthetic purpose for the adapter", Today.AddDays(5), requester, Now, "trace").Requisition!;
        requisition.AddLine(new RequisitionLineInput(null, "Synthetic line", RequisitionLineKind.Service,
            new CategorySnapshot(Guid.NewGuid(), "SV", "Services", true), new UnitSnapshot(Guid.NewGuid(), "H", "Hour", 2, true), 1.5m, new Money(80m, "BRL")),
            RequisitionPolicy.V1, Now).Error.ShouldBeNull();
        requisition.AddAttachment(Guid.NewGuid(), "scope.pdf", "application/pdf", 1024, requester, Now);
        var context = new SubmissionContext(requester, Now, Today, true, costCenter,
            requisition.Lines.SelectMany(line => new[] { line.CategoryId, line.UnitOfMeasureId }).ToDictionary(id => id, _ => true), "trace", RequisitionPolicy.V1);
        requisition.Submit(context, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).ShouldBeNull();
        await SaveAsync(scope => scope.GetRequiredService<IPurchaseRequisitionRepository>().Add(requisition));

        await using var read = provider.CreateAsyncScope();
        var repository = read.ServiceProvider.GetRequiredService<IPurchaseRequisitionRepository>();
        var loaded = await repository.FindAsync(requisition.Id, CancellationToken.None);
        loaded!.Lines.Single().LineTotal.ShouldBe(120m);
        loaded.Attachments.Single().LockedAtRevision.ShouldBe(1);
        loaded.StateChanges.Select(change => change.To).ShouldBe([RequisitionStatus.Draft, RequisitionStatus.Submitted], ignoreOrder: true);
        loaded.Submissions.Single().Lines.Single().Quantity.ShouldBe(1.5m);
        loaded.Version.ShouldBe(requisition.Version);

        var own = new RequisitionVisibility(requester, false, [], []);
        var stranger = new RequisitionVisibility(Guid.NewGuid(), false, [], []);
        var team = new RequisitionVisibility(Guid.NewGuid(), false, [], [costCenter.DepartmentId]);
        (await repository.ListAsync(new RequisitionFilter(own), Page("number"), CancellationToken.None)).TotalItems.ShouldBe(1);
        (await repository.ListAsync(new RequisitionFilter(stranger), Page("number"), CancellationToken.None)).TotalItems.ShouldBe(0);
        (await repository.ListAsync(new RequisitionFilter(team, Statuses: [RequisitionStatus.Submitted], Number: "000001"), Page("number"), CancellationToken.None))
            .TotalItems.ShouldBe(1);
        (await repository.ListDueForExpiryAsync(Today.AddDays(6), 10, CancellationToken.None)).ShouldBe([requisition.Id]);
    }

    [Fact]
    public async Task NumberAllocator_IncrementsPerYear_WithoutARelationalProvider()
    {
        await using var scope = provider.CreateAsyncScope();
        var numbers = scope.ServiceProvider.GetRequiredService<IRequisitionNumberAllocator>();

        var first = await numbers.NextAsync(1999, CancellationToken.None);
        var second = await numbers.NextAsync(1999, CancellationToken.None);

        second.ShouldBe(first + 1);
    }

    [Fact]
    public async Task OrganizationScope_ReadsManagedCostCentersAndHeadedDepartments()
    {
        var user = Guid.NewGuid();
        var department = Department.Create(Guid.NewGuid(), "OPS", "Operations", user, null, Now).Department!;
        var costCenter = CostCenter.Create(Guid.NewGuid(), "CC-OPS", "Support", department, user, null, Now).CostCenter!;
        await SaveAsync(scope =>
        {
            scope.GetRequiredService<IDepartmentRepository>().Add(department);
            scope.GetRequiredService<ICostCenterRepository>().Add(costCenter);
        });

        await using var read = provider.CreateAsyncScope();
        var scopeReader = read.ServiceProvider.GetRequiredService<IOrganizationScopeReader>();
        var found = await scopeReader.GetScopeAsync(user, CancellationToken.None);

        found.ManagedCostCenterIds.ShouldBe([costCenter.Id]);
        found.HeadedDepartmentIds.ShouldBe([department.Id]);
        (await scopeReader.GetScopeAsync(Guid.NewGuid(), CancellationToken.None)).ManagedCostCenterIds.ShouldBeEmpty();
    }

    public async ValueTask DisposeAsync() => await provider.DisposeAsync();

    private static (FiscalPeriod Period, BudgetAllocation Allocation) NewAllocation()
    {
        var period = FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal year 2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), null, Now).Period!;
        period.Open(Now);
        var allocation = BudgetAllocation.Create(Guid.NewGuid(), period, new MasterDataReference(Guid.NewGuid(), true), "BRL", true, 1000m, null, Now).Allocation!;
        return (period, allocation);
    }

    private async Task SaveAsync(Action<IServiceProvider> arrange)
    {
        await using var scope = provider.CreateAsyncScope();
        arrange(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static PageRequest Page(string sort) =>
        PageRequest.Create(1, 20, sort, ["number", "code", "createdAt"], sort).Value;
}
