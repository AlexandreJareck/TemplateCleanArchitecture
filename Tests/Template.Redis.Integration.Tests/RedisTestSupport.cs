using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Template.Application.DTOs.Products;
using Template.Infrastructure.Caching;
using Template.Infrastructure.Caching.Products;

// Real-server classes run one at a time: each owns containers on fixed loopback ports and some pause their server.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Template.Redis.Integration.Tests;

/// <summary>The Phase 15 target: the same image and persistence arguments as the redis service in docker-compose.yml.</summary>
public static class RedisImage
{
    public const string Name = "redis:8.10.2@sha256:2c2dff791878316e3b083188be0d578423b24813cdda5de0e80ea4f7b3e6bfd6";

    public static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public static string NewSecret() => $"ephemeral-{Guid.NewGuid():N}";
}

/// <summary>
/// A disposable Redis 8 server on a fixed loopback port (stable across stop/start) with a synthetic password.
/// Nothing here connects to a developer-hosted Redis.
/// </summary>
public sealed class RedisServer : IAsyncLifetime, IAsyncDisposable
{
    public string Password { get; } = RedisImage.NewSecret();

    public int Port { get; } = RedisImage.FreeTcpPort();

    public IContainer Container { get; }

    public RedisServer()
    {
        Container = new ContainerBuilder(RedisImage.Name)
            .WithPortBinding(Port, 6379)
            .WithCommand("--requirepass", Password, "--save", "3600 1 300 100 60 10000", "--appendonly", "no", "--maxmemory-policy", "noeviction")
            // Readiness by command, not by log line: under WSL the server's log timestamps can precede the container's
            // recorded start, and a log wait that reads "since start" can then miss the message and never complete.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("sh", "-c", "redis-cli -a \"$REDIS_PROBE_SECRET\" --no-auth-warning ping | grep -q PONG"))
            .WithEnvironment("REDIS_PROBE_SECRET", Password)
            .Build();
    }

    public string Endpoint => $"127.0.0.1:{Port}";

    public string ConnectionString(string? user = null, string? password = null) =>
        $"{Endpoint},{(user is null ? string.Empty : $"user={user},")}password={password ?? Password},connectTimeout=2000,syncTimeout=2000,asyncTimeout=2000,abortConnect=false";

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();

    ValueTask IAsyncDisposable.DisposeAsync() => Container.DisposeAsync();

    public Task<ConnectionMultiplexer> AdminAsync() => ConnectionMultiplexer.ConnectAsync(ConnectionString() + ",allowAdmin=true");

    public async Task RestartAsync()
    {
        await Container.StopAsync();
        await Container.StartAsync();
    }
}

public static class CacheFactory
{
    public static ProductDto Product(Guid id, string name = "Redis 8 product") =>
        new() { Id = id, Name = name, BarCode = "7891234567890", Price = 1234.56m, CreatedDateTime = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc) };

    public static (DistributedProductCache Cache, RedisCache Backend) Create(string connectionString, CacheSettings? settings = null, ILogger<DistributedProductCache>? logger = null)
    {
        var backend = new RedisCache(Options.Create(new RedisCacheOptions { Configuration = connectionString }));
        // Open the connection outside the adapter's operation timeout: the first call also connects, which can exceed a
        // short timeout on a loaded host and would turn the operation under test into a (correct, but unintended) miss.
        try { backend.Get("template:product:v1:connection-warmup"); } catch (Exception) { /* unreachable or rejected: the test covers it */ }
        var cache = new DistributedProductCache(backend, Options.Create(settings ?? new CacheSettings()), logger ?? new CapturingLogger<DistributedProductCache>());
        return (cache, backend);
    }
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Enqueue($"{eventId.Id}|{formatter(state, exception)}|{exception?.Message}");
}
