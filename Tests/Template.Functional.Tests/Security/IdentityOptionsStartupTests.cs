using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Identity.Settings;

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

    [Fact]
    public void SigningKeyAbsentFromEverySource_FailsStartup_NamingTheKeyOnly()
    {
        // No committed appsettings file carries the key (Phase 22); the process environment must not supply it either.
        Environment.GetEnvironmentVariable("JwtSettings__Key").ShouldBeNull();
        using var factory = new ConfiguredFactory(new Dictionary<string, string?>(), supplyJwtSigningKey: false);

        var exception = Should.Throw<Exception>(() => factory.CreateClient());
        var messages = Flatten(exception);

        messages.ShouldContain("JwtSettings:Key is required.");
    }

    [Fact]
    public void TestSigningKey_IsGeneratedPerHost_AndNeverCommitted()
    {
        using var first = new ConfiguredFactory(new Dictionary<string, string?>());
        using var second = new ConfiguredFactory(new Dictionary<string, string?>());

        var firstKey = SecurityTestSupport.JwtSettings(first.Services).Key;
        var secondKey = SecurityTestSupport.JwtSettings(second.Services).Key;

        (Encoding.UTF8.GetByteCount(firstKey) >= JwtSettings.MinimumKeyBytes).ShouldBeTrue();
        (firstKey == secondKey).ShouldBeFalse(); // compared without echoing either value
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

    private sealed class ConfiguredFactory(IReadOnlyDictionary<string, string?> values, bool supplyJwtSigningKey = true)
        : CustomWebApplicationFactory<Program>
    {
        protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } = values;
        protected override bool SupplyJwtSigningKey => supplyJwtSigningKey;
    }
}
