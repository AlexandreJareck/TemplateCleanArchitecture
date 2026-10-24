using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Identity.Contexts;
using Template.Infrastructure.Persistence.Contexts;
using Testcontainers.PostgreSql;

// One environment for the whole assembly; tests share the host and run one at a time (they restart it).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Template.WorkflowFunctions.Integration.Tests;

/// <summary>
/// Disposable local environment of the approval orchestration: an isolated Docker network with PostgreSQL 16.15 (the
/// application and Identity databases), Azurite 3.37.0 with blob, queue and table services for the Durable task hub
/// (synthetic account, never the published development key) and the WorkflowFunctions image built from its Dockerfile
/// (or <c>TEMPLATE_WORKFLOW_FUNCTIONS_IMAGE</c>). The host is published on a fixed loopback port so it can be stopped and
/// started again — with another orchestration <c>defaultVersion</c> — against the same task hub and database.
/// </summary>
public sealed class WorkflowEnvironment : IAsyncLifetime
{
    public const string PostgresImage = "postgres:16.15@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d";
    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.37.0@sha256:830430c1da1a2d537e08f3e6764dd1f5ae00cf0346bcaf625b968ec3f0971fd5";
    public const string DefaultFunctionsImage = "template-workflow-functions:test";
    private const string Account = "phase31workflow";
    private const string DatabasePassword = "phase31-synthetic-password";

    private readonly INetwork network = new NetworkBuilder().WithName($"p31-workflow-{Guid.NewGuid():N}"[..24]).Build();
    private readonly string storageKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private PostgreSqlContainer database = null!;
    private IContainer storage = null!;
    private IContainer? host;

    public string InternalKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public int HostPort { get; } = FreeTcpPort();

    public string HostBaseUrl => $"http://127.0.0.1:{HostPort}/api/";

    public string ApplicationConnectionString => Connection("template_app");

    public string IdentityConnectionString => Connection("template_identity");

    public string? HostVersion { get; private set; }

    public async Task InitializeAsync()
    {
        await network.CreateAsync();
        database = new PostgreSqlBuilder(PostgresImage)
            .WithNetwork(network).WithNetworkAliases("db")
            .WithDatabase("template_app").WithUsername("phase31").WithPassword(DatabasePassword)
            .Build();
        storage = new ContainerBuilder(AzuriteImage)
            .WithNetwork(network).WithNetworkAliases("workflow-storage")
            .WithEnvironment("AZURITE_ACCOUNTS", $"{Account}:{storageKey}")
            .WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0", "--location", "/data", "--skipApiVersionCheck")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("sh", "-c", "nc -z 127.0.0.1 10000 && nc -z 127.0.0.1 10001 && nc -z 127.0.0.1 10002"))
            .Build();
        await Task.WhenAll(database.StartAsync(), storage.StartAsync(), EnsureImageAsync());
        await database.ExecScriptAsync("CREATE DATABASE template_identity;");

        await using (var application = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ApplicationConnectionString).Options,
                         new SystemUser()))
        {
            await application.Database.MigrateAsync();
            // D23-1: the organization settings singleton (base currency BRL, time zone) that the WebApi seeds at startup.
            application.Add(Template.Domain.Organization.OrganizationSettings.CreateDefault(DateTime.UtcNow));
            await application.SaveChangesAsync();
        }
        await using (var identity = new IdentityContext(new DbContextOptionsBuilder<IdentityContext>().UseNpgsql(IdentityConnectionString).Options))
            await identity.Database.MigrateAsync();
        await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (host is not null)
            await host.DisposeAsync();
        await storage.DisposeAsync();
        await database.DisposeAsync();
        await network.DeleteAsync();
    }

    /// <summary>Starts the host (stopping a running one first) with an optional orchestration <c>defaultVersion</c> override.</summary>
    public async Task StartHostAsync(string? defaultVersion = null)
    {
        await StopHostAsync();
        var builder = new ContainerBuilder(FunctionsImage)
            .WithNetwork(network)
            .WithPortBinding(HostPort, 80)
            .WithEnvironment("AzureWebJobsStorage",
                $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={storageKey};BlobEndpoint=http://workflow-storage:10000/{Account};" +
                $"QueueEndpoint=http://workflow-storage:10001/{Account};TableEndpoint=http://workflow-storage:10002/{Account};")
            .WithEnvironment("ConnectionStrings__DefaultConnection", InternalConnection("template_app"))
            .WithEnvironment("ConnectionStrings__IdentityConnection", InternalConnection("template_identity"))
            .WithEnvironment("ApprovalWorkflowSettings__Key", InternalKey)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(80).ForPath("/api/health/live")));
        if (defaultVersion is not null)
            builder = builder.WithEnvironment("AzureFunctionsJobHost__extensions__durableTask__defaultVersion", defaultVersion);
        host = builder.Build();
        await host.StartAsync();
        HostVersion = defaultVersion ?? "1.0";
    }

    public async Task StopHostAsync()
    {
        if (host is null)
            return;
        await host.DisposeAsync();
        host = null;
    }

    public async Task<string> HostLogsAsync() => host is null ? string.Empty : string.Join('\n', (await host.GetLogsAsync()).Stdout);

    private static string FunctionsImage =>
        Environment.GetEnvironmentVariable("TEMPLATE_WORKFLOW_FUNCTIONS_IMAGE") is { Length: > 0 } image ? image : DefaultFunctionsImage;

    /// <summary>Builds the host image with the Docker CLI (repository .dockerignore and build cache) unless an image is given.</summary>
    private static async Task EnsureImageAsync()
    {
        if (Environment.GetEnvironmentVariable("TEMPLATE_WORKFLOW_FUNCTIONS_IMAGE") is { Length: > 0 })
            return;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TemplateCleanArchitecture.sln")))
            root = root.Parent;
        using var build = Process.Start(new ProcessStartInfo("docker",
            $"build -q -f Src/Functions/Template.WorkflowFunctions/Dockerfile -t {DefaultFunctionsImage} .")
        {
            WorkingDirectory = root?.FullName ?? throw new InvalidOperationException("Repository root was not found."),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var error = build.StandardError.ReadToEndAsync();
        await build.StandardOutput.ReadToEndAsync();
        await build.WaitForExitAsync();
        if (build.ExitCode != 0)
            throw new InvalidOperationException($"The WorkflowFunctions image did not build: {await error}");
    }

    private string Connection(string databaseName) =>
        $"Host=127.0.0.1;Port={database.GetMappedPublicPort(5432)};Username=phase31;Password={DatabasePassword};Database={databaseName}";

    private static string InternalConnection(string databaseName) =>
        $"Host=db;Port=5432;Username=phase31;Password={DatabasePassword};Database={databaseName}";

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class SystemUser : Template.Application.Interfaces.IAuthenticatedUserService
    {
        public string? UserId => null;
        public string? UserName => null;
    }
}

[CollectionDefinition(Name)]
public sealed class WorkflowCollection : ICollectionFixture<WorkflowEnvironment>
{
    public const string Name = "WorkflowFunctions emulator";
}
