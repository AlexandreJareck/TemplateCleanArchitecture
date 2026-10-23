using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Interfaces.Files;
using Template.Application.Services.Files;
using Template.Infrastructure.Files;

namespace Template.Integration.Tests.Files;

/// <summary>
/// Files adapters without a storage service: the in-memory store (Test), the disabled store, registration and options
/// validation, and the blob adapter against an unreachable loopback endpoint (bounded, classified unavailability, no
/// secret in messages). Real Azurite behavior is covered by the opt-in Azurite suite.
/// </summary>
public sealed class FileStorageAdapterTests
{
    [Fact]
    public async Task T_FIL_002_e_InMemoryStorage_HashesWhileWriting_LimitsSize_AndNeverOverwrites()
    {
        var storage = new InMemoryFileStorage();
        var id = Guid.NewGuid();
        var bytes = "%PDF-1.7 synthetic"u8.ToArray();

        var stored = await storage.WriteAsync(id, new MemoryStream(bytes), "application/pdf", 1024, CancellationToken.None);

        stored.ShouldBe(new StoredContent(bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
        (await FileContent.MeasureAsync(storage, id, CancellationToken.None)).ShouldBe(stored);
        await Should.ThrowAsync<InvalidOperationException>(() => storage.WriteAsync(id, new MemoryStream(bytes), "application/pdf", 1024, CancellationToken.None));
        await Should.ThrowAsync<FileContentTooLargeException>(() => storage.WriteAsync(Guid.NewGuid(), new MemoryStream(new byte[2048]), "application/pdf", 1024, CancellationToken.None));
        storage.Count.ShouldBe(1);
        (await storage.ExistsAsync(id, CancellationToken.None)).ShouldBeTrue();
        (await storage.DeleteAsync(id, CancellationToken.None)).ShouldBeTrue();
        (await storage.DeleteAsync(id, CancellationToken.None)).ShouldBeFalse();
        (await storage.OpenReadAsync(id, CancellationToken.None)).ShouldBeNull();
        (await FileContent.MeasureAsync(storage, id, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, true)]
    [InlineData("application/pdf", new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, false)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14 }, true)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }, false)]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData("text/html", new byte[] { 0x3C, 0x68 }, false)]
    [InlineData("application/pdf", new byte[] { 0x25 }, false)]
    public async Task T_FIL_002_f_ContentSniffingMustMatchTheDeclaredType(string mediaType, byte[] content, bool matches)
    {
        var stream = new MemoryStream(content);
        (await FileContent.MatchesAsync(stream, mediaType, CancellationToken.None)).ShouldBe(matches);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public async Task T_FIL_003_c_DisabledStorage_ReportsUnavailability_ForEveryOperation()
    {
        var storage = new UnavailableFileStorage();
        await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.WriteAsync(Guid.NewGuid(), new MemoryStream([1]), "application/pdf", 10, CancellationToken.None));
        await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.OpenReadAsync(Guid.NewGuid(), CancellationToken.None));
        await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
        await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.ExistsAsync(Guid.NewGuid(), CancellationToken.None));
        (await storage.IsAvailableAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task T_FIL_003_d_BlobAdapter_AgainstAnUnreachableEndpoint_FailsFastAsUnavailable_WithoutLeakingTheKey()
    {
        var port = FreePort();
        const string key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
        var storage = new BlobFileStorage(
            $"DefaultEndpointsProtocol=http;AccountName=unreachable;AccountKey={key};BlobEndpoint=http://127.0.0.1:{port}/unreachable;",
            Options.Create(new FileStorageSettings { Enabled = true, OperationTimeout = TimeSpan.FromSeconds(2), MaxRetries = 0 }),
            NullLogger<BlobFileStorage>.Instance);

        var started = DateTime.UtcNow;
        var write = await Should.ThrowAsync<FileStorageUnavailableException>(
            () => storage.WriteAsync(Guid.NewGuid(), new MemoryStream("%PDF-"u8.ToArray()), "application/pdf", 1024, CancellationToken.None));
        write.Message.ShouldNotContain(key);
        write.Message.ShouldNotContain("127.0.0.1");
        await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.OpenReadAsync(Guid.NewGuid(), CancellationToken.None));
        await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
        await Should.ThrowAsync<FileStorageUnavailableException>(() => storage.ExistsAsync(Guid.NewGuid(), CancellationToken.None));
        (await storage.IsAvailableAsync(CancellationToken.None)).ShouldBeFalse();
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(30));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => storage.OpenReadAsync(Guid.NewGuid(), cancelled.Token));
        BlobFileStorage.BlobName(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef")).ShouldBe("v1/0123456789abcdef0123456789abcdef");
    }

    [Fact]
    public void T_FIL_001_b_Registration_SelectsTheAdapter_AndValidatesOptions()
    {
        Resolve(new Dictionary<string, string?>(), inMemory: true).GetRequiredService<IFileStorage>().ShouldBeOfType<InMemoryFileStorage>();
        Resolve(new Dictionary<string, string?> { ["FileStorageSettings:Enabled"] = "false" }, inMemory: false)
            .GetRequiredService<IFileStorage>().ShouldBeOfType<UnavailableFileStorage>();
        var blob = Resolve(new Dictionary<string, string?>
        {
            ["FileStorageSettings:Enabled"] = "true",
            ["ConnectionStrings:BlobStorage"] = "DefaultEndpointsProtocol=http;AccountName=a;AccountKey=MDEyMzQ1Njc4OWFiY2RlZg==;BlobEndpoint=http://127.0.0.1:1/a;"
        }, inMemory: false);
        blob.GetRequiredService<IFileStorage>().ShouldBeOfType<BlobFileStorage>();
        blob.GetRequiredService<IFileStorageProbe>().ShouldBeSameAs(blob.GetRequiredService<IFileStorage>());
        blob.GetRequiredService<IMalwareScanner>().ShouldBeOfType<NoOpMalwareScanner>();

        var missing = Resolve(new Dictionary<string, string?> { ["FileStorageSettings:Enabled"] = "true" }, inMemory: false);
        Should.Throw<OptionsValidationException>(() => missing.GetRequiredService<IOptions<FileStorageSettings>>().Value)
            .Message.ShouldContain("ConnectionStrings:BlobStorage");
        foreach (var invalid in new[] { "UPPER", "a", "bad--name", "-bad", new string('a', 64) })
            FileStorageSettings.IsValid(new FileStorageSettings { ContainerName = invalid }).ShouldBeFalse(invalid);
        FileStorageSettings.IsValid(new FileStorageSettings { MaxRetries = 6 }).ShouldBeFalse();
        FileStorageSettings.IsValid(new FileStorageSettings()).ShouldBeTrue();
    }

    [Fact]
    public async Task T_FIL_006_a_TheMalwareScanHookExists_AndClaimsNothing() =>
        (await new NoOpMalwareScanner().ScanAsync(Guid.NewGuid(), "application/pdf", CancellationToken.None)).ShouldBe(MalwareScanVerdict.NotScanned);

    private static ServiceProvider Resolve(Dictionary<string, string?> values, bool inMemory)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFilesInfrastructure(configuration, inMemory);
        return services.BuildServiceProvider();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
