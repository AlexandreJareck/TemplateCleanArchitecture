using Microsoft.Extensions.Logging.Abstractions;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Features.Requisitions;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Access;
using Template.Application.Interfaces.Budgeting;
using Template.Application.Interfaces.Requisitions;
using Template.Application.Interfaces.Messaging;
using Template.Application.Messaging.Procurement;
using Template.Application.Security;
using Template.Application.Services.Procurement;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Unit.Tests.ApplicationTests.Features.Access;
using Template.Unit.Tests.ApplicationTests.Features.MasterData;

namespace Template.Unit.Tests.ApplicationTests.Features.Procurement;

/// <summary>
/// In-memory port fakes for the Phase 27 budget and requisition handlers. The clock starts on 2027-03-01 13:00 UTC
/// (10:00 in America/Sao_Paulo); an Open period covers 2027 and a BRL allocation of 1,000.00 exists for the cost
/// center. All data is synthetic.
/// </summary>
internal sealed class ProcurementTestDoubles
{
    public static readonly DateTime Now = new(2027, 3, 1, 13, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = new(2027, 3, 1);

    public ProcurementTestDoubles()
    {
        Settings.Current = OrganizationSettings.CreateDefault(Now);
        var department = Department.Create(Guid.NewGuid(), "OPS", "Operations", null, null, Now).Department!;
        CostCenter = Template.Domain.Organization.CostCenter.Create(Guid.NewGuid(), "CC-OPS", "Operations", department, Manager, null, Now).CostCenter!;
        CostCenters.Add(CostCenter);
        Period = FiscalPeriod.Create(Guid.NewGuid(), "FY27", "Fiscal year 2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), null, Now).Period!;
        Period.Open(Now);
        Periods.Items.Add(Period);
        Allocation = BudgetAllocation.Create(Guid.NewGuid(), Period, new MasterDataReference(CostCenter.Id, true), "BRL", true, 1000m, null, Now).Allocation!;
        Allocations.Items.Add(Allocation);
        Category = Template.Domain.Catalog.Category.Create(Guid.NewGuid(), "IT", "IT supplies", null, Now).Category!;
        Unit = UnitOfMeasure.Create(Guid.NewGuid(), "UN", "Unit", 0, null, Now).Unit!;
        References.Categories.Add(Category);
        References.Units.Add(Unit);
        Accounts.ActiveUsers.Add(Requester.Id);
        Permissions.Grant(Requester.Id, SeededRoles.PermissionsByRole[SeededRoles.Requester]);
    }

    public Actor Requester { get; } = new();
    public Guid Manager { get; } = Guid.NewGuid();
    public CostCenter CostCenter { get; }
    public FiscalPeriod Period { get; }
    public BudgetAllocation Allocation { get; }
    public Category Category { get; }
    public UnitOfMeasure Unit { get; }

    public AccessTestDoubles.MutableClock Clock { get; } = new(Now);
    public AccessTestDoubles.FakeAuditTrail Audit { get; } = new();
    public AccessTestDoubles.FakeAccounts Accounts { get; } = new();
    public TransactionalUnitOfWork UnitOfWork { get; } = new();
    public CurrentUser User { get; } = new();
    public FakePermissions Permissions { get; } = new();
    public MasterDataTestDoubles.FakeCostCenters CostCenters { get; } = new();
    public MasterDataTestDoubles.FakeSettings Settings { get; } = new();
    public FakePeriods Periods { get; } = new();
    public FakeAllocations Allocations { get; } = new();
    public FakeRequisitions Requisitions { get; } = new();
    public FakeNumbers Numbers { get; } = new();
    public FakeScope Scope { get; } = new();
    public FakeReferences References { get; } = new();
    public FakeOutbox Outbox { get; } = new();

    public ProcurementWriter NewWriter() => new(
        UnitOfWork, Audit, Clock, new AccessTestDoubles.FixedRequestContext(), User, Permissions, Settings, Outbox, NullLogger<ProcurementWriter>.Instance);

    public RequisitionDraftCommandHandlers Drafts()
    {
        var writer = NewWriter();
        return new(Requisitions, Numbers, References, CostCenters, new RequisitionAccess(writer, Scope), writer);
    }

    public RequisitionTransitionHandlers Transitions()
    {
        var writer = NewWriter();
        return new(Requisitions, Allocations, Periods, CostCenters, References, Numbers, Accounts, new RequisitionAccess(writer, Scope), writer);
    }

    public RequisitionQueryHandlers Queries()
    {
        var writer = NewWriter();
        return new(Requisitions, Allocations, Periods, new RequisitionAccess(writer, Scope), writer);
    }

    public void ActAs(Actor actor) => User.Id = actor.Id;

    public void ActAsSystem() => User.Id = null;

    internal sealed class Actor
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    internal sealed class CurrentUser : IAuthenticatedUserService
    {
        public Guid? Id { get; set; }
        public string? UserId => Id?.ToString();
        public string? UserName => Id is null ? null : "synthetic-user";
    }

    internal sealed class FakeOutbox : IIntegrationEventOutbox
    {
        public List<IntegrationEventEnvelope> Events { get; } = [];
        public void Add(IntegrationEventEnvelope envelope) => Events.Add(envelope);
    }

    internal sealed class FakePermissions : IPermissionReader
    {
        private readonly Dictionary<Guid, HashSet<string>> grants = [];

        public void Grant(Guid userId, IEnumerable<string> permissions)
        {
            if (!grants.TryGetValue(userId, out var set))
                grants[userId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.UnionWith(permissions);
        }

        public Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(grants.TryGetValue(userId, out var set) ? set : new HashSet<string>());
    }

    /// <summary>Records transaction calls; <see cref="ThrowConflict"/> simulates a lost optimistic race at commit.</summary>
    internal sealed class TransactionalUnitOfWork : IUnitOfWork
    {
        public int Begins { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public int Saves { get; private set; }
        public int Discards { get; private set; }
        public bool ThrowConflict { get; set; }
        public string? UniqueViolation { get; set; }
        public Action? BeforeSave { get; set; }

        public Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeSave?.Invoke();
            if (ThrowConflict)
                throw new ConcurrencyConflictException("synthetic conflict");
            if (UniqueViolation is { } constraint)
                throw new UniqueConstraintViolationException(constraint);
            Saves++;
            return Task.FromResult(true);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            Begins++;
            return Task.CompletedTask;
        }

        public async Task<bool> CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var saved = await SaveChangesAsync(cancellationToken);
                Commits++;
                return saved;
            }
            catch
            {
                Rollbacks++;
                throw;
            }
        }

        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
        {
            Rollbacks++;
            return Task.CompletedTask;
        }

        public void DiscardChanges() => Discards++;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FakePeriods : IFiscalPeriodRepository
    {
        public List<FiscalPeriod> Items { get; } = [];
        public int ShareLocks { get; private set; }
        public int UpdateLocks { get; private set; }

        public void Add(FiscalPeriod period) => Items.Add(period);

        public Task<FiscalPeriod?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<FiscalPeriod?> FindForUpdateAsync(Guid id, CancellationToken cancellationToken)
        {
            UpdateLocks++;
            return FindAsync(id, cancellationToken);
        }

        public Task<FiscalPeriod?> FindOpenContainingForShareAsync(DateOnly date, CancellationToken cancellationToken)
        {
            ShareLocks++;
            return FindOpenContainingAsync(date, cancellationToken);
        }

        public Task<FiscalPeriod?> FindOpenContainingAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => item.AcceptsReservationsOn(date)));

        public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(item => item.Id == id).Select(item => (long?)item.Version).SingleOrDefault());

        public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(Items.Any(item => item.Code == normalizedCode));

        public Task<bool> OverlapsAsync(DateOnly startDate, DateOnly endDate, Guid? excludingId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.Id != excludingId && item.Overlaps(startDate, endDate)));

        public Task<PagedResult<FiscalPeriod>> ListAsync(FiscalPeriodFilter filter, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<FiscalPeriod>(
                Items.Where(item => filter.Statuses is null || filter.Statuses.Contains(item.Status)).ToArray(), page.Page, page.PageSize, Items.Count));
    }

    internal sealed class FakeAllocations : IBudgetAllocationRepository
    {
        public List<BudgetAllocation> Items { get; } = [];
        public int UpdateLocks { get; private set; }
        public long? ConcurrentVersion { get; set; }

        public void Add(BudgetAllocation allocation) => Items.Add(allocation);

        public Task<BudgetAllocation?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<BudgetAllocation?> FindForUpdateAsync(Guid id, Guid requisitionId, CancellationToken cancellationToken)
        {
            UpdateLocks++;
            return FindAsync(id, cancellationToken);
        }

        public Task<Guid?> FindIdAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken) =>
            Task.FromResult(ByKey(fiscalPeriodId, costCenterId, currency)?.Id);

        public Task<BudgetAllocation?> FindByKeyAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken) =>
            Task.FromResult(ByKey(fiscalPeriodId, costCenterId, currency));

        public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ConcurrentVersion ?? Items.Where(item => item.Id == id).Select(item => (long?)item.Version).SingleOrDefault());

        public Task<bool> ExistsAsync(Guid fiscalPeriodId, Guid costCenterId, string currency, CancellationToken cancellationToken) =>
            Task.FromResult(ByKey(fiscalPeriodId, costCenterId, currency) is not null);

        public Task<bool> AnyAsync(CancellationToken cancellationToken) => Task.FromResult(Items.Count > 0);

        public Task<bool> AnyReservedInPeriodAsync(Guid fiscalPeriodId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.FiscalPeriodId == fiscalPeriodId && item.ReservedAmount > 0));

        public Task<PagedResult<BudgetAllocation>> ListAsync(BudgetAllocationFilter filter, PageRequest page, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<BudgetAllocation>(Items, page.Page, page.PageSize, Items.Count));

        public Task<PagedResult<BudgetReservation>> ListReservationsAsync(
            Guid allocationId, BudgetReservationFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            var reservations = Items.Where(item => item.Id == allocationId).SelectMany(item => item.Reservations)
                .Where(reservation => filter.Statuses is null || filter.Statuses.Contains(reservation.Status)).ToArray();
            return Task.FromResult(new PagedResult<BudgetReservation>(reservations, page.Page, page.PageSize, reservations.Length));
        }

        public Task<IReadOnlyList<BudgetReservation>> ListReservationsOfRequisitionAsync(Guid requisitionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BudgetReservation>>(Items.SelectMany(item => item.Reservations).Where(reservation => reservation.RequisitionId == requisitionId).ToArray());

        private BudgetAllocation? ByKey(Guid fiscalPeriodId, Guid costCenterId, string currency) =>
            Items.SingleOrDefault(item => item.FiscalPeriodId == fiscalPeriodId && item.CostCenterId == costCenterId && item.Currency == currency);
    }

    internal sealed class FakeRequisitions : IPurchaseRequisitionRepository
    {
        public List<PurchaseRequisition> Items { get; } = [];
        public RequisitionFilter? LastFilter { get; private set; }
        public long? ConcurrentVersion { get; set; }

        public void Add(PurchaseRequisition requisition) => Items.Add(requisition);

        public void Remove(PurchaseRequisition requisition) => Items.Remove(requisition);

        public Task<PurchaseRequisition?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));

        public Task<long?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(ConcurrentVersion ?? Items.Where(item => item.Id == id).Select(item => (long?)item.Version).SingleOrDefault());

        public Task<PagedResult<PurchaseRequisition>> ListAsync(RequisitionFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            var visible = Items.Where(item => filter.Visibility.CanRead(item.RequesterId, item.CostCenterId, item.DepartmentId)
                && (filter.RequesterId is null || item.RequesterId == filter.RequesterId)
                && (filter.Statuses is null || filter.Statuses.Contains(item.Status))).ToArray();
            return Task.FromResult(new PagedResult<PurchaseRequisition>(visible, page.Page, page.PageSize, visible.Length));
        }

        public Task<IReadOnlyList<Guid>> ListDueForExpiryAsync(DateOnly neededByBefore, int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(Items
                .Where(item => item.Status is RequisitionStatus.Submitted or RequisitionStatus.InSourcing && item.NeededBy < neededByBefore)
                .Select(item => item.Id).Take(max).ToArray());
    }

    internal sealed class FakeNumbers : IRequisitionNumberAllocator
    {
        private long next;

        public Task<long> NextAsync(int year, CancellationToken cancellationToken) => Task.FromResult(++next);
    }

    internal sealed class FakeScope : IOrganizationScopeReader
    {
        public Dictionary<Guid, OrganizationScope> Scopes { get; } = [];

        public Task<OrganizationScope> GetScopeAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Scopes.TryGetValue(userId, out var scope) ? scope : new OrganizationScope([], []));
    }

    internal sealed class FakeReferences : IRequisitionReferenceReader
    {
        public List<CatalogItem> Items { get; } = [];
        public List<Category> Categories { get; } = [];
        public List<UnitOfMeasure> Units { get; } = [];

        public Task<CatalogItemReferenceState?> FindCatalogItemAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(item => item.Id == id)
                .Select(item => new CatalogItemReferenceState(item.Id, item.Description, item.Kind.ToString(), item.CategoryId, item.UnitOfMeasureId,
                    item.IsActive, item.ReferencePriceAmount, item.ReferencePriceCurrency))
                .SingleOrDefault());

        public Task<CatalogReferenceState?> FindCategoryAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.Where(category => category.Id == id)
                .Select(category => new CatalogReferenceState(category.Id, category.Code, category.Name, category.IsActive)).SingleOrDefault());

        public Task<UnitReferenceState?> FindUnitAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Units.Where(unit => unit.Id == id)
                .Select(unit => new UnitReferenceState(unit.Id, unit.Code, unit.Name, unit.AllowedDecimals, unit.IsActive)).SingleOrDefault());

        public Task<IReadOnlyDictionary<Guid, bool>> GetActiveStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            var states = new Dictionary<Guid, bool>();
            foreach (var item in Items.Where(item => ids.Contains(item.Id)))
                states[item.Id] = item.IsActive;
            foreach (var category in Categories.Where(category => ids.Contains(category.Id)))
                states[category.Id] = category.IsActive;
            foreach (var unit in Units.Where(unit => ids.Contains(unit.Id)))
                states[unit.Id] = unit.IsActive;
            return Task.FromResult<IReadOnlyDictionary<Guid, bool>>(states);
        }
    }
}
