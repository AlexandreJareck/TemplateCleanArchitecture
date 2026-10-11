using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Shouldly;
using Template.Application.Logging;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.Observability;

/// <summary>
/// Phase 13 logging contract on a real Test host: stable event ids and property names, request correlation shared
/// with Problem Details, log-once for unexpected failures, cancellation, and redaction of representative secrets.
/// </summary>
public sealed class LoggingAndCorrelationTests(LogCapturingFactory factory) : IClassFixture<LogCapturingFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    private CapturingSink Sink => factory.Sink;

    [Fact]
    public async Task RequestCompleted_HasStableIdNameAndProperties_WithCorrelation()
    {
        var path = $"/api/v2/product/{Guid.NewGuid()}";
        using var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var traceId = await TraceIdAsync(response);

        var completed = await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
            .SingleOrDefault(logEvent => CapturingSink.Scalar(logEvent, "RequestPath") == path));

        CapturingSink.EventIdOf(completed).ShouldBe(1000);
        completed.Level.ShouldBe(LogEventLevel.Information);
        completed.MessageTemplate.Text.ShouldBe("HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMilliseconds} ms");
        CapturingSink.Scalar(completed, "RequestMethod").ShouldBe("GET");
        CapturingSink.Scalar(completed, "StatusCode").ShouldBe("404");
        completed.Properties.ShouldContainKey("ElapsedMilliseconds");
        CapturingSink.Scalar(completed, "CorrelationId").ShouldBe(traceId);
    }

    [Fact]
    public async Task UnexpectedException_IsLoggedOnce_WithTheTraceIdReturnedInProblemDetails()
    {
        using var response = await client.GetAsync("/_tests/exception");
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("internal-sensitive-marker");
        body.ShouldNotContain("InvalidOperationException");
        body.ShouldNotContain(" at ");
        var traceId = await TraceIdAsync(response);

        await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
            .SingleOrDefault(logEvent => CapturingSink.Scalar(logEvent, "CorrelationId") == traceId));
        var failures = Sink.Events
            .Where(logEvent => logEvent.Exception?.Message == "internal-sensitive-marker")
            .ToArray();

        var failure = failures.ShouldHaveSingleItem();
        CapturingSink.EventName(failure).ShouldBe(nameof(LogEvents.UnhandledException));
        CapturingSink.EventIdOf(failure).ShouldBe(1001);
        failure.Level.ShouldBe(LogEventLevel.Error);
        CapturingSink.Scalar(failure, "CorrelationId").ShouldBe(traceId);
        CapturingSink.Scalar(failure, "RequestMethod").ShouldBe("GET");
        CapturingSink.Scalar(failure, "RequestPath").ShouldBe("/_tests/exception");
    }

    [Fact]
    public async Task ExpectedFailures_AreNotLoggedAsUnexpectedExceptions()
    {
        var token = (await client.GetGhostAccount()).JwToken;
        using var invalid = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.V2.Product.CreateProduct)
        {
            Content = JsonContent.Create(new { name = "", price = 1m, barCode = "" })
        };
        invalid.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var invalidResponse = await client.SendAsync(invalid);
        using var notFound = await client.GetAsync(ApiRoutes.V2.Product.GetProductById(Guid.NewGuid()));

        invalidResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var traceIds = new[] { await TraceIdAsync(invalidResponse), await TraceIdAsync(notFound) };
        await WaitForAsync(() => traceIds.All(id => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
            .Any(logEvent => CapturingSink.Scalar(logEvent, "CorrelationId") == id)) ? true : (bool?)null);
        Sink.WithEventName(nameof(LogEvents.UnhandledException))
            .Where(logEvent => traceIds.Contains(CapturingSink.Scalar(logEvent, "CorrelationId")))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task AuthenticationAndAuthorizationFailures_AreLoggedWithoutTokens()
    {
        const string tokenMarker = "unit-test-bearer-marker";
        using var unauthenticated = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.V2.Product.CreateProduct)
        {
            Content = JsonContent.Create(new { name = "x", price = 1m, barCode = "x" })
        };
        unauthenticated.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenMarker);
        using var unauthenticatedResponse = await client.SendAsync(unauthenticated);
        var account = await client.GetGhostAccount();
        using var forbidden = new HttpRequestMessage(HttpMethod.Get, "/_tests/forbidden");
        forbidden.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.JwToken);
        using var forbiddenResponse = await client.SendAsync(forbidden);

        unauthenticatedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        forbiddenResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var challengedId = await TraceIdAsync(unauthenticatedResponse);
        var forbiddenId = await TraceIdAsync(forbiddenResponse);

        var challenged = await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.AuthenticationChallenged))
            .SingleOrDefault(logEvent => CapturingSink.Scalar(logEvent, "CorrelationId") == challengedId));
        var denied = await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.AuthorizationForbidden))
            .SingleOrDefault(logEvent => CapturingSink.Scalar(logEvent, "CorrelationId") == forbiddenId));
        CapturingSink.EventIdOf(challenged).ShouldBe(1003);
        CapturingSink.EventIdOf(denied).ShouldBe(1004);
        await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
            .FirstOrDefault(logEvent => CapturingSink.Scalar(logEvent, "CorrelationId") == forbiddenId));
        var text = Sink.AllText();
        text.ShouldNotContain(tokenMarker);
        text.ShouldNotContain(account.JwToken);
    }

    [Fact]
    public async Task NestedServices_InheritTheRequestCorrelation()
    {
        var token = (await client.GetGhostAccount()).JwToken;
        var product = await ProductCacheScenario_CreateAsync(token);

        using var response = await client.GetAsync(ApiRoutes.V2.Product.GetProductWithCache(product));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // ProductService (Application) logs below the controller and MediatR; its event carries the request scope.
        var cached = await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.ProductCached))
            .SingleOrDefault(logEvent => CapturingSink.Scalar(logEvent, "ProductId") == product.ToString()));
        var correlationId = CapturingSink.Scalar(cached, "CorrelationId");
        correlationId.ShouldNotBeNullOrWhiteSpace();
        var completed = await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
            .SingleOrDefault(logEvent => CapturingSink.Scalar(logEvent, "CorrelationId") == correlationId));
        CapturingSink.Scalar(completed, "RequestPath").ShouldBe(ApiRoutes.V2.Product.GetProductWithCache(product));
    }

    [Fact]
    public async Task ClientCancellation_IsNotLoggedOrAnsweredAsAnInternalError()
    {
        await Should.ThrowAsync<Exception>(() => client.GetAsync("/_tests/canceled"));

        var canceled = await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCanceled))
            .FirstOrDefault(logEvent => CapturingSink.Scalar(logEvent, "RequestPath") == "/_tests/canceled"));
        canceled.Level.ShouldBe(LogEventLevel.Information);
        canceled.Exception.ShouldBeNull();
        var completed = await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
            .FirstOrDefault(logEvent => CapturingSink.Scalar(logEvent, "RequestPath") == "/_tests/canceled"));
        CapturingSink.Scalar(completed, "StatusCode").ShouldBe("499");
        Sink.WithEventName(nameof(LogEvents.UnhandledException))
            .Where(logEvent => CapturingSink.Scalar(logEvent, "RequestPath") == "/_tests/canceled")
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task RepresentativeSecretsAndPersonalPayloads_NeverReachTheLogs()
    {
        const string password = "Pwd-Marker-7f3a!";
        const string apiKey = "apikey-marker-9c2e";
        const string cookie = "cookie-marker-5d1b";
        const string personal = "personal-payload-marker-3e8f";
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJtYXJrZXIifQ.c2lnbmF0dXJlLW1hcmtlcg";
        const string stamp = "security-stamp-marker-1a2b";
        const string connection = "Host=db;Username=app;Password=conn-secret-marker-6f6f";

        using var login = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.V2.Account.Authenticate + "?api_key=" + apiKey)
        {
            Content = JsonContent.Create(new { userName = personal, password })
        };
        login.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        login.Headers.Add("Cookie", "session=" + cookie);
        using var loginResponse = await client.SendAsync(login);
        var traceId = (await loginResponse.Content.ReadAsStringAsync()).Length > 0 && loginResponse.Content.Headers.ContentType?.MediaType == "application/problem+json"
            ? await TraceIdAsync(loginResponse)
            : null;

        // A careless call site: the enricher still masks secret-named properties and secret-looking values.
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Phase13.RedactionProbe");
        logger.LogWarning(
            "Probe {Password} {ConnectionString} {SecurityStamp} {Authorization} {Detail}",
            password, connection, stamp, "Bearer " + jwt, "token=" + apiKey + "; Password=" + password);

        await WaitForAsync(() => Sink.Events.Any(logEvent => logEvent.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal)) ? true : (bool?)null);
        if (traceId is not null)
        {
            await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
                .FirstOrDefault(logEvent => CapturingSink.Scalar(logEvent, "CorrelationId") == traceId));
        }
        else
        {
            await WaitForAsync(() => Sink.WithEventName(nameof(LogEvents.RequestCompleted))
                .FirstOrDefault(logEvent => CapturingSink.Scalar(logEvent, "RequestPath") == ApiRoutes.V2.Account.Authenticate));
        }

        var text = Sink.AllText();
        foreach (var marker in new[] { password, apiKey, cookie, personal, jwt, stamp, "conn-secret-marker-6f6f" })
            text.ShouldNotContain(marker);
        text.ShouldContain("[REDACTED]");
    }

    private Task<Guid> ProductCacheScenario_CreateAsync(string token) =>
        ApiEndpoints.ProductCacheScenario.CreateProductAsync(client, token).ContinueWith(task => task.Result.Id);

    private static async Task<string> TraceIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("traceId").GetString()!;
    }

    internal static async Task<T> WaitForAsync<T>(Func<T?> probe)
    {
        // Request logs are written after the response is sent, so they may land a moment after the client returns.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var value = probe();
            if (value is not null)
                return value;
            await Task.Delay(50);
        }

        throw new ShouldAssertException("The expected log event was not captured.");
    }
}
