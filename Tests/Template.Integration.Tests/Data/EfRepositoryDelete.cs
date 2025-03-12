using Shouldly;
using Template.Domain.Products.Entities;

namespace Template.Integration.Tests.Data;

public class EfRepositoryDelete : BaseEfRepoTestFixture
{
    [Fact]
    public async Task DeleteProduct_ShouldDeleteProductSuccessfully()
    {
        // Arrange
        var productName = "Test Product";
        var productPrice = 1000;
        var productBarCode = "123456789";
        var repository = GetRepository<ProductEntity>();
        var unitOfWork = GetUnitOfWork();
        var product = new ProductEntity(productName, productPrice, productBarCode);

        await repository.AddAsync(product);
        await unitOfWork.SaveChangesAsync();

        // Act
        var newProduct = await repository.GetByIdAsync(product.Id);
        newProduct.ShouldNotBeNull();

        repository.Delete(newProduct);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var deletedProduct = (await repository.GetAllAsync()).FirstOrDefault();

        deletedProduct.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteProductWithoutSaving_ShouldNotPersistDeletion()
    {
        // Arrange
        var productName = "Test Product";
        var productPrice = 1000;
        var productBarCode = "123456789";
        var repository = GetRepository<ProductEntity>();
        var unitOfWork = GetUnitOfWork();
        var product = new ProductEntity(productName, productPrice, productBarCode);

        await repository.AddAsync(product);
        await unitOfWork.SaveChangesAsync();

        // Act
        var newProduct = await repository.GetByIdAsync(product.Id);
        newProduct.ShouldNotBeNull();

        repository.Delete(newProduct);

        // Assert
        var deletedProduct = (await repository.GetAllAsync()).FirstOrDefault();

        deletedProduct.ShouldNotBeNull();
    }
}
