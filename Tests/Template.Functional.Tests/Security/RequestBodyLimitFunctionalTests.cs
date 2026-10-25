using System.Net;
using System.Text;
using System.Text.Json;
using Shouldly;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.Security;

/// <summary>NFR-SEC-4 (Phase 33): request bodies above 1 MB are refused before they are read.</summary>
public sealed class RequestBodyLimitFunctionalTests(CustomWebApplicationFactory<Program> factory) : IClassFixture<CustomWebApplicationFactory<Program>>
{
    [Fact]
    public async Task ABodyAboveOneMegabyte_Is413WithAStableCode()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(new string('x', 1024 * 1024 + 1), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v2/auth/sessions", content);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("request.too-large");
        problem.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ABodyWithinTheLimit_ReachesTheEndpoint()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"userName\":\"nobody\",\"password\":\"wrong\"}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v2/auth/sessions", content);

        response.StatusCode.ShouldNotBe(HttpStatusCode.RequestEntityTooLarge);
    }
}
