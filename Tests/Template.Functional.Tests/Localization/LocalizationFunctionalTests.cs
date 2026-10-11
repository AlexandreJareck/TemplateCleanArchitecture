using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.DTOs;
using Template.Application.Interfaces;
using Template.Application.Settings;
using Template.Functional.Tests.Common;
using Template.WebApi.Infrastructure.Extensions;

namespace Template.Functional.Tests.Localization;

/// <summary>
/// Localization contract: options validated at startup without an intermediate provider, request culture from the
/// query string, cookie or <c>Accept-Language</c> with fallback to the default, no culture leakage across concurrent
/// requests, and missing-key fallback. Messages below are the committed resource values.
/// </summary>
public sealed class LocalizationFunctionalTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private const string PortugueseNotFound = "Produto com ID";
    private const string EnglishNotFound = "Product with ID";

    private readonly HttpClient client = factory.CreateClient();

    [Theory]
    [InlineData(null, PortugueseNotFound)]   // default culture pt-BR
    [InlineData("pt-BR", PortugueseNotFound)]
    [InlineData("en", EnglishNotFound)]
    [InlineData("en-US", EnglishNotFound)]   // parent-culture fallback of a supported culture
    [InlineData("fa", EnglishNotFound)]      // supported, but without resources: neutral (English) resources
    [InlineData("de-DE", PortugueseNotFound)] // unsupported: default culture
    public async Task NotFoundBusinessMessage_FollowsTheRequestCulture(string? acceptLanguage, string expected)
    {
        var message = await NotFoundMessageAsync(acceptLanguage);

        message.ShouldStartWith(expected);
    }

    [Fact]
    public async Task QueryStringCulture_TakesPrecedenceOverAcceptLanguage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.V2.Product.GetProductById(Guid.NewGuid()) + "?culture=en");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("pt-BR"));

        using var response = await client.SendAsync(request);

        (await ErrorMessageAsync(response)).ShouldStartWith(EnglishNotFound);
    }

    [Theory]
    [InlineData("pt-BR", "Nome", "Código de barras")]
    [InlineData("en", "Name", "Barcode")]
    public async Task ValidationMessages_UseLocalizedFieldNames(string culture, string name, string barCode)
    {
        var token = (await client.GetGhostAccount()).JwToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.V2.Product.CreateProduct)
        {
            Content = JsonContent.Create(new { name = "", price = 1m, barCode = "" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(culture));

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var messages = body.RootElement.GetProperty("errors").EnumerateObject()
            .SelectMany(property => property.Value.EnumerateArray().Select(message => message.GetString()!))
            .ToArray();
        messages.ShouldContain(message => message.Contains($"'{name}'"));
        messages.ShouldContain(message => message.Contains($"'{barCode}'"));
    }

    [Fact]
    public async Task ConcurrentRequests_WithDifferentCultures_DoNotLeakCulture()
    {
        var cultures = Enumerable.Range(0, 40).Select(index => index % 2 == 0 ? "en" : "pt-BR").ToArray();

        var results = await Task.WhenAll(cultures.Select(async culture => (culture, message: await NotFoundMessageAsync(culture))));

        foreach (var (culture, message) in results)
            message.ShouldStartWith(culture == "en" ? EnglishNotFound : PortugueseNotFound);
    }

    [Fact]
    public void MissingKeys_FallBackToTheKey_AndBackgroundWorkUsesTheThreadCulture()
    {
        var translator = factory.Services.GetRequiredService<ITranslator>();
        var original = CultureInfo.CurrentCulture;
        try
        {
            translator["Missing_General_Key"].ShouldBe("Missing_General_Key");
            translator.GetString("Missing_Message_Key").ShouldBe("Missing_Message_Key");
            translator.GetString(new TranslatorMessageDto("Missing_formatted_{0}", ["x"])).ShouldBe("Missing formatted x");

            // Outside a request (hosted services, consumers) nothing sets a request culture: the thread culture applies.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            translator["Name"].ShouldBe("Name");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            translator["Name"].ShouldBe("Nome");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void ValidOptions_RegisterWithoutAnIntermediateProvider_AndDeriveRequestLocalization()
    {
        var services = new ServiceCollection();
        services.AddCustomLocalization(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalizationSettings:DefaultRequestCulture"] = "en",
            ["LocalizationSettings:SupportedCultures:0"] = "en",
            ["LocalizationSettings:SupportedCultures:1"] = "pt-BR"
        }).Build());

        // Registration alone resolves nothing; options materialize only when the host asks for them.
        services.ShouldNotContain(descriptor => descriptor.ImplementationInstance is IServiceProvider);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;
        options.DefaultRequestCulture.Culture.Name.ShouldBe("en");
        options.SupportedUICultures!.Select(culture => culture.Name).ShouldBe(["en", "pt-BR"]);
    }

    [Theory]
    [InlineData("LocalizationSettings:DefaultRequestCulture", "xx-not-a-culture", "LocalizationSettings:DefaultRequestCulture")]
    [InlineData("LocalizationSettings:DefaultRequestCulture", "", "LocalizationSettings:DefaultRequestCulture")]
    [InlineData("LocalizationSettings:DefaultRequestCulture", "de-DE", "must be one of LocalizationSettings:SupportedCultures")]
    [InlineData("LocalizationSettings:SupportedCultures:2", "zz-not-a-culture", "LocalizationSettings:SupportedCultures must contain only predefined")]
    [InlineData("LocalizationSettings:SupportedCultures:2", "PT-br", "LocalizationSettings:SupportedCultures must not contain duplicates")]
    public void InvalidOptions_FailStartup_NamingTheKeyOnly(string key, string value, string expected)
    {
        using var invalid = new ConfiguredFactory(new Dictionary<string, string?> { [key] = value });

        var exception = Should.Throw<Exception>(() => invalid.CreateClient());
        var messages = Flatten(exception);

        messages.ShouldContain(expected);
        if (value.Length > 0)
            messages.ShouldNotContain(value);
    }

    [Fact]
    public void Validator_RejectsAnEmptySupportedSet()
    {
        var result = new LocalizationSettingsValidator().Validate(null, new LocalizationSettings { DefaultRequestCulture = "en", SupportedCultures = [] });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LocalizationSettings:SupportedCultures must contain at least one culture.");
    }

    private async Task<string> NotFoundMessageAsync(string? acceptLanguage)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.V2.Product.GetProductById(Guid.NewGuid()));
        if (acceptLanguage is not null)
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(acceptLanguage));
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        return await ErrorMessageAsync(response);
    }

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errors")[0].GetProperty("message").GetString()!;
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
