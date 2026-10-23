using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Interfaces.Files;
using Template.Application.Services.Files;
using Template.Infrastructure.Files;

// One disposable Azurite per class on a fixed loopback port (stable across stop/start); classes run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Template.Azurite.Integration.Tests;

/// <summary>The Compose image (docker-compose.yml azurite service); <c>TEMPLATE_AZURITE_TEST_IMAGE</c> selects another pinned image.</summary>
public static class AzuriteImage
{
    public const string Default = "mcr.microsoft.com/azure-storage/azurite:3.37.0@sha256:830430c1da1a2d537e08f3e6764dd1f5ae00cf0346bcaf625b968ec3f0971fd5";

    public static string Name => Environment.GetEnvironmentVariable("TEMPLATE_AZURITE_TEST_IMAGE") is { Length: > 0 } image ? image : Default;

    public static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>
/// A disposable Azurite blob service with a synthetic account (never the published development account) on a fixed
/// loopback port, started with the same arguments as Compose.
/// </summary>
public sealed class AzuriteServer : IAsyncLifetime
{
    public const string Account = "phase30tests";

    public AzuriteServer()
    {
        Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Container = new ContainerBuilder(AzuriteImage.Name)
            .WithPortBinding(Port, 10000)
            .WithEnvironment("AZURITE_ACCOUNTS", $"{Account}:{Key}")
            .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--blobPort", "10000", "--location", "/data", "--skipApiVersionCheck")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("sh", "-c", "nc -z 127.0.0.1 10000"))
            .Build();
    }

    public string Key { get; }

    public int Port { get; } = AzuriteImage.FreeTcpPort();

    public IContainer Container { get; }

    public string ConnectionString => $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Key};BlobEndpoint=http://127.0.0.1:{Port}/{Account};";

    public BlobFileStorage Storage(string container = "template-files", TimeSpan? timeout = null) =>
        new(ConnectionString, Options.Create(new FileStorageSettings
        {
            Enabled = true,
            ContainerName = container,
            OperationTimeout = timeout ?? TimeSpan.FromSeconds(10),
            MaxRetries = 1
        }), NullLogger<BlobFileStorage>.Instance);

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

/// <summary>
/// <see cref="BlobFileStorage"/> against real Azurite (D23-7, FIL-001..005): streamed create-only writes with server-side
/// SHA-256, read-back verification, size limits leaving no object, private containers, idempotent deletes, outage and
/// recovery, restart persistence and cancellation. All content is synthetic.
/// </summary>
public sealed class AzuriteBlobStorageTests : IClassFixture<AzuriteServer>
{
    private readonly AzuriteServer server;

    public AzuriteBlobStorageTests(AzuriteServer server) => this.server = server;

    [Fact]
    public async Task T_FIL_001_c_StreamedWrite_ReadBack_Exists_AndIdempotentDelete()
    {
        var storage = server.Storage();
        var id = Guid.NewGuid();
        var bytes = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 17); // larger than one transfer block

        var stored = await storage.WriteAsync(id, new MemoryStream(bytes), "application/pdf", 10L * 1024 * 1024, CancellationToken.None);

        stored.ShouldBe(new StoredContent(bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
        (await FileContent.MeasureAsync(storage, id, CancellationToken.None)).ShouldBe(stored);
        (await storage.ExistsAsync(id, CancellationToken.None)).ShouldBeTrue();
        await using (var read = (await storage.OpenReadAsync(id, CancellationToken.None))!)
        {
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            copy.ToArray().ShouldBe(bytes);
        }

        (await storage.DeleteAsync(id, CancellationToken.None)).ShouldBeTrue();
        (await storage.DeleteAsync(id, CancellationToken.None)).ShouldBeFalse();
        (await storage.OpenReadAsync(id, CancellationToken.None)).ShouldBeNull();
        (await storage.ExistsAsync(id, CancellationToken.None)).ShouldBeFalse();
        (await storage.IsAvailableAsync(CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task T_FIL_002_g_ObjectsAreNeverOverwritten_AndOversizedWritesLeaveNothing()
    {
        var storage = server.Storage();
        var id = Guid.NewGuid();
        await storage.WriteAsync(id, new MemoryStream("%PDF-first"u8.ToArray()), "application/pdf", 1024, CancellationToken.None);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            storage.WriteAsync(id, new MemoryStream("%PDF-second"u8.ToArray()), "application/pdf", 1024, CancellationToken.None));
        (await FileContent.MeasureAsync(storage, id, CancellationToken.None))!.Length.ShouldBe("%PDF-first"u8.Length);

        var oversized = Guid.NewGuid();
        await Should.ThrowAsync<FileContentTooLargeException>(() =>
            storage.WriteAsync(oversized, new MemoryStream(new byte[6 * 1024 * 1024]), "application/pdf", 5L * 1024 * 1024, CancellationToken.None));
        (await storage.ExistsAsync(oversized, CancellationToken.None)).ShouldBeFalse();
        await Should.ThrowAsync<FileContentTooLargeException>(() =>
            storage.WriteAsync(Guid.NewGuid(), new MemoryStream(new byte[2048]), "application/pdf", 1024, CancellationToken.None));
    }

    [Fact]
    public async Task T_FIL_003_e_ContainerIsPrivate_AnonymousReadsAndListingsAreRejected()
    {
        var storage = server.Storage("private-check");
        var id = Guid.NewGuid();
        await storage.WriteAsync(id, new MemoryStream("%PDF-secret"u8.ToArray()), "application/pdf", 1024, CancellationToken.None);

        using var anonymous = new HttpClient();
        var blob = await anonymous.GetAsync($"http://127.0.0.1:{server.Port}/{AzuriteServer.Account}/private-check/{BlobFileStorage.BlobName(id)}");
        blob.IsSuccessStatusCode.ShouldBeFalse();
        var listing = await anonymous.GetAsync($"http://127.0.0.1:{server.Port}/{AzuriteServer.Account}/private-check?restype=container&comp=list");
        listing.IsSuccessStatusCode.ShouldBeFalse();
        (await listing.Content.ReadAsStringAsync()).ShouldNotContain(id.ToString("N"));
    }

    [Fact]
    public async Task T_FIL_003_f_WrongKey_IsUnavailable_WithoutExposingTheKey()
    {
        var wrongKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var storage = new BlobFileStorage(server.ConnectionString.Replace(server.Key, wrongKey, StringComparison.Ordinal),
            Options.Create(new FileStorageSettings { Enabled = true, OperationTimeout = TimeSpan.FromSeconds(5), MaxRetries = 0 }),
            NullLogger<BlobFileStorage>.Instance);
        var failure = await Should.ThrowAsync<FileStorageUnavailableException>(() =>
            storage.WriteAsync(Guid.NewGuid(), new MemoryStream("%PDF-"u8.ToArray()), "application/pdf", 1024, CancellationToken.None));
        failure.Message.ShouldNotContain(wrongKey);
        failure.Message.ShouldNotContain(server.Key);
        (await storage.IsAvailableAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task T_FIL_003_g_OutageIsUnavailable_AndStorageRecoversWithDataAfterRestart()
    {
        var storage = server.Storage(timeout: TimeSpan.FromSeconds(3));
        var id = Guid.NewGuid();
        var bytes = "%PDF-persisted across restart"u8.ToArray();
        await storage.WriteAsync(id, new MemoryStream(bytes), "application/pdf", 1024, CancellationToken.None);

        await server.Container.StopAsync();
        try
        {
            await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.OpenReadAsync(id, CancellationToken.None));
            await Should.ThrowAsync<FileStorageUnavailableException>(() =>
                storage.WriteAsync(Guid.NewGuid(), new MemoryStream(bytes), "application/pdf", 1024, CancellationToken.None));
            (await storage.IsAvailableAsync(CancellationToken.None)).ShouldBeFalse();
        }
        finally
        {
            await server.Container.StartAsync();
        }

        var recovered = await WaitAsync(() => FileContent.MeasureAsync(storage, id, CancellationToken.None));
        recovered!.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    [Fact]
    public async Task T_FIL_003_h_CallerCancellationIsNotReportedAsUnavailability()
    {
        var storage = server.Storage();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            storage.WriteAsync(Guid.NewGuid(), new MemoryStream(new byte[1024]), "application/pdf", 4096, cancelled.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => storage.OpenReadAsync(Guid.NewGuid(), cancelled.Token));
    }

    private static async Task<T> WaitAsync<T>(Func<Task<T>> probe)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                return await probe();
            }
            catch (FileStorageUnavailableException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }
}
