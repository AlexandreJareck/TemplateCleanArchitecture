using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Template.Functional.Tests.Common;
public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    /// <summary>Extra configuration applied on top of appsettings.Test.json.</summary>
    protected virtual IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } = new Dictionary<string, string?>();

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
