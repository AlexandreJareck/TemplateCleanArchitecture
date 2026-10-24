using Microsoft.EntityFrameworkCore;
using Template.Application.Demo;
using Template.Domain.Budgeting;
using Template.Domain.Catalog;
using Template.Domain.Common;
using Template.Domain.Organization;
using Template.Domain.Requisitions;
using Template.Domain.Suppliers;
using Template.Infrastructure.Persistence.Contexts;
using Template.Infrastructure.Persistence.Contexts.Configurations;

namespace Template.Infrastructure.Persistence.Seeds;

/// <summary>
/// Realistic but entirely fictional academic data. Stable IDs make the seed repeatable. Transactional lifecycle
/// states are intentionally left for the guided API journey so business rules, audit and events are exercised.
/// </summary>
public static class DemoApplicationData
{
    public static async Task SeedAsync(ApplicationDbContext context)
    {
        var sentinelId = Id("10000000-0000-0000-0000-000000000001");
        if (await context.Set<Department>().AnyAsync(entity => entity.Id == sentinelId))
            return;

        var now = DateTime.UtcNow;
        var year = now.Year;
        var technology = Require(Department.Create(sentinelId, "TEC", "Tecnologia e Dados", DemoDataIds.DepartmentHead, DemoDataIds.Administrator, now));
        var operations = Require(Department.Create(Id("10000000-0000-0000-0000-000000000002"), "OPE", "Operações", null, DemoDataIds.Administrator, now));
        var finance = Require(Department.Create(Id("10000000-0000-0000-0000-000000000003"), "FIN", "Finanças", DemoDataIds.FinanceApprover, DemoDataIds.Administrator, now));

        var infrastructure = Require(CostCenter.Create(Id("11000000-0000-0000-0000-000000000001"), "TEC-INFRA", "Infraestrutura e Cloud", technology, DemoDataIds.CostCenterManager, DemoDataIds.Administrator, now));
        var product = Require(CostCenter.Create(Id("11000000-0000-0000-0000-000000000002"), "TEC-PROD", "Produtos Digitais", technology, null, DemoDataIds.Administrator, now));
        var facilities = Require(CostCenter.Create(Id("11000000-0000-0000-0000-000000000003"), "OPE-FAC", "Facilities", operations, null, DemoDataIds.Administrator, now));
        var controllership = Require(CostCenter.Create(Id("11000000-0000-0000-0000-000000000004"), "FIN-CTRL", "Controladoria", finance, DemoDataIds.BudgetController, DemoDataIds.Administrator, now));

        var it = Require(Category.Create(Id("20000000-0000-0000-0000-000000000001"), "TI", "Tecnologia da Informação", DemoDataIds.Administrator, now));
        var software = Require(Category.Create(Id("20000000-0000-0000-0000-000000000002"), "SOFTWARE", "Software e Licenças", DemoDataIds.Administrator, now));
        var office = Require(Category.Create(Id("20000000-0000-0000-0000-000000000003"), "ESCRITORIO", "Mobiliário e Escritório", DemoDataIds.Administrator, now));
        var services = Require(Category.Create(Id("20000000-0000-0000-0000-000000000004"), "SERVICOS", "Serviços Profissionais", DemoDataIds.Administrator, now));
        var unit = Require(UnitOfMeasure.Create(Id("21000000-0000-0000-0000-000000000001"), "UN", "Unidade", 0, DemoDataIds.Administrator, now));
        var hour = Require(UnitOfMeasure.Create(Id("21000000-0000-0000-0000-000000000002"), "H", "Hora", 2, DemoDataIds.Administrator, now));
        var month = Require(UnitOfMeasure.Create(Id("21000000-0000-0000-0000-000000000003"), "MES", "Mês", 0, DemoDataIds.Administrator, now));

        var notebook = Item("22000000-0000-0000-0000-000000000001", "HW-NOTEBOOK-14", "Notebook corporativo, 16 GB RAM, SSD 512 GB e garantia de 36 meses", CatalogItemKind.Good, it, unit, 6890m, now);
        var monitor = Item("22000000-0000-0000-0000-000000000002", "HW-MONITOR-27", "Monitor IPS 27 polegadas QHD com ajuste de altura", CatalogItemKind.Good, it, unit, 1840m, now);
        var chair = Item("22000000-0000-0000-0000-000000000003", "MOV-CADEIRA-ERG", "Cadeira ergonômica com apoio lombar e braços reguláveis", CatalogItemKind.Good, office, unit, 2450m, now);
        var collaboration = Item("22000000-0000-0000-0000-000000000004", "SW-COLLAB-ANUAL", "Licença anual de colaboração corporativa", CatalogItemKind.Service, software, unit, 960m, now);
        var cloud = Item("22000000-0000-0000-0000-000000000005", "SRV-CLOUD-MANAGED", "Operação mensal de ambiente cloud gerenciado", CatalogItemKind.Service, services, month, 18500m, now);
        var consulting = Item("22000000-0000-0000-0000-000000000006", "SRV-CONSULTORIA-H", "Consultoria especializada em arquitetura de software", CatalogItemKind.Service, services, hour, 420m, now);

        var atlas = Supplier("30000000-0000-0000-0000-000000000001", "Atlas Tecnologia Corporativa Ltda.", "Atlas Tech", "BR", "11.222.333/0001-81", "Juliana Ferreira", "juliana.ferreira@fornecedor.demo", "+55 11 4000-1001", "Executiva de contas", new(SupplierAddressType.Headquarters, "01310100", "Avenida Paulista", "1000", "10º andar", "Bela Vista", "São Paulo", "SP", "BR"), [it, software], now);
        var northwind = Supplier("30000000-0000-0000-0000-000000000002", "Northwind Cloud Services LLC", "Northwind Cloud", "US", "US-94-0000001", "Ethan Walker", "ethan.walker@vendor.demo", "+1 206 555 0110", "Account manager", new(SupplierAddressType.Headquarters, "98101", "Pine Street", "1201", null, null, "Seattle", "WA", "US"), [software, services], now);
        var mobili = Supplier("30000000-0000-0000-0000-000000000003", "Mobili Espaços Corporativos GmbH", "Mobili", "DE", "DE-123456789", "Sofia Becker", "sofia.becker@lieferant.demo", "+49 30 5550 1200", "Commercial contact", new(SupplierAddressType.Headquarters, "10115", "Invalidenstrasse", "44", null, null, "Berlin", "Berlin", "DE"), [office], now);

        var period = Require(FiscalPeriod.Create(Id("40000000-0000-0000-0000-000000000001"), $"FY{year}", $"Exercício fiscal {year}", new(year, 1, 1), new(year, 12, 31), DemoDataIds.BudgetController, now));
        Ensure(period.Open(now), "open the demo fiscal period");
        var allocations = new[]
        {
            Allocation("41000000-0000-0000-0000-000000000001", period, infrastructure, 1_800_000m, now),
            Allocation("41000000-0000-0000-0000-000000000002", period, product, 950_000m, now),
            Allocation("41000000-0000-0000-0000-000000000003", period, facilities, 620_000m, now),
            Allocation("41000000-0000-0000-0000-000000000004", period, controllership, 300_000m, now)
        };
        var requisitions = new[]
        {
            Requisition("50000000-0000-0000-0000-000000000001", year, 1, DemoDataIds.Requester, infrastructure, "Renovação do parque de trabalho da equipe de engenharia", 45, now, [Line(notebook, it, unit, 8m, 6650m), Line(monitor, it, unit, 8m, 1760m)]),
            Requisition("50000000-0000-0000-0000-000000000002", year, 2, DemoDataIds.Requester, product, "Contratação anual de operação assistida para a plataforma digital", 60, now, [Line(cloud, services, month, 12m, 18_200m), Line(consulting, services, hour, 80m, 410m)]),
            Requisition("50000000-0000-0000-0000-000000000003", year, 3, DemoDataIds.CostCenterManager, facilities, "Adequação ergonômica das estações do novo escritório", 35, now, [Line(chair, office, unit, 24m, 2_380m), Line(collaboration, software, unit, 10m, 930m)])
        };

        context.AddRange(technology, operations, finance, infrastructure, product, facilities, controllership);
        context.AddRange(it, software, office, services, unit, hour, month);
        context.AddRange(notebook, monitor, chair, collaboration, cloud, consulting);
        context.AddRange(atlas, northwind, mobili, period);
        context.AddRange(allocations);
        context.AddRange(requisitions);
        context.Add(new RequisitionNumberCounter { Year = year, LastValue = 3 });
        await context.SaveChangesAsync();
    }

    private static CatalogItem Item(string id, string code, string description, CatalogItemKind kind, Category category, UnitOfMeasure unit, decimal price, DateTime now) => Require(CatalogItem.Create(Id(id), code, description, kind, category, unit, price, "BRL", DemoDataIds.Administrator, now));
    private static BudgetAllocation Allocation(string id, FiscalPeriod period, CostCenter costCenter, decimal amount, DateTime now) => Require(BudgetAllocation.Create(Id(id), period, new(costCenter.Id, costCenter.IsActive), "BRL", true, amount, DemoDataIds.BudgetController, now));

    private static Supplier Supplier(string id, string legalName, string tradeName, string country, string taxId, string contactName, string email, string phone, string role, AddressValues address, IReadOnlyCollection<Category> categories, DateTime now)
    {
        var supplier = Require(Template.Domain.Suppliers.Supplier.CreateDraft(Id(id), legalName, tradeName, country, taxId, DemoDataIds.Buyer, now));
        Ensure(supplier.ReplaceContacts([new(null, contactName, email, phone, role)], now), $"add contacts to supplier {tradeName}");
        Ensure(supplier.ReplaceAddresses([new(null, address)], now), $"add addresses to supplier {tradeName}");
        Ensure(supplier.SetCategories(categories.Select(category => new MasterDataReference(category.Id, category.IsActive)).ToArray(), now), $"assign categories to supplier {tradeName}");
        Ensure(supplier.Activate(DemoDataIds.ProcurementManager, now), $"activate supplier {tradeName}");
        return supplier;
    }

    private static PurchaseRequisition Requisition(string id, int year, long sequence, Guid requesterId, CostCenter costCenter, string purpose, int neededInDays, DateTime now, IReadOnlyCollection<RequisitionLineInput> lines)
    {
        var requisition = Require(PurchaseRequisition.CreateDraft(Id(id), PurchaseRequisition.FormatNumber(year, sequence), requesterId, new(costCenter.Id, costCenter.Code, costCenter.Name, costCenter.DepartmentId, costCenter.IsActive), "BRL", true, purpose, DateOnly.FromDateTime(now).AddDays(neededInDays), requesterId, now, $"demo-seed-{id}"));
        foreach (var line in lines)
            Require(requisition.AddLine(line, RequisitionPolicy.V1, now));
        return requisition;
    }

    private static RequisitionLineInput Line(CatalogItem item, Category category, UnitOfMeasure unit, decimal quantity, decimal unitPrice) => new(new(item.Id, item.IsActive), item.Description, item.Kind == CatalogItemKind.Good ? RequisitionLineKind.Good : RequisitionLineKind.Service, new(category.Id, category.Code, category.Name, category.IsActive), new(unit.Id, unit.Code, unit.Name, unit.AllowedDecimals, unit.IsActive), quantity, new(unitPrice, "BRL"));
    private static T Require<T>((T? Entity, DomainError? Error) result) where T : class => result.Entity ?? throw new InvalidOperationException($"Invalid academic demo seed: {result.Error?.Code ?? "unknown"}.");
    private static void Ensure(DomainError? error, string operation) { if (error is not null) throw new InvalidOperationException($"Could not {operation}: {error.Code}."); }
    private static Guid Id(string value) => Guid.Parse(value);
}
