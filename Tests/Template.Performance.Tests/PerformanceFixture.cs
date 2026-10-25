using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Interfaces;
using Template.Infrastructure.Persistence.Contexts;
using Testcontainers.PostgreSql;

namespace Template.Performance.Tests;

/// <summary>
/// One disposable PostgreSQL 16.15 (application and Identity databases), the real WebApi pipeline on top of it (Test
/// environment: in-memory cache, workflow client and file storage; real EF Core/Npgsql, Identity and authorization),
/// the demo personas for authenticated calls and the NFR dataset. Everything is synthetic and per run.
/// </summary>
public sealed class PerformanceFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:16.15@sha256:ca0bd484cb98bf4b24eb1010e73fb3fcbd6714d240fbc1a10eea5b7dbecb641d";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(PostgresImage)
        .WithDatabase("nfr_application")
        .WithUsername("nfr_user")
        .WithPassword($"ephemeral-{Guid.NewGuid():N}")
        .Build();

    public string DemoPassword { get; } = $"Demo!9{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";

    public WebApplicationFactory<Program> Api { get; private set; } = null!;

    public SeededDataset Dataset { get; private set; } = null!;

    public string ApplicationConnectionString { get; private set; } = string.Empty;

    public string IdentityConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        ApplicationConnectionString = container.GetConnectionString();
        await using (var connection = new NpgsqlConnection(ApplicationConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE nfr_identity", connection);
            await create.ExecuteNonQueryAsync();
        }

        IdentityConnectionString = new NpgsqlConnectionStringBuilder(ApplicationConnectionString) { Database = "nfr_identity" }.ConnectionString;
        Api = new ApiFactory(this);
        _ = Api.Server; // starts the host: migrations, roles, demo personas and demo master data
        Dataset = await NfrDatasetSeeder.SeedAsync(CreateContext, new NfrVolumes());
        await using var analyze = new NpgsqlConnection(ApplicationConnectionString);
        await analyze.OpenAsync();
        await using var command = new NpgsqlCommand("ANALYZE", analyze) { CommandTimeout = 600 };
        await command.ExecuteNonQueryAsync(); // planner statistics after the bulk load, as autovacuum would eventually do
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await container.DisposeAsync();
    }

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ApplicationConnectionString).Options, new SeedUser());

    /// <summary>Signs a demo persona in through the session endpoint and returns a client carrying its access token.</summary>
    public async Task<HttpClient> SignInAsync(string userName)
    {
        var client = Api.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v2/auth/sessions", new { userName, password = DemoPassword });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class ApiFactory(PerformanceFixture fixture) : WebApplicationFactory<Program>
    {
        // UseSetting applies before Program reads UseInMemoryDatabase; configuration added in CreateHost comes too late.
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("UseInMemoryDatabase", "false");
            builder.UseSetting("ConnectionStrings:DefaultConnection", fixture.ApplicationConnectionString);
            builder.UseSetting("ConnectionStrings:IdentityConnection", fixture.IdentityConnectionString);
            builder.UseSetting("DemoData:Enabled", "true");
            builder.UseSetting("DemoData:Password", fixture.DemoPassword);
            builder.UseSetting("JwtSettings:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("RabbitMqSettings:Password", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
            // Many sign-ins and commands from one test address; SEC-007 is proven elsewhere.
            builder.UseSetting("SessionSettings:RateLimitPermitLimit", "100000");
            // Request logs at Information would dominate the measured time; warnings still surface.
            builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        }
    }

    private sealed record SeedUser : IAuthenticatedUserService
    {
        public string UserId { get; } = Template.Application.Demo.DemoDataIds.Administrator.ToString();
        public string UserName { get; } = "nfr-performance-seed";
    }
}

[CollectionDefinition(Name)]
public sealed class PerformanceCollection : ICollectionFixture<PerformanceFixture>
{
    public const string Name = "NFR performance";
}
