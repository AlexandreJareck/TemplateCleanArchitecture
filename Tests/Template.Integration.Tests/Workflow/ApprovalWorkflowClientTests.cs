using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Interfaces.Approvals;
using Template.Application.Workflow;
using Template.Infrastructure.Workflow;
using Template.Tests.Shared;

namespace Template.Integration.Tests.Workflow;

/// <summary>
/// The Infrastructure workflow client (ADR-0003 §5) against a loopback fake of the WorkflowFunctions internal API: it
/// sends the internal key and the correlation, maps the internal contract, treats 404 as "not running", and reports every
/// network, timeout, credential, server or contract failure as <see cref="ApprovalWorkflowUnavailableException"/>.
/// </summary>
public sealed class ApprovalWorkflowClientTests : IAsyncLifetime
{
    private const string Key = "synthetic-internal-key-0123456789abcdef";
    private static readonly Guid Award = Guid.Parse("8c6a42d0-1111-4222-8333-444455556666");
    private FakeElasticsearchServer server = null!;

    public async Task InitializeAsync() => server = await FakeElasticsearchServer.StartAsync();

    public async Task DisposeAsync() => await server.DisposeAsync();

    [Fact]
    public async Task Start_SendsTheKeyCorrelationAndInput_AndMapsTheResult()
    {
        server.Handler = _ => FakeResponse.Json(202, $$"""{"instanceId":"{{ApprovalWorkflowNames.InstanceIdFor(Award)}}","result":"Started"}""");
        var input = new ApprovalWorkflowInput(ApprovalWorkflowInput.Version1, Award, Guid.NewGuid(), DateTime.UtcNow, "trace-start");

        (await Client().StartAsync(input, default)).ShouldBe(ApprovalWorkflowStartResult.Started);

        var request = server.Requests.Single();
        (request.Method, request.Path).ShouldBe(("POST", "/api/approval-workflows"));
        request.Headers![ApprovalWorkflowHttp.KeyHeader].ShouldBe(Key);
        request.Headers[ApprovalWorkflowHttp.CorrelationHeader].ShouldBe("trace-start");
        request.Headers.ContainsKey("Content-Length").ShouldBeTrue();
        request.Body.ShouldContain($"\"awardId\":\"{Award}\"");
        request.Body.ShouldContain("\"inputVersion\":1");
    }

    [Fact]
    public async Task Status_MapsTheRuntime_AndNotFound()
    {
        server.Handler = _ => FakeResponse.Json(200, $$"""{"instanceId":"x","runtimeStatus":"Running","version":"1.0","createdAt":"2027-01-01T00:00:00Z","lastUpdatedAt":null}""");
        var running = await Client().GetStatusAsync(Award, default);
        running.RuntimeStatus.ShouldBe(ApprovalWorkflowRuntimeStatus.Running);
        running.IsActive.ShouldBeTrue();
        server.Requests.Single().Path.ShouldBe($"/api/approval-workflows/{ApprovalWorkflowNames.InstanceIdFor(Award)}");

        server.Handler = _ => new FakeResponse(404);
        (await Client().GetStatusAsync(Award, default)).RuntimeStatus.ShouldBe(ApprovalWorkflowRuntimeStatus.NotFound);
        server.Handler = _ => FakeResponse.Json(200, """{"instanceId":"x","runtimeStatus":"Exotic"}""");
        (await Client().GetStatusAsync(Award, default)).RuntimeStatus.ShouldBe(ApprovalWorkflowRuntimeStatus.Unknown);
    }

    [Fact]
    public async Task SignalAndTerminate_TreatNotFoundAsNotRunning()
    {
        server.Handler = request => request.Path.EndsWith("/signals", StringComparison.Ordinal)
            ? FakeResponse.Json(200, """{"instanceId":"x","result":"Delivered"}""")
            : FakeResponse.Json(200, """{"instanceId":"x","result":"Terminated"}""");
        (await Client().SignalAsync(Award, new ApprovalSignal(Guid.NewGuid(), ApprovalSignalKinds.Decision, "trace"), default)).ShouldBe(ApprovalWorkflowSignalResult.Delivered);
        (await Client().TerminateAsync(Award, "operator stop", default)).ShouldBeTrue();

        server.Handler = _ => new FakeResponse(404);
        (await Client().SignalAsync(Award, new ApprovalSignal(Guid.NewGuid(), ApprovalSignalKinds.Ended, "trace"), default)).ShouldBe(ApprovalWorkflowSignalResult.NotRunning);
        (await Client().TerminateAsync(Award, "operator stop", default)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task ServerAndCredentialFailures_AreUnavailable(int status)
    {
        server.Handler = _ => new FakeResponse(status);

        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() => Client().GetStatusAsync(Award, default));
    }

    [Fact]
    public async Task Timeout_MalformedBody_UnknownResult_AndRefusedConnection_AreUnavailable()
    {
        server.Handler = _ => new FakeResponse(200, "{}", Delay: TimeSpan.FromSeconds(5));
        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() => Client(TimeSpan.FromMilliseconds(300)).GetStatusAsync(Award, default));

        server.Handler = _ => FakeResponse.Json(200, "not-json");
        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() => Client().GetStatusAsync(Award, default));

        server.Handler = _ => FakeResponse.Json(202, """{"instanceId":"x","result":"Sideways"}""");
        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() =>
            Client().StartAsync(new ApprovalWorkflowInput(1, Award, Guid.NewGuid(), DateTime.UtcNow, "trace"), default));

        await server.StopAsync();
        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() => Client().GetStatusAsync(Award, default));
    }

    [Fact]
    public async Task CallerCancellation_IsNotReportedAsUnavailable()
    {
        server.Handler = _ => new FakeResponse(200, "{}", Delay: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() => Client().GetStatusAsync(Award, cancellation.Token));
    }

    [Fact]
    public async Task Registration_SelectsTheClientBySetting_AndValidatesOnlyWhenEnabled()
    {
        (await Resolve(enabled: false)).ShouldBeOfType<UnavailableApprovalWorkflowClient>();
        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() => new UnavailableApprovalWorkflowClient().GetStatusAsync(Award, default));
        (await Resolve(enabled: true)).ShouldBeOfType<HttpApprovalWorkflowClient>();

        var validator = new ApprovalWorkflowSettingsValidator();
        validator.Validate(null, new ApprovalWorkflowSettings { Enabled = false }).Succeeded.ShouldBeTrue();
        var invalid = validator.Validate(null, new ApprovalWorkflowSettings { Enabled = true, BaseUrl = "ftp://host", Key = "short", Timeout = TimeSpan.Zero });
        invalid.Failed.ShouldBeTrue();
        invalid.Failures!.Count().ShouldBe(3);
        invalid.FailureMessage.ShouldNotContain("short");
        validator.Validate(null, new ApprovalWorkflowSettings { Enabled = true, BaseUrl = "http://workflow-functions/api/", Key = Key }).Succeeded.ShouldBeTrue();

        // The Test-environment client contacts no host: an enabled section without a key must not fail startup.
        var testConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApprovalWorkflowSettings:Enabled"] = "true",
            ["ApprovalWorkflowSettings:Key"] = ""
        }).Build();
        var testServices = new ServiceCollection();
        testServices.AddLogging();
        testServices.AddWorkflowInfrastructure(testConfiguration, useInMemoryClient: true);
        await using var testProvider = testServices.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        testProvider.GetServices<IValidateOptions<ApprovalWorkflowSettings>>().ShouldBeEmpty();
        testProvider.GetRequiredService<IApprovalWorkflowClient>().ShouldBeOfType<InMemoryApprovalWorkflowClient>();
    }

    [Fact]
    public async Task InMemoryClient_RecordsStartsAndSignals_AndSimulatesAnOutage()
    {
        var client = new InMemoryApprovalWorkflowClient();
        var input = new ApprovalWorkflowInput(1, Award, Guid.NewGuid(), DateTime.UtcNow, "trace");

        (await client.SignalAsync(Award, new ApprovalSignal(Guid.NewGuid(), "decision", "trace"), default)).ShouldBe(ApprovalWorkflowSignalResult.NotRunning);
        (await client.StartAsync(input, default)).ShouldBe(ApprovalWorkflowStartResult.Started);
        (await client.StartAsync(input, default)).ShouldBe(ApprovalWorkflowStartResult.AlreadyRunning);
        (await client.SignalAsync(Award, new ApprovalSignal(Guid.NewGuid(), "decision", "trace"), default)).ShouldBe(ApprovalWorkflowSignalResult.Delivered);
        client.Complete(Award);
        (await client.GetStatusAsync(Award, default)).RuntimeStatus.ShouldBe(ApprovalWorkflowRuntimeStatus.Completed);
        (await client.StartAsync(input, default)).ShouldBe(ApprovalWorkflowStartResult.Restarted);
        (await client.TerminateAsync(Award, "stop", default)).ShouldBeTrue();
        (await client.TerminateAsync(Guid.NewGuid(), "stop", default)).ShouldBeFalse();
        client.Starts.Count.ShouldBe(3);
        client.Signals.ShouldHaveSingleItem();

        client.Unavailable = true;
        await Should.ThrowAsync<ApprovalWorkflowUnavailableException>(() => client.GetStatusAsync(Award, default));
    }

    private HttpApprovalWorkflowClient Client(TimeSpan? timeout = null)
    {
        var settings = new ApprovalWorkflowSettings { Enabled = true, BaseUrl = $"{server.Url}api/", Key = Key, Timeout = timeout ?? TimeSpan.FromSeconds(5) };
        return new HttpApprovalWorkflowClient(new HttpClient { BaseAddress = new Uri(settings.BaseUrl) }, Options.Create(settings),
            NullLogger<HttpApprovalWorkflowClient>.Instance);
    }

    private async Task<IApprovalWorkflowClient> Resolve(bool enabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApprovalWorkflowSettings:Enabled"] = enabled.ToString(),
            ["ApprovalWorkflowSettings:BaseUrl"] = $"{server.Url}api/",
            ["ApprovalWorkflowSettings:Key"] = Key
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowInfrastructure(configuration, useInMemoryClient: false);
        await using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IApprovalWorkflowClient>();
    }
}
