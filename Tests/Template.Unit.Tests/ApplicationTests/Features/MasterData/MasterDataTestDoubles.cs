using Microsoft.Extensions.Logging.Abstractions;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Catalog;
using Template.Application.Interfaces.Integrations;
using Template.Application.Interfaces.Organization;
using Template.Application.Interfaces.Suppliers;
using Template.Application.Services.MasterData;
using Template.Domain.Catalog;
using Template.Domain.Organization;
using Template.Domain.Suppliers;
using Template.Unit.Tests.ApplicationTests.Features.Access;

namespace Template.Unit.Tests.ApplicationTests.Features.MasterData;

/// <summary>In-memory port fakes for the Phase 25 master-data handlers. All data is synthetic.</summary>
internal sealed class MasterDataTestDoubles
{
    public static readonly DateTime Now = AccessTestDoubles.Start;

    public AccessTestDoubles.MutableClock Clock { get; } = new(Now);
    public AccessTestDoubles.FakeAuditTrail Audit { get; } = new();
    public AccessTestDoubles.FakeAccounts Accounts { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeUser User { get; } = new();
    public FakeDepartments Departments { get; } = new();
    public FakeCostCenters CostCenters { get; } = new();
    public FakeSuppliers Suppliers { get; } = new();
    public FakeCategories Categories { get; } = new();
    public FakeUnits Units { get; } = new();
    public FakeItems Items { get; } = new();
    public FakeSettings Settings { get; } = new();
    public FakeLock SettingsLock { get; } = new();
    public FakeRegistry Registry { get; } = new();
    public FakePostalCodes PostalCodes { get; } = new();

    public MasterDataWriter Writer => new(UnitOfWork, Audit, Clock, new AccessTestDoubles.FixedRequestContext(), User);

    public static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;

    internal sealed class FakeUser : IAuthenticatedUserService
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string? UserId => Id.ToString();
        public string? UserName => "synthetic-user";
    }

    internal sealed class FakeUnitOfWork : IUnitOfWork
    {
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

        public void DiscardChanges() => Discards++;
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal abstract class FakeRepository<T>(Func<T, Guid> id, Func<T, long> version) where T : class
    {
        public List<T> Items { get; } = [];

        /// <summary>Version reported after a conflict, as if another writer had committed.</summary>
        public long? ConcurrentVersion { get; set; }

        public void Add(T item) => Items.Add(item);

        public Task<T?> FindAsync(Guid key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Items.SingleOrDefault(item => id(item) == key));
        }

        public Task<long?> GetVersionAsync(Guid key, CancellationToken cancellationToken) =>
            Task.FromResult(ConcurrentVersion ?? Items.Where(item => id(item) == key).Select(item => (long?)version(item)).SingleOrDefault());

        protected Task<PagedResult<T>> Page(IEnumerable<T> source, PageRequest page)
        {
            var all = source.ToArray();
            return Task.FromResult(new PagedResult<T>(all.Skip(page.Skip).Take(page.PageSize).ToArray(), page.Page, page.PageSize, all.Length));
        }
    }

    internal sealed class FakeDepartments() : FakeRepository<Department>(item => item.Id, item => item.Version), IDepartmentRepository
    {
        public DepartmentFilter? LastFilter { get; private set; }

        public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(Items.Any(item => item.Code == normalizedCode));

        public Task<PagedResult<Department>> ListAsync(DepartmentFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Page(Items.Where(item => filter.IsActive is null || item.IsActive == filter.IsActive), page);
        }
    }

    internal sealed class FakeCostCenters() : FakeRepository<CostCenter>(item => item.Id, item => item.Version), ICostCenterRepository
    {
        public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(Items.Any(item => item.Code == normalizedCode));

        public Task<bool> AnyActiveInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.DepartmentId == departmentId && item.IsActive));

        public Task<PagedResult<CostCenter>> ListAsync(CostCenterFilter filter, PageRequest page, CancellationToken cancellationToken) =>
            Page(Items.Where(item => filter.DepartmentId is null || item.DepartmentId == filter.DepartmentId), page);
    }

    internal sealed class FakeSuppliers() : FakeRepository<Supplier>(item => item.Id, item => item.Version), ISupplierRepository
    {
        public SupplierFilter? LastFilter { get; private set; }

        public Task<bool> TaxIdExistsAsync(string country, string normalizedTaxId, Guid? excludingId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.Country == country && item.TaxId == normalizedTaxId && item.Id != excludingId));

        public Task<PagedResult<Supplier>> ListAsync(SupplierFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Page(Items, page);
        }
    }

    internal sealed class FakeCategories() : FakeRepository<Category>(item => item.Id, item => item.Version), ICategoryRepository
    {
        public Task<IReadOnlyList<Category>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Category>>(Items.Where(item => ids.Contains(item.Id)).ToArray());

        public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(Items.Any(item => item.Code == normalizedCode));

        public Task<PagedResult<Category>> ListAsync(CategoryFilter filter, PageRequest page, CancellationToken cancellationToken) => Page(Items, page);
    }

    internal sealed class FakeUnits() : FakeRepository<UnitOfMeasure>(item => item.Id, item => item.Version), IUnitOfMeasureRepository
    {
        public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(Items.Any(item => item.Code == normalizedCode));

        public Task<PagedResult<UnitOfMeasure>> ListAsync(UnitOfMeasureFilter filter, PageRequest page, CancellationToken cancellationToken) => Page(Items, page);
    }

    internal sealed class FakeItems() : FakeRepository<CatalogItem>(item => item.Id, item => item.Version), ICatalogItemRepository
    {
        public CatalogItemFilter? LastFilter { get; private set; }

        public Task<bool> CodeExistsAsync(string normalizedCode, CancellationToken cancellationToken) => Task.FromResult(Items.Any(item => item.Code == normalizedCode));

        public Task<PagedResult<CatalogItem>> ListAsync(CatalogItemFilter filter, PageRequest page, CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Page(Items, page);
        }
    }

    internal sealed class FakeSettings : IOrganizationSettingsRepository
    {
        public OrganizationSettings? Current { get; set; } = OrganizationSettings.CreateDefault(Now);

        public Task<OrganizationSettings?> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Current);
    }

    internal sealed class FakeLock : IOrganizationSettingsLock
    {
        public bool HasAllocations { get; set; }

        public Task<bool> HasBudgetAllocationsAsync(CancellationToken cancellationToken) => Task.FromResult(HasAllocations);
    }

    internal sealed class FakeRegistry : ISupplierRegistryLookup
    {
        public EnrichmentResult<CompanyRegistrationSuggestion> Result { get; set; } =
            EnrichmentResult<CompanyRegistrationSuggestion>.Failed(EnrichmentOutcome.Unavailable, "BrasilAPI", "disabled");

        public List<string> Calls { get; } = [];

        public Task<EnrichmentResult<CompanyRegistrationSuggestion>> LookupAsync(string cnpj, CancellationToken cancellationToken)
        {
            Calls.Add(cnpj);
            return Task.FromResult(Result);
        }
    }

    internal sealed class FakePostalCodes : IPostalCodeLookup
    {
        public EnrichmentResult<PostalAddressSuggestion> Result { get; set; } =
            EnrichmentResult<PostalAddressSuggestion>.Failed(EnrichmentOutcome.Unavailable, "BrasilAPI", "disabled");

        public Task<EnrichmentResult<PostalAddressSuggestion>> LookupAsync(string postalCode, CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }
}
