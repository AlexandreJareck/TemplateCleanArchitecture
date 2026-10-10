using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.ApiEndpoints;

/// <summary>
/// Characterizes the HTTP validation response after the Phase 5 MediatR validation pipeline
/// was added: invalid product payloads still return 400 with an <c>errors</c> collection.
/// </summary>
[Collection("ProductFunctionalTests")]
public class ProductValidationFunctionalTests(CustomWebApplicationFactory<Program> factory) : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Theory]
    [InlineData(ApiRoutes.V1.Product.CreateProduct)]
    [InlineData(ApiRoutes.V2.Product.CreateProduct)]
    public async Task CreateProduct_WithInvalidPayload_ShouldReturnBadRequestWithErrors(string url)
    {
        var ghostAccount = await client.GetGhostAccount();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { name = "", price = 10m, barCode = "" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ghostAccount.JwToken);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
        body.RootElement.TryGetProperty("errors", out _).ShouldBeTrue();
    }
}
