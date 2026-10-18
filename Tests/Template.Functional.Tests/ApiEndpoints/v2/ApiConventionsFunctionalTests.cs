using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Operations;
using Template.Functional.Tests.Common;
using Template.Functional.Tests.Security;
using Template.WebApi.Infrastructure.Http;
using Template.WebApi.Infrastructure.Security;

namespace Template.Functional.Tests.ApiEndpoints.v2;

/// <summary>
/// Reusable HTTP conventions of docs/product/api-guidelines.md §4, §6 and §8 exercised through a test-only controller
/// (registered only by this fixture, never by the application): Idempotency-Key, If-Match/ETag (SP-1 412/428) and
/// 202 Accepted. The first production endpoints adopt them in Phase 25.
/// </summary>
public sealed class ApiConventionsFunctionalTests(ConventionsWebApplicationFactory factory) : IClassFixture<ConventionsWebApplicationFactory>
{
    [Fact]
    public async Task IdempotentPost_ReplaysTheStoredResponse_WithoutRunningTwice()
    {
        var (client, token) = await AuthenticatedAsync();
        var key = Guid.NewGuid().ToString();
        var before = ConventionsFixtureController.Executions;

        using var first = await PostAsync(client, token, key, new { name = "first" });
        using var replay = await PostAsync(client, token, key, new { name = "first" });

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        replay.Headers.Location.ShouldBe(first.Headers.Location);
        replay.Headers.GetValues(RequireIdempotencyKeyAttribute.ReplayedHeaderName).ShouldBe(["true"]);
        first.Headers.Contains(RequireIdempotencyKeyAttribute.ReplayedHeaderName).ShouldBeFalse();
        (ConventionsFixtureController.Executions - before).ShouldBe(1);
    }

    [Fact]
    public async Task IdempotentPost_SameKeyDifferentBody_Is422()
    {
        var (client, token) = await AuthenticatedAsync();
        var key = Guid.NewGuid().ToString();
        (await PostAsync(client, token, key, new { name = "first" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        using var reused = await PostAsync(client, token, key, new { name = "second" });

        reused.StatusCode.ShouldBe((HttpStatusCode)422);
        (await SessionClient.ReadJsonAsync(reused)).GetProperty("code").GetString().ShouldBe("idempotency.key-reused");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")] // 65 characters
    public async Task IdempotentPost_MissingOrOversizedKey_Is400(string? key)
    {
        var (client, token) = await AuthenticatedAsync();

        using var response = await PostAsync(client, token, key, new { name = "x" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await SessionClient.ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("idempotency.key-required");
        problem.GetProperty("errors").TryGetProperty("Idempotency-Key", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task IdempotentPost_KeysAreScopedPerUser()
    {
        var (firstClient, firstToken) = await AuthenticatedAsync();
        var (secondClient, secondToken) = await AuthenticatedAsync();
        var key = Guid.NewGuid().ToString();

        (await PostAsync(firstClient, firstToken, key, new { name = "a" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        using var other = await PostAsync(secondClient, secondToken, key, new { name = "b" });

        other.StatusCode.ShouldBe(HttpStatusCode.Created);
        other.Headers.Contains(RequireIdempotencyKeyAttribute.ReplayedHeaderName).ShouldBeFalse();
    }

    [Fact]
    public async Task VersionedResource_ExposesETag_AndEnforcesIfMatch()
    {
        var client = factory.CreateClient();
        var id = Guid.NewGuid();
        ConventionsFixtureController.Versions[id] = 3;

        using var get = await client.GetAsync($"/_tests/conventions/resources/{id}");
        get.Headers.ETag!.Tag.ShouldBe("\"3\"");
        get.Headers.ETag.IsWeak.ShouldBeFalse();
        (await SessionClient.ReadJsonAsync(get)).GetProperty("version").GetString().ShouldBe("3");

        using var missing = await PutAsync(client, id, null);
        missing.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await SessionClient.ReadJsonAsync(missing)).GetProperty("code").GetString().ShouldBe("concurrency.precondition-required");

        using var stale = await PutAsync(client, id, "\"2\"");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var problem = await SessionClient.ReadJsonAsync(stale);
        problem.GetProperty("code").GetString().ShouldBe("concurrency.version-mismatch");
        problem.GetProperty("version").GetString().ShouldBe("3");
        ConventionsFixtureController.Versions[id].ShouldBe(3); // nothing applied

        using var weak = await PutAsync(client, id, "W/\"3\"");
        weak.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        using var ok = await PutAsync(client, id, "\"3\"");
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ok.Headers.ETag!.Tag.ShouldBe("\"4\"");
    }

    [Fact]
    public async Task AsyncOperation_Returns202_WithLocationRetryAfterAndJob()
    {
        using var response = await factory.CreateClient().PostAsync("/_tests/conventions/jobs", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location!.OriginalString.ShouldStartWith("/_tests/conventions/jobs/");
        response.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(2));
        var job = await SessionClient.ReadJsonAsync(response);
        job.GetProperty("status").GetString().ShouldBe("Queued");
        job.TryGetProperty("contentUrl", out var contentUrl).ShouldBeTrue();
        contentUrl.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    private async Task<(HttpClient Client, string Token)> AuthenticatedAsync()
    {
        var session = SessionClient.Create(factory);
        await session.LoginOkAsync(await SessionUsers.CreateAsync(factory.Services));
        return (session.Client, session.AccessToken!);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string? key, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/_tests/conventions/items") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (key is not null)
            request.Headers.TryAddWithoutValidation(RequireIdempotencyKeyAttribute.HeaderName, key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, string? ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/_tests/conventions/resources/{id}") { Content = JsonContent.Create(new { }) };
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(request);
    }
}

public sealed class ConventionsWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddControllers().AddApplicationPart(typeof(ConventionsFixtureController).Assembly);
}

/// <summary>Test-only endpoints; this assembly is added as an application part only by <see cref="ConventionsWebApplicationFactory"/>.</summary>
[ApiController]
[ApiVersionNeutral]
[Route("_tests/conventions")]
[StableProblemCodes]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class ConventionsFixtureController(ApiResultMapper resultMapper) : ControllerBase
{
    private static int executions;
    public static int Executions => Volatile.Read(ref executions);
    public static ConcurrentDictionary<Guid, long> Versions { get; } = new();

    public sealed record ItemRequest(string Name);

    [HttpPost("items")]
    [Authorize]
    [RequireIdempotencyKey]
    public IActionResult CreateItem([FromBody] ItemRequest request)
    {
        Interlocked.Increment(ref executions);
        var id = Guid.NewGuid();
        return Created($"/_tests/conventions/items/{id}", new { id, request.Name });
    }

    [HttpGet("resources/{id:guid}")]
    [AllowAnonymous]
    public IActionResult GetResource(Guid id)
    {
        var version = Versions.GetOrAdd(id, 1);
        VersionHeaders.SetETag(Response, version);
        return Ok(new { id, version = VersionPrecondition.Format(version) });
    }

    [HttpPut("resources/{id:guid}")]
    [AllowAnonymous]
    public IActionResult UpdateResource(Guid id)
    {
        var current = Versions.GetOrAdd(id, 1);
        if (VersionPrecondition.Check(VersionHeaders.ReadIfMatch(Request), current) is { } error)
            return resultMapper.ToProblemResult(error, HttpContext);

        Versions[id] = current + 1;
        VersionHeaders.SetETag(Response, current + 1);
        return Ok(new { id, version = VersionPrecondition.Format(current + 1) });
    }

    [HttpPost("jobs")]
    [AllowAnonymous]
    public IActionResult StartJob()
    {
        var job = new AcceptedOperation(Guid.NewGuid(), OperationStatus.Queued, DateTime.UtcNow);
        return AcceptedOperations.Create(Response, $"/_tests/conventions/jobs/{job.Id}", job);
    }
}
