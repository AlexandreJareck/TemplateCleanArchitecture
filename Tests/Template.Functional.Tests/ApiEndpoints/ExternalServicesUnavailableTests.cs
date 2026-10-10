using System.Net;
using Shouldly;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.ApiEndpoints;

public sealed class ExternalServicesUnavailableTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly CustomWebApplicationFactory<Program> factory;

    public ExternalServicesUnavailableTests(CustomWebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task TestHost_Starts_When_ExternalServices_Are_Unavailable()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
