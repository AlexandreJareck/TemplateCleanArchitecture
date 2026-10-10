using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Template.Functional.Tests.Common;
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    /// <summary>Extra configuration applied on top of appsettings.Test.json.</summary>
    protected virtual IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } = new Dictionary<string, string?>();

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
                ["TestDatabaseName"] = Guid.NewGuid().ToString("N")
            };
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
