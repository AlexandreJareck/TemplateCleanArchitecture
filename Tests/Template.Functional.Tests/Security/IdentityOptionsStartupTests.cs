using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.Security;

/// <summary>Startup fails safely on invalid Identity/JWT configuration and names keys, never values.</summary>
public sealed class IdentityOptionsStartupTests
{
    private const string SentinelValue = "synthetic-sentinel-value";

    [Fact]
    public void ValidConfiguration_StartsInTestEnvironmentOnly()
    {
        using var factory = new ConfiguredFactory(new Dictionary<string, string?>());
        using var _ = factory.CreateClient();

        var environment = factory.Services.GetRequiredService<IHostEnvironment>();
        environment.EnvironmentName.ShouldBe("Test"); // appsettings.Test.json, never Production
    }

    [Theory]
    [InlineData("JwtSettings:Key", "", "JwtSettings:Key")]
    [InlineData("JwtSettings:Key", SentinelValue, "JwtSettings:Key")]
    [InlineData("JwtSettings:Issuer", "", "JwtSettings:Issuer")]
    [InlineData("JwtSettings:Audience", "", "JwtSettings:Audience")]
    [InlineData("JwtSettings:DurationInMinutes", "0", "JwtSettings:DurationInMinutes")]
    [InlineData("JwtSettings:DurationInMinutes", "100000", "JwtSettings:DurationInMinutes")]
    [InlineData("IdentitySettings:PasswordRequiredLength", "0", "IdentitySettings:PasswordRequiredLength")]
    public void InvalidConfiguration_FailsStartup_NamingTheKeyOnly(string key, string value, string expectedKey)
    {
        using var factory = new ConfiguredFactory(new Dictionary<string, string?> { [key] = value });

        var exception = Should.Throw<Exception>(() => factory.CreateClient());
        var messages = Flatten(exception);

        messages.ShouldContain(expectedKey);
        messages.ShouldNotContain(SentinelValue);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
                messages.AddRange(aggregate.InnerExceptions.Select(Flatten));
        }

        return string.Join(Environment.NewLine, messages);
    }

    private sealed class ConfiguredFactory(IReadOnlyDictionary<string, string?> values) : CustomWebApplicationFactory<Program>
    {
        protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } = values;
    }
}
