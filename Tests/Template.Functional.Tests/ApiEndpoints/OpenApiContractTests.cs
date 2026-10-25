using System.Text.Json.Nodes;
using Shouldly;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.ApiEndpoints;

/// <summary>
/// Phase 33: the published OpenAPI documents equal the approved contract baseline, so a route, status code or schema
/// change fails the fast CI gate instead of only the Docker baseline. Line endings are normalized; a deliberate
/// contract change captures a new baseline (scripts/capture-api-baseline.ps1) and updates <see cref="BaselineDirectory"/>.
/// </summary>
public sealed class OpenApiContractTests(CustomWebApplicationFactory<Program> factory) : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private const string BaselineDirectory = "docs/refactoring/baseline-phase-32";

    private readonly HttpClient client = factory.CreateClient();

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PublishedDocument_EqualsTheApprovedBaseline(string version)
    {
        var published = await client.GetStringAsync($"/swagger/{version}/swagger.json");
        var baseline = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), BaselineDirectory, $"openapi-{version}.json"));

        JsonNode.DeepEquals(JsonNode.Parse(published), JsonNode.Parse(baseline))
            .ShouldBeTrue($"OpenAPI {version} differs from {BaselineDirectory}; capture a new baseline only for an approved contract change.");
        Normalize(published).ShouldBe(Normalize(baseline));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TemplateCleanArchitecture.App.slnf")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The repository root (TemplateCleanArchitecture.App.slnf) was not found.");
    }
}
