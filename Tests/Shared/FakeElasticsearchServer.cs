using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Template.Tests.Shared;

/// <summary>A request received by <see cref="FakeElasticsearchServer"/>.</summary>
public sealed record RecordedRequest(string Method, string Path, string Query, string Body)
{
    public string PathAndQuery => Path + Query;
}

/// <summary>Scripted answer of <see cref="FakeElasticsearchServer"/>.</summary>
public sealed record FakeResponse(int Status, string? Body = null, bool ElasticProductHeader = true, TimeSpan Delay = default, string ContentType = "application/json")
{
    public static FakeResponse Json(int status, string body) => new(status, body);
}

/// <summary>
/// Deterministic stand-in for Elasticsearch: a loopback Kestrel server that records requests and answers through a
/// scripted handler. It lets adapter, health and logging tests exercise HTTP behavior (errors, invalid bodies, delays,
/// restarts) without a real service or any external network access.
/// </summary>
public sealed class FakeElasticsearchServer : IAsyncDisposable
{
    private readonly ConcurrentQueue<RecordedRequest> requests = new();
    private WebApplication? app;

    private FakeElasticsearchServer()
    {
    }

    public int Port { get; private set; }

    public Uri Url => new($"http://127.0.0.1:{Port}");

    public IReadOnlyList<RecordedRequest> Requests => requests.ToArray();

    public Func<RecordedRequest, FakeResponse> Handler { get; set; } = _ => FakeResponse.Json(200, "{}");

    public static async Task<FakeElasticsearchServer> StartAsync(int port = 0)
    {
        var server = new FakeElasticsearchServer();
        await server.StartOnAsync(port);
        return server;
    }

    public void ClearRequests() => requests.Clear();

    /// <summary>Stops listening; connections to <see cref="Url"/> are refused until <see cref="RestartAsync"/>.</summary>
    public async Task StopAsync()
    {
        if (app is null)
            return;
        await app.StopAsync();
        await app.DisposeAsync();
        app = null;
    }

    /// <summary>Listens again on the same port, as a restored service would.</summary>
    public Task RestartAsync() => StartOnAsync(Port);

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task StartOnAsync(int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        var application = builder.Build();
        application.Run(HandleAsync);
        await application.StartAsync();

        var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Port = new Uri(address).Port;
        app = application;
    }

    private async Task HandleAsync(HttpContext context)
    {
        // The client compresses request bodies (gzip) by default.
        var bodyStream = context.Request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase)
            ? new System.IO.Compression.GZipStream(context.Request.Body, System.IO.Compression.CompressionMode.Decompress)
            : context.Request.Body;
        using var reader = new StreamReader(bodyStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        var request = new RecordedRequest(context.Request.Method, context.Request.Path.Value ?? "/", context.Request.QueryString.Value ?? string.Empty, body);
        requests.Enqueue(request);

        var response = Handler(request);
        if (response.Delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(response.Delay, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        context.Response.StatusCode = response.Status;
        if (response.ElasticProductHeader)
            context.Response.Headers["X-Elastic-Product"] = "Elasticsearch";
        if (response.Body is not null && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.ContentType = response.ContentType;
            await context.Response.WriteAsync(response.Body, context.RequestAborted);
        }
    }
}
