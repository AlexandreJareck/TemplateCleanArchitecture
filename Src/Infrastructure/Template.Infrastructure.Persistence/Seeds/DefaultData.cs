using Microsoft.EntityFrameworkCore;
using Template.Domain.Products.Entities;
using Template.Infrastructure.Persistence.Contexts;

namespace Template.Infrastructure.Persistence.Seeds;

public static class DefaultData
{
    public static async Task SeedAsync(ApplicationDbContext applicationDbContext)
    {
        if (!await applicationDbContext.Products.AnyAsync())
        {
            List<ProductEntity> defaultProducts = [
                new ProductEntity(Guid.Parse("c9770569-499d-4b25-59af-08dd5e76cb62"), "Product 1", 100000, "111111111111"),
                new ProductEntity(Guid.Parse("80f099ae-95a2-4fdd-59b0-08dd5e76cb62"), "Product 2", 150000, "222222222222"),
                new ProductEntity(Guid.Parse("e9e9743f-44fb-4d0b-59b1-08dd5e76cb62"), "Product 3", 200000, "333333333333"),
                new ProductEntity(Guid.Parse("b3c87ba8-5c9e-49f2-59b2-08dd5e76cb62"), "Product 4", 105000, "444444444444"),
                new ProductEntity(Guid.Parse("818d3815-269a-4ed8-59b3-08dd5e76cb62"), "Product 5", 200000, "555555555555")
            ];

            await applicationDbContext.Products.AddRangeAsync(defaultProducts);
            await applicationDbContext.SaveChangesAsync();
        }
    }
}

