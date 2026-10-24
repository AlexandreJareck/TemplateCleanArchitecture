using System.Globalization;
using Microsoft.EntityFrameworkCore;
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
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Approvals;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Security;
using Template.Application.Services.Integrations;
using Template.Application.Workflow;
using Template.Domain.Approvals;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Domain.Sourcing;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Workflow;

namespace Template.WorkflowFunctions.Integration.Tests;

/// <summary>
/// The test process as the WebApi: real Application use cases on the environment's PostgreSQL and the HTTP workflow
/// client against the running host. A short policy version (60 s steps, reminder after 15 s, one escalation) is created
/// once so reminders, escalation and expiry happen within the test; it pins exactly like version 1 (APR-017).
/// </summary>
internal sealed class ApprovalScenario : IAsyncDisposable
{
    public static readonly TimeSpan StepDuration = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan ReminderAfter = TimeSpan.FromSeconds(15);
    private const string Reason = "Synthetic reason for the emulator scenario";

    private readonly WorkflowEnvironment environment;
    private readonly ServiceProvider provider;
    private readonly SwitchableActor actor = new();
    private Guid costCenterId;
    private Guid categoryId;
    private Guid unitId;

    private ApprovalScenario(WorkflowEnvironment environment, Func<IApprovalWorkflowClient, IApprovalWorkflowClient>? decorate)
    {
        this.environment = environment;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = environment.ApplicationConnectionString,
            ["ApprovalWorkflowSettings:Enabled"] = "true",
            ["ApprovalWorkflowSettings:BaseUrl"] = environment.HostBaseUrl,
            ["ApprovalWorkflowSettings:Key"] = environment.InternalKey,
            ["ApprovalWorkflowSettings:Timeout"] = "00:00:15"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationLayer();
        services.AddPersistenceInfrastructure(configuration, useInMemoryDatabase: false);
        services.AddWorkflowInfrastructure(configuration, useInMemoryClient: false);
        if (decorate is not null)
            services.AddTransient(provider => decorate(provider.GetRequiredService<HttpApprovalWorkflowClient>()));
        services.AddSingleton<IAuthenticatedUserService>(actor);
        services.AddSingleton<IRequestContext>(new Trace());
        services.AddSingleton<IAccountDirectory>(new ActiveUsers());
        services.AddSingleton<IPermissionReader>(new AllPermissions());
        services.AddSingleton<IApproverDirectory>(new GroupMembers());
        services.AddSingleton<IExchangeRateProvider>(new NoRates());
        foreach (var handler in new[]
                 {
                     typeof(FiscalPeriodCommandHandlers), typeof(BudgetAllocationCommandHandlers), typeof(RequisitionDraftCommandHandlers),
                     typeof(RequisitionTransitionHandlers), typeof(RfqCommandHandlers), typeof(QuoteCommandHandlers), typeof(ApprovalDecisionHandlers)
                 })
            services.AddScoped(handler);
        provider = services.BuildServiceProvider();
    }

    public static async Task<ApprovalScenario> CreateAsync(WorkflowEnvironment environment, Func<IApprovalWorkflowClient, IApprovalWorkflowClient>? decorate = null)
    {
        var scenario = new ApprovalScenario(environment, decorate);
        await scenario.SeedAsync();
        return scenario;
    }

    public IApprovalWorkflowClient Workflow => provider.GetRequiredService<IApprovalWorkflowClient>();

    public ValueTask DisposeAsync() => provider.DisposeAsync();

    /// <summary>Requisition → RFQ → award pending approval. Above 5,000.00 BRL a Direct RFQ with the DirectPurchase waiver is used.</summary>
    public async Task<(Guid AwardId, Guid RequisitionId)> AwardAsync(decimal amount)
    {
        actor.Current = Guid.NewGuid();
        var requisition = await SubmittedAsync(amount);
        actor.Current = Guid.NewGuid();
        var supplier = await ActiveSupplierAsync();
        var direct = amount > 5_000m;
        var created = await Rfqs(handlers => handlers.Handle(new CreateRfqCommand(requisition.Id, direct ? "Direct" : null), default));
        created.IsSuccess.ShouldBeTrue(created.Error?.Message);
        var invited = await Rfqs(handlers => handlers.Handle(new InviteSupplierCommand(created.Value.Id, V(created.Value.Version), supplier), default));
        var published = await Rfqs(handlers => handlers.Handle(
            new PublishRfqCommand(invited.Value.Id, V(invited.Value.Version), DateTime.UtcNow.AddDays(2), direct ? Reason : null), default));
        published.IsSuccess.ShouldBeTrue(published.Error?.Message);
        await using (var scope = provider.CreateAsyncScope())
        {
            var quote = new QuoteRequest("BRL", [new QuoteLineRequest(1, "1", amount.ToString(CultureInfo.InvariantCulture))], null, null, null,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), 5, "30 days", null, QuoteSource.Email, DateTime.UtcNow.AddSeconds(-1), []);
            var entered = await scope.ServiceProvider.GetRequiredService<QuoteCommandHandlers>()
                .Handle(new EnterQuoteCommand(published.Value.Id, V(published.Value.Version), supplier, quote), default);
            entered.IsSuccess.ShouldBeTrue(entered.Error?.Message);
            published = entered;
        }

        var closed = await Rfqs(handlers => handlers.Handle(new CloseRfqCommand(published.Value.Id, V(published.Value.Version), Reason), default));
        closed.IsSuccess.ShouldBeTrue(closed.Error?.Message);
        var awarded = await Rfqs(handlers => handlers.Handle(new SubmitAwardCommand(closed.Value.Id, V(closed.Value.Version),
            closed.Value.Comparison!.RecommendedQuoteRevisionId!.Value, null, null, null), default));
        awarded.IsSuccess.ShouldBeTrue(awarded.Error?.Message);
        return (awarded.Value.Awards.Single().Id, requisition.Id);
    }

    /// <summary>The Worker's part: the idempotent start with the deterministic instance ID (APR-001).</summary>
    public Task<ApprovalWorkflowStartResult> StartAsync(Guid awardId, Guid requisitionId) =>
        Workflow.StartAsync(new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, awardId, requisitionId, DateTime.UtcNow, $"emulator-{awardId:N}"), default);

    public async Task<Result<ApprovalInstanceView>> DecideAsync(Guid awardId, Guid decider, string decision, string? reason = null)
    {
        actor.Current = decider;
        var instance = await InstanceAsync(awardId);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApprovalDecisionHandlers>()
            .Handle(new DecideApprovalStepCommand(instance!.Id, instance.CurrentLevel, instance.Version, decision, reason), default);
    }

    public async Task<Result<ApprovalInstanceView>> CancelAsync(Guid awardId)
    {
        actor.Current = Guid.NewGuid();
        var instance = await InstanceAsync(awardId);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApprovalDecisionHandlers>()
            .Handle(new CancelApprovalCommand(instance!.Id, instance.Version, Reason), default);
    }

    public async Task<ApprovalReconciliationView> ReconcileAsync(bool repair)
    {
        actor.Current = null;
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApprovalReconciliation>().RunAsync(repair, 500, default);
    }

    public async Task<ApprovalInstance?> InstanceAsync(Guid awardId)
    {
        await using var context = Context();
        return await context.Set<ApprovalInstance>().AsNoTracking().Include(item => item.Steps).Include(item => item.History)
            .SingleOrDefaultAsync(item => item.AwardId == awardId);
    }

    /// <summary>Polls the database until <paramref name="condition"/> holds for the award's instance.</summary>
    public async Task<ApprovalInstance> WaitForAsync(Guid awardId, Func<ApprovalInstance, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (await InstanceAsync(awardId) is { } instance && condition(instance))
                return instance;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The approval of award {awardId} did not reach the expected state in {timeout}. Host log:\n{await environment.HostLogsAsync()}");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    public async Task<ApprovalWorkflowStatus> WaitForRuntimeAsync(Guid awardId, ApprovalWorkflowRuntimeStatus expected, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var status = await Workflow.GetStatusAsync(awardId, default);
            if (status.RuntimeStatus == expected)
                return status;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The orchestration of award {awardId} is {status.RuntimeStatus}, expected {expected}.");
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }

    public ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(environment.ApplicationConnectionString).Options, actor);

    private async Task SeedAsync()
    {
        await using var context = Context();
        var now = DateTime.UtcNow;
        if (!await context.Set<ApprovalPolicy>().AnyAsync(policy => policy.Version == 2))
        {
            var (policy, error) = ApprovalPolicy.Create(Guid.NewGuid(), 2, now, "BRL",
                [
                    new ApprovalPolicyLevelInput(1, 0m, ApproverResolver.CostCenterManager, null),
                    new ApprovalPolicyLevelInput(2, 10_000m, ApproverResolver.DepartmentHead, null)
                ], StepDuration, ReminderAfter, SeededRoles.ProcurementManager, 1, "Emulator test durations", null, now, now);
            error.ShouldBeNull();
            context.Add(policy!);
        }

        // Managers and heads are not Identity users here, so assignments fall back to the ProcurementManager group (APR-006).
        var department = Department.Create(Guid.NewGuid(), Code("D"), "Emulator department", Guid.NewGuid(), null, now).Department!;
        var costCenter = CostCenter.Create(Guid.NewGuid(), Code("C"), "Emulator cost center", department, Guid.NewGuid(), null, now).CostCenter!;
        var category = Category.Create(Guid.NewGuid(), Code("G"), "Emulator category", null, now).Category!;
        var unit = UnitOfMeasure.Create(Guid.NewGuid(), Code("U"), "Emulator unit", 0, null, now).Unit!;
        context.AddRange(department, costCenter, category, unit);
        await context.SaveChangesAsync();
        (costCenterId, categoryId, unitId) = (costCenter.Id, category.Id, unit.Id);

        actor.Current = Guid.NewGuid();
        await using var scope = provider.CreateAsyncScope();
        var year = now.Year;
        var periods = scope.ServiceProvider.GetRequiredService<FiscalPeriodCommandHandlers>();
        var openPeriod = await context.Set<Template.Domain.Budgeting.FiscalPeriod>().AsNoTracking()
            .FirstOrDefaultAsync(period => period.StartDate <= DateOnly.FromDateTime(now) && period.EndDate >= DateOnly.FromDateTime(now));
        var periodId = openPeriod?.Id;
        if (periodId is null)
        {
            var period = await periods.Handle(new CreateFiscalPeriodCommand(Code("FY"), "Emulator fiscal year", new DateOnly(year, 1, 1), new DateOnly(year, 12, 31)), default);
            period.IsSuccess.ShouldBeTrue(period.Error?.Message);
            (await periods.Handle(new ChangeFiscalPeriodStatusCommand(period.Value.Id, 1, FiscalPeriodTransition.Open), default)).IsSuccess.ShouldBeTrue();
            periodId = period.Value.Id;
        }

        var allocation = await scope.ServiceProvider.GetRequiredService<BudgetAllocationCommandHandlers>().Handle(
            new CreateBudgetAllocationCommand(periodId.Value, costCenterId, "BRL", "1000000"), default);
        allocation.IsSuccess.ShouldBeTrue(allocation.Error?.Message);
    }

    private async Task<RequisitionView> SubmittedAsync(decimal amount)
    {
        await using var scope = provider.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<RequisitionDraftCommandHandlers>();
        var draft = await drafts.Handle(new CreateRequisitionDraftCommand(costCenterId, "BRL", "Synthetic purpose for the emulator",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60))), default);
        draft.IsSuccess.ShouldBeTrue(draft.Error?.Message);
        var line = await drafts.Handle(new AddRequisitionLineCommand(draft.Value.Id, V(draft.Value.Version), new RequisitionLineRequest(
            null, "Synthetic line", RequisitionLineKind.Good, categoryId, unitId, "1", new MoneyInput(amount.ToString(CultureInfo.InvariantCulture), "BRL"))), default);
        line.IsSuccess.ShouldBeTrue(line.Error?.Message);
        var submitted = await scope.ServiceProvider.GetRequiredService<RequisitionTransitionHandlers>()
            .Handle(new SubmitRequisitionCommand(line.Value.Id, V(line.Value.Version)), default);
        submitted.IsSuccess.ShouldBeTrue(submitted.Error?.Message);
        return submitted.Value;
    }

    private async Task<Guid> ActiveSupplierAsync()
    {
        var now = DateTime.UtcNow;
        var supplier = Supplier.CreateDraft(Guid.NewGuid(), $"Emulator Supplier {Guid.NewGuid():N}"[..30], null, "BR", SyntheticCnpj(), null, now).Supplier!;
        supplier.ReplaceContacts([new ContactInput(null, "Synthetic Contact", "contact@example.invalid", null, null)], now).ShouldBeNull();
        supplier.ReplaceAddresses([new AddressInput(null, new AddressValues(
            SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "BR"))], now).ShouldBeNull();
        supplier.SetCategories([new MasterDataReference(categoryId, true)], now).ShouldBeNull();
        supplier.Activate(null, now).ShouldBeNull();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ISupplierRepository>().Add(supplier);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return supplier.Id;
    }

    private async Task<Result<RfqView>> Rfqs(Func<RfqCommandHandlers, Task<Result<RfqView>>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<RfqCommandHandlers>());
    }

    private static long V(string version) => long.Parse(version, CultureInfo.InvariantCulture);

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

    private sealed class SwitchableActor : IAuthenticatedUserService
    {
        private readonly AsyncLocal<Guid?> current = new();

        public Guid? Current
        {
            get => current.Value;
            set => current.Value = value;
        }

        public string? UserId => current.Value?.ToString();
        public string? UserName => current.Value is null ? null : "emulator-user";
    }

    private sealed class Trace : IRequestContext
    {
        public string CorrelationId { get; } = $"emulator-{Guid.NewGuid():N}";
    }

    /// <summary>Every acting user is an active member of the ProcurementManager group.</summary>
    private sealed class GroupMembers : IApproverDirectory
    {
        public Task<ApprovalCandidate?> GetCandidateAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<ApprovalCandidate?>(new ApprovalCandidate(userId, true, true));

        public Task<IReadOnlySet<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { SeededRoles.ProcurementManager });

        public Task<PagedResult<UserSummary>> SearchApproversAsync(string? search, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], page.Page, page.PageSize, 0));

        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
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
