using Shouldly;
using Template.Domain.Products.Entities;

namespace Template.IntegrationTests.Data;
public class EfRepositoryAdd : BaseEfRepoTestFixture
{
    [Fact]
    public async Task AddProduct_ShouldAddProductSuccessfully()
    {
        // Arrange
        var productName = "Test Product";
        var productPrice = 1000;
        var productBarCode = "123456789";
        var repository = GetRepository<ProductEntity>();
        var unitOfWork = GetUnitOfWork();
        var product = new ProductEntity(productName, productPrice, productBarCode);

        // Act
        await repository.AddAsync(product);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var newProduct = (await repository.GetAllAsync()).FirstOrDefault();

        newProduct.ShouldNotBeNull();
        newProduct.Name.ShouldBe(productName);
        newProduct.Price.ShouldBe(productPrice);
        newProduct.BarCode.ShouldBe(productBarCode);
    }

    [Fact]
    public async Task AddProductWithoutSaving_ShouldNotPersistProduct()
    {
        // Arrange
        var productName = "Test Product";
        var productPrice = 1000;
        var productBarCode = "123456789";
        var repository = GetRepository<ProductEntity>();
        var product = new ProductEntity(productName, productPrice, productBarCode);

        // Act
        await repository.AddAsync(product);

        // Assert
        var newProduct = (await repository.GetAllAsync()).FirstOrDefault();

        newProduct.ShouldBeNull();
    }
}
