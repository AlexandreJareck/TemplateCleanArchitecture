using Microsoft.EntityFrameworkCore;
using Shouldly;
using Template.Domain.Products.Entities;

namespace Template.Integration.Tests.Data;

public class ProductPersistencePrecisionTests : BaseEfRepoTestFixture
{
    [Fact]
    public async Task ProductPrice_ShouldRoundTripExactlyAsDecimal()
    {
        const decimal expectedPrice = 1234567890123456.78m;
        var repository = GetRepository<ProductEntity>();
        var unitOfWork = GetUnitOfWork();
        var product = new ProductEntity("Precision product", expectedPrice, "PRECISION-001");

        await repository.AddAsync(product);
        await unitOfWork.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var persistedProduct = await repository.GetByIdAsync(product.Id);

        persistedProduct.ShouldNotBeNull();
        persistedProduct.Price.ShouldBe(expectedPrice);
    }

    [Fact]
    public void ProductPrice_ShouldUseExpectedDatabasePrecisionAndScale()
    {
        var property = dbContext.Model
            .FindEntityType(typeof(ProductEntity))!
            .FindProperty(nameof(ProductEntity.Price))!;

        property.GetPrecision().ShouldBe(18);
        property.GetScale().ShouldBe(2);
    }
}
