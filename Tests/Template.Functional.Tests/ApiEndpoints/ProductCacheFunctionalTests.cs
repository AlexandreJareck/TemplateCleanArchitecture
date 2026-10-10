using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.DTOs.Products;
using Template.Application.Features.Products.Commands;
using Template.Application.Interfaces.Caching;
using Template.Application.Wrappers;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Caching.Products;

namespace Template.Functional.Tests.ApiEndpoints;

public sealed class ProductCacheFunctionalTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public void TestHost_UsesDistributedProductCache_WhenEnabled()
    {
        factory.Services.GetRequiredService<IProductCache>().ShouldBeOfType<DistributedProductCache>();
    }

    [Fact]
    public async Task UpdateProduct_InvalidatesCachedProduct()
    {
        var token = (await client.GetGhostAccount()).JwToken;
        var product = await ProductCacheScenario.CreateProductAsync(client, token);
        (await ProductCacheScenario.GetCachedAsync(client, product.Id)).Name.ShouldBe(product.Name);

        var newName = RandomDataExtensionMethods.RandomString(10);
        var update = await client.PutAndDeserializeAsync<BaseResult>(
            ApiRoutes.V1.Product.UpdateProduct,
            new UpdateProductCommand { Id = product.Id, Name = newName, Price = product.Price, BarCode = product.BarCode },
            token);

        update.Success.ShouldBeTrue();
        (await ProductCacheScenario.GetCachedAsync(client, product.Id)).Name.ShouldBe(newName);
    }

    [Fact]
    public async Task DeleteProduct_InvalidatesCachedProduct()
    {
        var token = (await client.GetGhostAccount()).JwToken;
        var product = await ProductCacheScenario.CreateProductAsync(client, token);
        await ProductCacheScenario.GetCachedAsync(client, product.Id);

        var delete = await client.DeleteAndDeserializeAsync<BaseResult>(
            ApiRoutes.V1.Product.DeleteProduct.AddQueryString("id", product.Id.ToString()),
            token);

        delete.Success.ShouldBeTrue();
        (await client.GetAsync(ApiRoutes.V2.Product.GetProductWithCache(product.Id))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

public sealed class ProductCacheDisabledFunctionalTests(ProductCacheDisabledFunctionalTests.CacheDisabledFactory factory)
    : IClassFixture<ProductCacheDisabledFunctionalTests.CacheDisabledFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task CacheDisabled_ServesProductsFromDatabase()
    {
        factory.Services.GetRequiredService<IProductCache>().ShouldBeOfType<NoOpProductCache>();
        var token = (await client.GetGhostAccount()).JwToken;
        var product = await ProductCacheScenario.CreateProductAsync(client, token);
        (await ProductCacheScenario.GetCachedAsync(client, product.Id)).Name.ShouldBe(product.Name);

        var newName = RandomDataExtensionMethods.RandomString(10);
        await client.PutAndDeserializeAsync<BaseResult>(
            ApiRoutes.V1.Product.UpdateProduct,
            new UpdateProductCommand { Id = product.Id, Name = newName, Price = product.Price, BarCode = product.BarCode },
            token);

        (await ProductCacheScenario.GetCachedAsync(client, product.Id)).Name.ShouldBe(newName);
    }

    public sealed class CacheDisabledFactory : CustomWebApplicationFactory<Program>
    {
        protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } =
            new Dictionary<string, string?> { ["CacheSettings:Enabled"] = "false" };
    }
}

internal static class ProductCacheScenario
{
    public static async Task<ProductDto> CreateProductAsync(HttpClient client, string token)
    {
        var created = await client.PostAndDeserializeAsync<BaseResult<ProductDto>>(
            ApiRoutes.V1.Product.CreateProduct,
            new CreateProductCommand
            {
                Name = RandomDataExtensionMethods.RandomString(10),
                Price = 123.45m,
                BarCode = RandomDataExtensionMethods.RandomString(11)
            },
            token);

        created.Success.ShouldBeTrue();
        return created.Data;
    }

    public static async Task<ProductDto> GetCachedAsync(HttpClient client, Guid id)
    {
        var result = await client.GetAndDeserializeAsync<BaseResult<ProductDto>>(ApiRoutes.V1.Product.GetProductWithCache(id));

        result.Success.ShouldBeTrue();
        return result.Data;
    }
}
