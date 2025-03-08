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
                new ProductEntity("Product 1",100000,"111111111111"),
                new ProductEntity("Product 2",150000,"222222222222"),
                new ProductEntity("Product 3",200000,"333333333333"),
                new ProductEntity("Product 4",105000,"444444444444"),
                new ProductEntity("Product 5",200000,"555555555555")
            ];

            await applicationDbContext.Products.AddRangeAsync(defaultProducts);
            await applicationDbContext.SaveChangesAsync();
        }
    }
}

