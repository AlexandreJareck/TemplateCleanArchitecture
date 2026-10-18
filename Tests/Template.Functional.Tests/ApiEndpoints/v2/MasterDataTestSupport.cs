using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Template.Application.Interfaces.Integrations;
using Template.Functional.Tests.Common;
using Template.Functional.Tests.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Test host for the Phase 25 endpoints. The enrichment ports are replaced by scripted fakes, so no provider is ever
/// contacted; by default they answer <c>Unavailable</c> exactly like the disabled adapter.
/// </summary>
public sealed class MasterDataWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    public ScriptedRegistry Registry { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ISupplierRegistryLookup>();
        services.AddSingleton<ISupplierRegistryLookup>(Registry);
    }

    public sealed class ScriptedRegistry : ISupplierRegistryLookup
    {
        public Func<string, EnrichmentResult<CompanyRegistrationSuggestion>> Answer { get; set; } =
            _ => EnrichmentResult<CompanyRegistrationSuggestion>.Failed(EnrichmentOutcome.Unavailable, "BrasilAPI", "disabled");

        public Task<EnrichmentResult<CompanyRegistrationSuggestion>> LookupAsync(string cnpj, CancellationToken cancellationToken) =>
            Task.FromResult(Answer(cnpj));
    }
}

/// <summary>Bearer-authenticated JSON client for one synthetic user with the given seeded roles.</summary>
internal sealed class ApiUser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SessionClient session;

    private ApiUser(SessionClient session)
    {
        this.session = session;
    }

    public static async Task<ApiUser> CreateAsync(MasterDataWebApplicationFactory factory, params string[] roles)
    {
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services, roles));
        return new ApiUser(session);
    }

    public async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, object? body = null, string? ifMatch = null, string? idempotencyKey = null, string? acceptLanguage = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (acceptLanguage is not null)
            request.Headers.AcceptLanguage.ParseAdd(acceptLanguage);
        return await session.Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);

    /// <summary>POST that creates or transitions: always sends a fresh <c>Idempotency-Key</c>.</summary>
    public Task<HttpResponseMessage> PostAsync(string url, object? body, string? ifMatch = null) =>
        SendAsync(HttpMethod.Post, url, body ?? new { }, ifMatch, Guid.NewGuid().ToString());

    public Task<HttpResponseMessage> PutAsync(string url, object body, string? ifMatch) => SendAsync(HttpMethod.Put, url, body, ifMatch);

    /// <summary>Creates a resource and returns its representation and strong ETag.</summary>
    public async Task<(JsonElement Body, string ETag)> CreateAsync(string url, object body)
    {
        using var response = await PostAsync(url, body);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadAsync(response), response.Headers.ETag!.Tag);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).Clone();

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;
}

internal static class MasterDataRoutes
{
    public const string Departments = "/api/v2/departments";
    public const string CostCenters = "/api/v2/cost-centers";
    public const string Settings = "/api/v2/organization-settings";
    public const string Suppliers = "/api/v2/suppliers";
    public const string Categories = "/api/v2/categories";
    public const string Units = "/api/v2/units-of-measure";
    public const string Items = "/api/v2/catalog-items";
    public const string Users = "/api/v2/users";
    public const string Enrichment = "/api/v2/enrichment";

    public static string NewCode(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(20, prefix.Length + 11)].ToUpperInvariant();

    /// <summary>Random structurally valid CNPJ (synthetic).</summary>
    public static string NewCnpj()
    {
        var digits = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture) + "0001";
        static int Digit(string value, int[] weights)
        {
            var remainder = value.Select((character, index) => (character - '0') * weights[index]).Sum() % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }

        var first = Digit(digits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{digits}{first}{Digit(digits + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }
}
