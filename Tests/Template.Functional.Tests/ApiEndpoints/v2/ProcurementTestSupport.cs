using System.Globalization;
using System.Net;
using System.Text.Json;
using Shouldly;
using Template.Application.Security;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Test host for the Phase 27 endpoints. One <c>Open</c> fiscal period around today is created once per host (periods
/// never overlap, BUD-001); every scenario then gets its own cost center and allocation, so tests stay independent.
/// </summary>
public sealed class ProcurementWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Guid? periodId;

    internal async Task<Guid> OpenPeriodAsync(ApiUser budgetController)
    {
        await gate.WaitAsync();
        try
        {
            if (periodId is { } existing)
                return existing;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var (period, etag) = await budgetController.CreateAsync(ProcurementRoutes.Periods, new
            {
                code = MasterDataRoutes.NewCode("FY"),
                name = "Synthetic fiscal period",
                startDate = today.AddDays(-400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                endDate = today.AddDays(400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
            var id = period.GetProperty("id").GetGuid();
            using var opened = await budgetController.PostAsync($"{ProcurementRoutes.Periods}/{id}/open", null, etag);
            opened.StatusCode.ShouldBe(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
            periodId = id;
            return id;
        }
        finally
        {
            gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            gate.Dispose();
        base.Dispose(disposing);
    }
}

internal static class ProcurementRoutes
{
    public const string Periods = "/api/v2/fiscal-periods";
    public const string Allocations = "/api/v2/budget-allocations";
    public const string Requisitions = "/api/v2/purchase-requisitions";
}

/// <summary>Synthetic organization, catalog and budget data plus one requester for a single test.</summary>
internal sealed class ProcurementScenario
{
    public const string Purpose = "Replacement laptops for the support team";

    private ProcurementScenario(ApiUser administrator, ApiUser budget, ApiUser requester)
    {
        Administrator = administrator;
        Budget = budget;
        Requester = requester;
    }

    public ApiUser Administrator { get; }
    public ApiUser Budget { get; }
    public ApiUser Requester { get; }
    public Guid PeriodId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public Guid CostCenterId { get; private set; }
    public string CostCenterETag { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid AllocationId { get; private set; }

    public static async Task<ProcurementScenario> CreateAsync(ProcurementWebApplicationFactory factory, decimal allocation = 1000m)
    {
        var scenario = new ProcurementScenario(
            await ApiUser.CreateAsync(factory, SeededRoles.Administrator),
            await ApiUser.CreateAsync(factory, SeededRoles.BudgetController),
            await ApiUser.CreateAsync(factory, SeededRoles.Requester));
        var (department, _) = await scenario.Administrator.CreateAsync(MasterDataRoutes.Departments, new { code = MasterDataRoutes.NewCode("D"), name = "Operations" });
        scenario.DepartmentId = department.GetProperty("id").GetGuid();
        var (costCenter, costCenterETag) = await scenario.Administrator.CreateAsync(MasterDataRoutes.CostCenters,
            new { code = MasterDataRoutes.NewCode("C"), name = "Support", departmentId = scenario.DepartmentId });
        scenario.CostCenterId = costCenter.GetProperty("id").GetGuid();
        scenario.CostCenterETag = costCenterETag;
        var (category, _) = await scenario.Administrator.CreateAsync(MasterDataRoutes.Categories, new { code = MasterDataRoutes.NewCode("G"), name = "IT supplies" });
        scenario.CategoryId = category.GetProperty("id").GetGuid();
        var (unit, _) = await scenario.Administrator.CreateAsync(MasterDataRoutes.Units, new { code = MasterDataRoutes.NewCode("U"), name = "Unit", allowedDecimals = 0 });
        scenario.UnitId = unit.GetProperty("id").GetGuid();
        scenario.PeriodId = await factory.OpenPeriodAsync(scenario.Budget);
        var (created, _) = await scenario.Budget.CreateAsync(ProcurementRoutes.Allocations, new
        {
            fiscalPeriodId = scenario.PeriodId,
            costCenterId = scenario.CostCenterId,
            currency = "BRL",
            amount = allocation.ToString(CultureInfo.InvariantCulture)
        });
        scenario.AllocationId = created.GetProperty("id").GetGuid();
        return scenario;
    }

    public static async Task<Guid> UserIdAsync(ApiUser user)
    {
        using var response = await user.GetAsync("/api/v2/account/me");
        return (await ApiUser.ReadAsync(response)).GetProperty("userId").GetGuid();
    }

    /// <summary>Makes <paramref name="managerId"/> the cost-center manager (REQ-011 team scope).</summary>
    public async Task AssignManagerAsync(Guid managerId)
    {
        using var response = await Administrator.PutAsync($"{MasterDataRoutes.CostCenters}/{CostCenterId}", new { name = "Support", managerUserId = managerId }, CostCenterETag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        CostCenterETag = response.Headers.ETag!.Tag;
    }

    public Task<(JsonElement Body, string ETag)> DraftAsync(ApiUser? user = null, string? purpose = Purpose) =>
        (user ?? Requester).CreateAsync(ProcurementRoutes.Requisitions, new
        {
            costCenterId = CostCenterId,
            currency = "BRL",
            purpose,
            neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        });

    public object Line(string quantity = "2", string price = "150.00") => new
    {
        description = "Laptop 14 inch",
        kind = "Good",
        categoryId = CategoryId,
        unitOfMeasureId = UnitId,
        quantity,
        estimatedUnitPrice = new { amount = price, currency = "BRL" }
    };

    public async Task<(JsonElement Body, string ETag)> AddLineAsync(Guid id, string etag, ApiUser? user = null, string quantity = "2", string price = "150.00")
    {
        using var response = await (user ?? Requester).PostAsync($"{ProcurementRoutes.Requisitions}/{id}/lines", Line(quantity, price), etag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ApiUser.ReadAsync(response), response.Headers.ETag!.Tag);
    }

    public async Task<(JsonElement Body, string ETag)> SubmittedAsync(string quantity = "2", string price = "150.00")
    {
        var (draft, etag) = await DraftAsync();
        var id = draft.GetProperty("id").GetGuid();
        (_, etag) = await AddLineAsync(id, etag, quantity: quantity, price: price);
        using var response = await Requester.PostAsync($"{ProcurementRoutes.Requisitions}/{id}/submit", null, etag);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ApiUser.ReadAsync(response), response.Headers.ETag!.Tag);
    }

    public async Task<JsonElement> AllocationAsync()
    {
        using var response = await Budget.GetAsync($"{ProcurementRoutes.Allocations}/{AllocationId}");
        return await ApiUser.ReadAsync(response);
    }
}
