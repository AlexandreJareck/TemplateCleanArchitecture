using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Features.Approvals;
using Template.Application.Features.Budgeting;
using Template.Application.Features.Catalog;
using Template.Application.Features.Requisitions;
using Template.Application.Features.Sourcing;
using Template.Application.Features.Suppliers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Security;
using Template.Application.Services.Integrations;
using Template.Application.Workflow;
using Template.Domain.Approvals;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Messaging;

namespace Template.PostgreSql.Integration.Tests;

/// <summary>
/// Phase 31 approvals on real PostgreSQL 16.15: additive migration with the policy-v1 seed and rollback, the SC-01
/// two-level journey persisted and reloaded with the committed reservation, the inbox and participant queries
/// translated to SQL, concurrent duplicate starts (APR-001), two group members deciding the same step at once (SC-08,
/// APR-014), concurrent finalization releasing the reservation exactly once (APR-009), supplier suspension invalidating
/// in the supplier's own commit (APR-012) and the reconciliation queries (APR-016). Each test uses its own synthetic year.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class ApprovalPostgreSqlTests(PostgreSqlFixture fixture) : IAsyncLifetime
{
    private const string Phase30Migration = "20261023161337_Phase30FilesReports";
    private const string Reason = "Synthetic approval reason for PostgreSQL";
    private static int nextYear = 2600;

    private readonly int year = Interlocked.Increment(ref nextYear);
    private readonly SwitchableActor actor = new();
    private readonly Approvers approvers = new();
    private readonly Guid requester = Guid.NewGuid();
    private readonly Guid buyer = Guid.NewGuid();
    private readonly Guid manager = Guid.NewGuid();
    private readonly Guid head = Guid.NewGuid();
    private MutableClock clock = null!;
    private ServiceProvider provider = null!;
    private Guid costCenterId;
    private Guid categoryId;
    private Guid unitId;

    private DateTime Now => new(year, 3, 1, 13, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        clock = new MutableClock(Now);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(clock);
        services.AddApplicationLayer();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddSingleton<IAuthenticatedUserService>(actor);
        services.AddSingleton<IRequestContext>(new Trace());
        services.AddSingleton<IAccountDirectory>(new ActiveUsers());
        services.AddSingleton<IPermissionReader>(new AllPermissions());
        services.AddSingleton<IApproverDirectory>(approvers);
        services.AddSingleton<IApprovalWorkflowClient>(new RunningWorkflow());
        services.AddSingleton<IExchangeRateProvider>(new NoRates());
        foreach (var handler in new[]
                 {
                     typeof(FiscalPeriodCommandHandlers), typeof(BudgetAllocationCommandHandlers), typeof(RequisitionDraftCommandHandlers),
                     typeof(RequisitionTransitionHandlers), typeof(RfqCommandHandlers), typeof(QuoteCommandHandlers), typeof(ApprovalDecisionHandlers),
                     typeof(ApprovalQueryHandlers), typeof(SupplierCommandHandlers)
                 })
            services.AddScoped(handler);
        provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.InitializeApplicationDatabaseAsync(applyMigrations: true);
        await using var context = fixture.CreateContext();
        var department = Department.Create(Guid.NewGuid(), Code("D"), "Synthetic department", head, null, Now).Department!;
        var costCenter = CostCenter.Create(Guid.NewGuid(), Code("C"), "Synthetic cost center", department, manager, null, Now).CostCenter!;
        var category = Category.Create(Guid.NewGuid(), Code("G"), "Synthetic category", null, Now).Category!;
        var unit = UnitOfMeasure.Create(Guid.NewGuid(), Code("U"), "Synthetic unit", 0, null, Now).Unit!;
        context.AddRange(department, costCenter, category, unit);
        await context.SaveChangesAsync();
        (costCenterId, categoryId, unitId) = (costCenter.Id, category.Id, unit.Id);
        approvers.Add(manager, SeededRoles.CostCenterManager);
        approvers.Add(head, SeededRoles.DepartmentHead);
    }

    public async Task DisposeAsync() => await provider.DisposeAsync();

    [Fact]
    public async Task Migration_IsAdditive_SeedsPolicyVersion1_AndRollsBackToPhase30_ThenReapplies()
    {
        await using var context = fixture.CreateContext();
        string[] tables = ["ApprovalPolicies", "ApprovalPolicyLevels", "ApprovalInstances", "ApprovalSteps", "ApprovalHistory"];
        var list = string.Join(',', tables.Select(table => $"'{table}'"));
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT count(*) FROM information_schema.tables WHERE table_name IN ({list})")).ShouldBe(5);
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'IX_ApprovalInstances_AwardId'")).ShouldContain("UNIQUE");
        (await PostgreSqlFixture.ExecuteScalarAsync<string>(context,
            "SELECT data_type FROM information_schema.columns WHERE table_name = 'ApprovalPolicies' AND column_name = 'StepDuration'")).ShouldBe("interval");
        var policy = await context.Set<ApprovalPolicy>().Include(item => item.Levels).SingleAsync(item => item.Version == 1);
        policy.Levels.OrderBy(level => level.Level).Select(level => level.MinimumAmountExclusive).ShouldBe([0m, 10_000m, 100_000m]);
        policy.StepDuration.ShouldBe(TimeSpan.FromHours(120));

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Phase30Migration);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context, $"SELECT count(*) FROM information_schema.tables WHERE table_name IN ({list})")).ShouldBe(0);
        (await PostgreSqlFixture.ExecuteScalarAsync<long>(context,
            "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('SourcingAwards', 'PurchaseRequisitions', 'ReportingReportJobs')")).ShouldBe(3);
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SC_01_TwoLevelJourney_IsPersisted_AndTheReservationIsCommittedOnce()
    {
        var (award, requisitionId) = await AwardedAsync("16200");
        await StartAndAssignAsync(award);

        var inbox = await Query(handlers => handlers.Handle(new ListApprovalInstancesQuery(null, null, "dueAt", null, "me", null, null), default), manager);
        inbox.IsSuccess.ShouldBeTrue(inbox.Error?.Message);
        var instanceId = inbox.Value.Items.ShouldHaveSingleItem().Id;
        (await Query(handlers => handlers.Handle(new ListApprovalInstancesQuery(null, null, null, null, "me", null, null), default), head)).Value.Items.ShouldBeEmpty();
        (await Query(handlers => handlers.Handle(new ListApprovalInstancesQuery(null, null, "-amount", "Pending", null, null, false), default), requester))
            .Value.Items.Select(item => item.Id).ShouldContain(instanceId);

        (await DecideAsync(instanceId, 1, manager, "Approved")).IsSuccess.ShouldBeTrue();
        await ActivityAsync(activities => activities.AssignStepAsync(new ApprovalActivityRequest(award, "pg"), default));
        (await DecideAsync(instanceId, 2, head, "Approved")).IsSuccess.ShouldBeTrue();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
            ActivityAsync(activities => activities.FinalizeAsync(new ApprovalActivityRequest(award, "pg"), default)))));

        await using var context = fixture.CreateContext();
        var stored = await context.Set<ApprovalInstance>().Include(item => item.Steps).Include(item => item.History).SingleAsync(item => item.Id == instanceId);
        stored.Status.ShouldBe(ApprovalStatus.Approved);
        stored.History.OrderBy(entry => entry.Sequence).Select(entry => entry.Action)
            .ShouldBe(["started", "step-assigned", "step-approved", "step-assigned", "step-approved", "approved"]);
        (await context.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisitionId)).Status.ShouldBe(RequisitionStatus.Approved);
        var reservation = await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == requisitionId);
        reservation.Status.ShouldBe(BudgetReservationStatus.Committed);
        reservation.Changes.Count(change => change.Operation == BudgetReservationOperation.Committed).ShouldBe(1);
        (await context.Set<OutboxMessage>().CountAsync(message => message.AggregateId == instanceId && message.EventType == "procurement.approval.completed"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_001_a_ConcurrentDuplicateStarts_CreateOneInstance()
    {
        var (award, requisitionId) = await AwardedAsync("5000");
        using var barrier = new Barrier(4);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var scope = provider.CreateAsyncScope();
            var activities = scope.ServiceProvider.GetRequiredService<ApprovalWorkflowActivities>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await activities.StartAsync(Start(award, requisitionId), default);
        })));

        results.Select(result => result.InstanceId).Distinct().ShouldHaveSingleItem();
        await using var context = fixture.CreateContext();
        (await context.Set<ApprovalInstance>().CountAsync(instance => instance.AwardId == award)).ShouldBe(1);
        (await context.Set<ApprovalPolicy>().SingleAsync(policy => policy.Version == 1)).FirstUsedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task SC_08_TwoGroupMembersDecideTheSameStep_OneDecision_OneAlreadyDecided()
    {
        approvers.Deactivate(manager);
        var (award, _) = await AwardedAsync("5000");
        await StartAndAssignAsync(award);
        var instance = await InstanceAsync(award);
        instance.CurrentStep!.AssigneeRole.ShouldBe(SeededRoles.ProcurementManager);
        var members = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var member in members)
            approvers.Add(member, SeededRoles.ProcurementManager);
        using var barrier = new Barrier(2);

        var results = await Task.WhenAll(members.Select(member => Task.Run(async () =>
        {
            actor.Current = member;
            await using var scope = provider.CreateAsyncScope();
            var handlers = scope.ServiceProvider.GetRequiredService<ApprovalDecisionHandlers>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(30));
            return await handlers.Handle(new DecideApprovalStepCommand(instance.Id, 1, instance.Version, "Approved", null), default);
        })));

        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Single(result => !result.IsSuccess).Error!.Code.ShouldBe(ProblemCodes.ApprovalStepAlreadyDecided);
        await using var context = fixture.CreateContext();
        (await context.Set<ApprovalHistoryEntry>().CountAsync(entry => entry.InstanceId == instance.Id && entry.Action == ApprovalInstance.ActionApproved))
            .ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_009_a_ConcurrentFinalizationOfARejection_ReleasesTheReservationOnce()
    {
        var (award, requisitionId) = await AwardedAsync("5000");
        await StartAndAssignAsync(award);
        var instance = await InstanceAsync(award);
        (await DecideAsync(instance.Id, 1, manager, "Rejected", Reason)).IsSuccess.ShouldBeTrue();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            ActivityAsync(activities => activities.FinalizeAsync(new ApprovalActivityRequest(award, "pg"), default)))));

        await using var context = fixture.CreateContext();
        var reservation = await context.Set<BudgetReservation>().Include(item => item.Changes).SingleAsync(item => item.RequisitionId == requisitionId);
        reservation.Status.ShouldBe(BudgetReservationStatus.Released);
        reservation.ReleaseReason.ShouldBe(BudgetReleaseReason.Rejected);
        reservation.Changes.Count(change => change.Operation == BudgetReservationOperation.Released).ShouldBe(1);
        (await context.Set<BudgetAllocation>().SingleAsync(item => item.Id == reservation.AllocationId)).ReservedAmount.ShouldBe(0m);
        (await context.Set<OutboxMessage>().CountAsync(message => message.EventType == "procurement.budget.reservation-released"
            && message.AggregateId == reservation.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task T_APR_012_b_SupplierSuspension_InvalidatesTheApproval_InTheSupplierCommit()
    {
        var (award, requisitionId) = await AwardedAsync("5000");
        await StartAndAssignAsync(award);
        var instance = await InstanceAsync(award);
        long supplierVersion;
        await using (var context = fixture.CreateContext())
            supplierVersion = (await context.Set<Supplier>().SingleAsync(item => item.Id == instance.SupplierId)).Version;

        actor.Current = buyer;
        await using (var scope = provider.CreateAsyncScope())
        {
            var suppliers = scope.ServiceProvider.GetRequiredService<SupplierCommandHandlers>();
            var suspended = await suppliers.Handle(new ChangeSupplierStatusCommand(instance.SupplierId, supplierVersion, SupplierStatusTransition.Suspend,
                "Synthetic suspension during approval"), default);
            suspended.IsSuccess.ShouldBeTrue(suspended.Error?.Message);
        }

        await using var check = fixture.CreateContext();
        (await check.Set<ApprovalInstance>().SingleAsync(item => item.Id == instance.Id)).Status.ShouldBe(ApprovalStatus.Invalidated);
        (await check.Set<PurchaseRequisition>().SingleAsync(item => item.Id == requisitionId)).Status.ShouldBe(RequisitionStatus.InSourcing);
        (await check.Set<RfqAward>().SingleAsync(item => item.Id == award)).Status.ShouldBe(AwardStatus.Invalidated);
        (await check.Set<RequestForQuotation>().SingleAsync(item => item.Id == instance.RfqId)).Status.ShouldBe(RfqStatus.Closed);
        (await check.Set<BudgetReservation>().SingleAsync(item => item.RequisitionId == requisitionId)).Status.ShouldBe(BudgetReservationStatus.Reserved);
    }

    [Fact]
    public async Task T_APR_016_b_ReconciliationQueries_FindAwardsWithoutInstances_AndPendingInstances()
    {
        var (award, requisitionId) = await AwardedAsync("5000");
        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IApprovalInstanceRepository>();
            (await repository.FindPendingAwardAsync(award, default)).ShouldBe(new PendingAwardReference(award, (await InstanceRfqAsync(requisitionId)), requisitionId,
                clock.UtcNow));
            (await repository.ListAwardsWithoutInstanceAsync(clock.UtcNow.AddMinutes(1), 500, default)).Select(item => item.AwardId).ShouldContain(award);
        }

        await StartAndAssignAsync(award);
        await using var after = provider.CreateAsyncScope();
        var instances = after.ServiceProvider.GetRequiredService<IApprovalInstanceRepository>();
        (await instances.ListAwardsWithoutInstanceAsync(clock.UtcNow.AddMinutes(1), 500, default)).Select(item => item.AwardId).ShouldNotContain(award);
        (await instances.ListPendingAsync(500, default)).Select(item => item.AwardId).ShouldContain(award);
        (await instances.ListPendingIdsForSupplierAsync((await InstanceAsync(award)).SupplierId, default)).ShouldHaveSingleItem();
    }

    private async Task<Guid> InstanceRfqAsync(Guid requisitionId)
    {
        await using var context = fixture.CreateContext();
        return await context.Set<RequestForQuotation>().Where(rfq => rfq.RequisitionId == requisitionId).Select(rfq => rfq.Id).SingleAsync();
    }

    private async Task<(Guid AwardId, Guid RequisitionId)> AwardedAsync(string unitPrice)
    {
        actor.Current = requester;
        await OpenBudgetAsync(100_000m);
        var requisition = await SubmittedAsync("1", unitPrice);
        actor.Current = buyer;
        var supplier = await ActiveSupplierAsync($"Approval Supplier {Guid.NewGuid():N}"[..30]);
        var rfq = await RfqAsync(requisition, supplier, unitPrice);
        var awarded = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers => handlers.Handle(
            new SubmitAwardCommand(rfq.Id, Version(rfq.Version), rfq.Comparison!.RecommendedQuoteRevisionId!.Value, null, null, null), default));
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);
        return (awarded.Value.Awards.Single().Id, requisition.Id);
    }

    private async Task StartAndAssignAsync(Guid award)
    {
        await using var context = fixture.CreateContext();
        var reference = await context.Set<RfqAward>().Where(item => item.Id == award)
            .Join(context.Set<RequestForQuotation>(), item => item.RfqId, rfq => rfq.Id, (_, rfq) => rfq.RequisitionId).SingleAsync();
        await ActivityAsync(activities => activities.StartAsync(Start(award, reference), default));
        (await ActivityAsync(activities => activities.AssignStepAsync(new ApprovalActivityRequest(award, "pg"), default))).Next.ShouldBe(ApprovalWorkflowStep.Wait);
    }

    private ApprovalStartRequest Start(Guid award, Guid requisitionId) =>
        new(new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, award, requisitionId, clock.UtcNow, "pg-approval"), "1.0");

    private async Task<ApprovalInstance> InstanceAsync(Guid award)
    {
        await using var context = fixture.CreateContext();
        return await context.Set<ApprovalInstance>().AsNoTracking().Include(item => item.Steps).SingleAsync(item => item.AwardId == award);
    }

    private async Task<Result<ApprovalInstanceView>> DecideAsync(Guid instanceId, int level, Guid decider, string decision, string? reason = null)
    {
        actor.Current = decider;
        await using var context = fixture.CreateContext();
        var version = (await context.Set<ApprovalInstance>().SingleAsync(item => item.Id == instanceId)).Version;
        return await Scoped<ApprovalDecisionHandlers, Result<ApprovalInstanceView>>(handlers =>
            handlers.Handle(new DecideApprovalStepCommand(instanceId, level, version, decision, reason), default));
    }

    private async Task<T> Query<T>(Func<ApprovalQueryHandlers, Task<T>> action, Guid caller)
    {
        actor.Current = caller;
        return await Scoped(action);
    }

    private async Task<ApprovalProgress> ActivityAsync(Func<ApprovalWorkflowActivities, Task<ApprovalProgress>> action)
    {
        actor.Current = null;
        return await Scoped(action);
    }

    private async Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<RfqView> RfqAsync(RequisitionView requisition, Guid supplier, string unitPrice)
    {
        // One supplier: above the 5,000.00 BRL competition threshold the buyer uses a Direct RFQ with the DirectPurchase waiver (SRC-006).
        var direct = decimal.Parse(unitPrice, CultureInfo.InvariantCulture) > 5_000m;
        var created = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new CreateRfqCommand(requisition.Id, direct ? "Direct" : null), default));
        created.IsSuccess.ShouldBeTrue(created.Error?.Message);
        var invited = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new InviteSupplierCommand(created.Value.Id, Version(created.Value.Version), supplier), default));
        var published = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new PublishRfqCommand(invited.Value.Id, Version(invited.Value.Version), Now.AddDays(3), direct ? Reason : null), default));
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        var quote = new QuoteRequest("BRL", [new QuoteLineRequest(1, "1", unitPrice)], null, null, null, new DateOnly(year, 5, 31), 7, "30 days", null,
            QuoteSource.Email, Now, []);
        var entered = await Scoped<QuoteCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new EnterQuoteCommand(published.Value.Id, Version(published.Value.Version), supplier, quote), default));
        entered.IsSuccess.ShouldBeTrue(entered.Error?.Message);
        clock.UtcNow = Now.AddDays(3);
        var closed = await Scoped<RfqCommandHandlers, Result<RfqView>>(handlers =>
            handlers.Handle(new CloseRfqCommand(entered.Value.Id, Version(entered.Value.Version), null), default));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        return closed.Value;
    }

    private async Task<Guid> ActiveSupplierAsync(string legalName)
    {
        var supplier = Supplier.CreateDraft(Guid.NewGuid(), legalName, null, "BR", SyntheticCnpj(), null, Now).Supplier!;
        supplier.ReplaceContacts([new ContactInput(null, "Synthetic Contact", "contact@example.invalid", null, null)], Now).ShouldBeNull();
        supplier.ReplaceAddresses([new AddressInput(null, new AddressValues(
            SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR"))], Now).ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(categoryId, true)], Now).ShouldBeNull();
        supplier.Activate(null, Now).ShouldBeNull();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISupplierRepository>().Add(supplier);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return supplier.Id;
    }

    private async Task OpenBudgetAsync(decimal amount)
    {
        await using var context = fixture.CreateContext();
        if (await context.Set<BudgetAllocation>().AnyAsync(item => item.CostCenterId == costCenterId))
            return;
        await using var scope = provider.CreateAsyncScope();
        var periods = scope.ServiceProvider.GetRequiredService<FiscalPeriodCommandHandlers>();
        var period = await periods.Handle(new CreateFiscalPeriodCommand(Code("FY"), "Synthetic fiscal year", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)), default);
        period.IsSuccess.ShouldBeTrue(period.Error?.Message);
        (await periods.Handle(new ChangeFiscalPeriodStatusCommand(period.Value.Id, 1, FiscalPeriodTransition.Open), default)).IsSuccess.ShouldBeTrue();
        var allocation = await scope.ServiceProvider.GetRequiredService<BudgetAllocationCommandHandlers>().Handle(
            new CreateBudgetAllocationCommand(period.Value.Id, costCenterId, "BRL", amount.ToString(CultureInfo.InvariantCulture)), default);
        allocation.IsSuccess.ShouldBeTrue(allocation.Error?.Message);
    }

    private async Task<RequisitionView> SubmittedAsync(string quantity, string price)
    {
        await using var scope = provider.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>();
        var draft = await drafts.Handle(new CreateRequisitionDraftCommand(costCenterId, "BRL", "Synthetic purpose for approval", new DateOnly(year, 6, 30)), default);
        draft.IsSuccess.ShouldBeTrue(draft.Error?.Message);
        var line = await drafts.Handle(new AddRequisitionLineCommand(draft.Value.Id, Version(draft.Value.Version),
            new RequisitionLineRequest(null, "Synthetic line", RequisitionLineKind.Good, categoryId, unitId, quantity, new MoneyInput(price, "BRL"))), default);
        line.IsSuccess.ShouldBeTrue(line.Error?.Message);
        var submitted = await scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>()
            .Handle(new SubmitRequisitionCommand(line.Value.Id, Version(line.Value.Version)), default);
        submitted.IsSuccess.ShouldBeTrue(submitted.Error?.Message);
        return submitted.Value;
    }

    private static long Version(string version) => long.Parse(version, CultureInfo.InvariantCulture);

    private static string Code(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(20, prefix.Length + 11)].ToUpperInvariant();

    private static string SyntheticCnpj()
    {
        var digits = Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture) + "0001";
        int Digit(string value, int[] weights)
        {
            var sum = value.Select((character, index) => (character - '0') * weights[index]).Sum() % 11;
            return sum < 2 ? 0 : 11 - sum;
        }

        var first = Digit(digits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var second = Digit(digits + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{digits}{first}{second}";
    }

    private sealed class MutableClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    /// <summary>The acting user per async flow, so parallel tasks act as different users.</summary>
    private sealed class SwitchableActor : IAuthenticatedUserService
    {
        private readonly AsyncLocal<Guid?> current = new();

        public Guid? Current
        {
            get => current.Value;
            set => current.Value = value;
        }

        public string? UserId => current.Value?.ToString();
        public string? UserName => current.Value is null ? null : "pg-approval";
    }

    private sealed class Trace : IRequestContext
    {
        public string CorrelationId => "pg-approval";
    }

    private sealed class Approvers : IApproverDirectory
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (bool Active, string Role)> users = new();

        public void Add(Guid user, string role) => users[user] = (true, role);

        public void Deactivate(Guid user) => users[user] = (false, users[user].Role);

        public Task<ApprovalCandidate?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(users.TryGetValue(userId, out var user) ? new ApprovalCandidate(userId, user.Active, true) : null);

        public Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(users.TryGetValue(userId, out var user) ? new HashSet<string> { user.Role } : new HashSet<string>());

        public Task<PagedResult<UserSummary>> SearchApproversAsync(string? search, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], page.Page, page.PageSize, 0));

        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class RunningWorkflow : IApprovalWorkflowClient
    {
        public Task<ApprovalWorkflowStartResult> StartAsync(ApprovalWorkflowInput input, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalWorkflowStartResult.Started);

        public Task<ApprovalWorkflowSignalResult> SignalAsync(Guid awardId, ApprovalSignal signal, CancellationToken cancellationToken) =>
            Task.FromResult(ApprovalWorkflowSignalResult.Delivered);

        public Task<ApprovalWorkflowStatus> GetStatusAsync(Guid awardId, CancellationToken cancellationToken) =>
            Task.FromResult(new ApprovalWorkflowStatus(ApprovalWorkflowNames.InstanceIdFor(awardId), ApprovalWorkflowRuntimeStatus.Running, "1.0", null, null));

        public Task<bool> TerminateAsync(Guid awardId, string reason, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class ActiveUsers : IAccountDirectory
    {
        public Task<VerifiedAccount?> VerifyCredentialsAsync(string userName, string password, CancellationToken cancellationToken) => Task.FromResult<VerifiedAccount?>(null);
        public Task<string?> GetSecurityStampFingerprintAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<AccountProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<AccountProfile?>(null);
        public Task<bool> IsActiveUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<PagedResult<UserSummary>> SearchUsersAsync(string? search, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], page.Page, page.PageSize, 0));
    }

    private sealed class AllPermissions : IPermissionReader
    {
        private static readonly IReadOnlySet<string> Granted = SeededRoles.PermissionsByRole.Values.SelectMany(permissions => permissions)
            .ToHashSet(StringComparer.Ordinal);

        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Granted);
    }

    private sealed class NoRates : IExchangeRateProvider
    {
        public Task<EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>> GetPtaxBulletinsAsync(
            string currency, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult(EnrichmentResult<IReadOnlyList<ExchangeRateBulletin>>.Failed(EnrichmentOutcome.Unavailable, EnrichmentProviders.BcbPtax, "synthetic"));
    }
}
