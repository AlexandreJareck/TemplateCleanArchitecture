using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Template.Functional.Tests.Common;
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    /// <summary>Configuration key of the JWT signing key, which no committed appsettings file carries (Phase 22).</summary>
    internal const string JwtSigningKeyName = "JwtSettings:Key";

    /// <summary>Extra configuration applied on top of appsettings.Test.json.</summary>
    protected virtual IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } = new Dictionary<string, string?>();

    /// <summary>
    /// When true (default) the host receives a random signing key generated for this factory instance. It stays in
    /// memory and is never written to test output.
    /// </summary>
    protected virtual bool SupplyJwtSigningKey => true;

    /// <summary>Service overrides applied after the application's own registrations.</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(ConfigureTestServices);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["TestDatabaseName"] = Guid.NewGuid().ToString("N"),
                // No committed file carries credentials (Phase 22). The Test host never contacts a broker, but the
                // validated RabbitMqSettings still require one; a random synthetic value satisfies it.
                ["RabbitMqSettings:Password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                // Shared fixtures make many session calls from one test address; SEC-007 is proven by a dedicated
                // factory that restores the policy value (10 per minute).
                ["SessionSettings:RateLimitPermitLimit"] = "100000"
            };
            if (SupplyJwtSigningKey)
            {
                values[JwtSigningKeyName] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            }

            foreach (var (key, value) in AdditionalConfiguration)
            {
                values[key] = value;
            }

            configuration.AddInMemoryCollection(values);
        });

        var host = builder.Build();
        host.Start();

        return host;
    }
}
