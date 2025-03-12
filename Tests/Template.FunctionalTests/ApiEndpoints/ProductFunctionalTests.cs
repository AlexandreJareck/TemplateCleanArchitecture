using Microsoft.VisualStudio.TestPlatform.TestHost;
using Shouldly;
using Template.Application.Features.Products.Commands;
using Template.Application.Wrappers;
using Template.Domain.Products.DTOs;
using Template.FunctionalTests.Common;

namespace Template.FunctionalTests.ApiEndpoints;

[Collection("ProductFunctionalTests")]
public class ProductFunctionalTests(CustomWebApplicationFactory<Program> factory) : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task GetPagedListProduct_ShouldReturnPagedResponse()
    {
        // Arrange
        var url = ApiRoutes.V1.Product.GetPagedListProduct;

        // Act
        var result = await client.GetAndDeserializeAsync<PagedResponse<ProductDto>>(url);

        // Assert
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetProductById_ShouldReturnProduct()
    {
        // Arrange
        var url = ApiRoutes.V1.Product.GetProductById.AddQueryString("id", "c9770569-499d-4b25-59af-08dd5e76cb62");

        // Act
        var result = await client.GetAndDeserializeAsync<BaseResult<ProductDto>>(url);

        // Assert
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.Id.ShouldBe(Guid.Parse("c9770569-499d-4b25-59af-08dd5e76cb62"));
    }

    [Fact]
    public async Task CreateProduct_ShouldReturnProductId()
    {
        // Arrange
        var url = ApiRoutes.V1.Product.CreateProduct;
        var command = new CreateProductCommand
        {
            Name = RandomDataExtensionMethods.RandomString(10),
            Price = RandomDataExtensionMethods.RandomNumber(100000000),
            BarCode = RandomDataExtensionMethods.RandomString(11)
        };
        var ghostAccount = await client.GetGhostAccount();

        // Act
        var result = await client.PostAndDeserializeAsync<BaseResult<ProductDto>>(url, command, ghostAccount.JwToken);

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateProduct_ShouldSucceed()
    {
        // Arrange
        var url = ApiRoutes.V1.Product.UpdateProduct;
        var command = new UpdateProductCommand
        {
            Id = Guid.Parse("c9770569-499d-4b25-59af-08dd5e76cb62"),
            Name = RandomDataExtensionMethods.RandomString(10),
            Price = RandomDataExtensionMethods.RandomNumber(100000000),
            BarCode = RandomDataExtensionMethods.RandomString(11)
        };
        var ghostAccount = await client.GetGhostAccount();

        // Act
        var result = await client.PutAndDeserializeAsync<BaseResult>(url, command, ghostAccount.JwToken);

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteProduct_ShouldSucceed()
    {
        // Arrange
        var url = ApiRoutes.V1.Product.DeleteProduct.AddQueryString("id", "c9770569-499d-4b25-59af-08dd5e76cb62");
        var ghostAccount = await client.GetGhostAccount();

        // Act
        var result = await client.DeleteAndDeserializeAsync<BaseResult>(url, ghostAccount.JwToken);

        // Assert
        result.Success.ShouldBeTrue();
    }

}
