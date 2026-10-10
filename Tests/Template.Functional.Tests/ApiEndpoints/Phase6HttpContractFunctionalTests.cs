using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Template.Application.DTOs.Products;
using Template.Application.Features.Products.Commands;
using Template.Application.Wrappers;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.ApiEndpoints;

[Collection("ProductFunctionalTests")]
public sealed class Phase6HttpContractFunctionalTests(
    CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task V1_NotFound_PreservesLegacy200Wrapper()
    {
        var url = ApiRoutes.V1.Product.GetProductById.AddQueryString("id", Guid.NewGuid().ToString());

        using var response = await client.GetAsync(url);
        var result = await response.Content.ReadFromJsonAsync<BaseResult<ProductDto>>(JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        result!.Success.ShouldBeFalse();
        result.Errors.Single().ErrorCode.ShouldBe(ErrorCode.NotFound);
    }

    [Fact]
    public async Task V2_NotFound_ReturnsProblemDetails()
    {
        using var response = await client.GetAsync(ApiRoutes.V2.Product.GetProductById(Guid.NewGuid()));

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task V2_ProtectedEndpoint_WithoutToken_Returns401ProblemDetails()
    {
        using var response = await client.PostAsJsonAsync(
            ApiRoutes.V2.Product.CreateProduct,
            new { name = "Book", price = 10m, barCode = "AUTH-401" });

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(challenge => challenge.Scheme == "Bearer");
    }

    [Fact]
    public async Task AuthenticatedUser_WithoutRole_Returns403ProblemDetails()
    {
        var account = await client.GetGhostAccount();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_tests/forbidden");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.JwToken);

        using var response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task V2_CreateUpdateDelete_UseRestStatusCodesAndLocation()
    {
        var account = await client.GetGhostAccount();
        var command = new CreateProductCommand
        {
            Name = "Phase 6 product",
            Price = 19.99m,
            BarCode = $"P6-{Guid.NewGuid():N}"[..20]
        };

        using var createRequest = Authorized(HttpMethod.Post, ApiRoutes.V2.Product.CreateProduct, account.JwToken, command);
        using var createResponse = await client.SendAsync(createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<BaseResult<ProductDto>>(JsonOptions);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        created!.Success.ShouldBeTrue();
        createResponse.Headers.Location!.ToString().ShouldBe(ApiRoutes.V2.Product.GetProductById(created.Data.Id));

        var update = new UpdateProductCommand
        {
            Name = "Phase 6 updated",
            Price = 29.95m,
            BarCode = command.BarCode
        };
        using var updateRequest = Authorized(HttpMethod.Put, ApiRoutes.V2.Product.UpdateProduct(created.Data.Id), account.JwToken, update);
        using var updateResponse = await client.SendAsync(updateRequest);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var deleteRequest = Authorized(HttpMethod.Delete, ApiRoutes.V2.Product.DeleteProduct(created.Data.Id), account.JwToken);
        using var deleteResponse = await client.SendAsync(deleteRequest);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await deleteResponse.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task WrongMethod_Returns405_AndUnsupportedUrlVersionReturns404()
    {
        using var wrongMethod = await client.PatchAsJsonAsync(ApiRoutes.V2.Product.CreateProduct, new { });
        wrongMethod.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);

        using var unsupportedVersion = await client.GetAsync($"/api/v99/product/{Guid.NewGuid()}");
        unsupportedVersion.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnexpectedException_Returns500WithoutInternalDetails()
    {
        using var response = await client.GetAsync("/_tests/exception");
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        text.ShouldNotContain("internal-sensitive-marker");
    }

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string url,
        string token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.RootElement.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        body.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
