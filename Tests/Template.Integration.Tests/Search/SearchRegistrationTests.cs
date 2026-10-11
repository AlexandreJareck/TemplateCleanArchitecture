using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Interfaces.Search;
using Template.Application.Search.Persons;
using Template.Infrastructure.Search;
using Template.Infrastructure.Search.Persons;
using Template.Tests.Shared;

namespace Template.Integration.Tests.Search;

/// <summary>Composition, options precedence and validation, and the disabled (rollback) path of the search adapter.</summary>
public sealed class SearchRegistrationTests
{
    private static ServiceProvider BuildProvider(bool useSearchEngine, Dictionary<string, string?> configuration)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSearchInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(), useSearchEngine);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public async Task WithSearchEngine_ResolvesTheElasticsearchAdapter_WithoutContactingIt()
    {
        await using var server = await FakeElasticsearchServer.StartAsync();
        await using var provider = BuildProvider(true, new() { ["ElasticSearchSettings:Url"] = server.Url.ToString() });

        provider.GetRequiredService<IPersonSearchIndex>().ShouldBeOfType<ElasticsearchPersonSearchIndex>();
        provider.GetRequiredService<IPersonSearchIndex>().ShouldBeSameAs(provider.GetRequiredService<IPersonSearchIndex>());
        server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutSearchEngine_ResolvesTheDisabledIndex_ThatReportsUnavailable()
    {
        await using var provider = BuildProvider(false, new());

        var index = provider.GetRequiredService<IPersonSearchIndex>();

        index.ShouldBeOfType<DisabledPersonSearchIndex>();
        (await index.GetAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.UpsertAsync(new IndexedPerson(1, "A", null), CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.RemoveAsync("1", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        (await index.EnsureIndexAsync("people", CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        var bulk = await index.UpsertManyAsync([new IndexedPerson(1, "A", null)], CancellationToken.None);
        bulk.IsComplete.ShouldBeFalse();
        bulk.FailedIds.ShouldBe([1]);
    }

    [Fact]
    public async Task DisabledIndex_PropagatesCancellation()
    {
        var index = new DisabledPersonSearchIndex();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => index.GetAsync("1", cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => index.ListAsync(cancellation.Token));
    }

    [Fact]
    public async Task EnabledFalse_IsTheRollbackSwitch_EvenWithASearchEngine()
    {
        await using var server = await FakeElasticsearchServer.StartAsync();
        await using var provider = BuildProvider(true, new()
        {
            ["PersonSearchSettings:Enabled"] = "false",
            ["ElasticSearchSettings:Url"] = server.Url.ToString()
        });

        var index = provider.GetRequiredService<IPersonSearchIndex>();

        index.ShouldBeOfType<DisabledPersonSearchIndex>();
        (await index.ListAsync(CancellationToken.None)).Status.ShouldBe(PersonIndexStatus.Unavailable);
        server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LegacyKeys_AreUsed_WhenTheNewSectionIsAbsent()
    {
        await using var provider = BuildProvider(true, new()
        {
            ["ElasticSearchSettings:Url"] = "http://legacy-host:9200",
            ["ElasticSearchSettings:DefaultIndex"] = "legacy-persons",
            ["ElasticSearchSettings:Type"] = "logs-api"
        });

        var settings = provider.GetRequiredService<IOptions<PersonSearchSettings>>().Value;

        settings.Url.ShouldBe("http://legacy-host:9200");
        settings.IndexName.ShouldBe("legacy-persons");
    }

    [Fact]
    public async Task NewSection_WinsOverLegacyKeys_AndDefaultsApplyLast()
    {
        await using var provider = BuildProvider(true, new()
        {
            ["ElasticSearchSettings:Url"] = "http://legacy-host:9200",
            ["ElasticSearchSettings:DefaultIndex"] = "legacy-persons",
            ["PersonSearchSettings:Url"] = "http://search-host:9200",
            ["PersonSearchSettings:RequestTimeout"] = "00:00:03"
        });

        var settings = provider.GetRequiredService<IOptions<PersonSearchSettings>>().Value;

        settings.Url.ShouldBe("http://search-host:9200");
        settings.IndexName.ShouldBe("legacy-persons");
        settings.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(3));
        settings.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Defaults_MatchThePreviousImplementation()
    {
        await using var provider = BuildProvider(true, new());

        var settings = provider.GetRequiredService<IOptions<PersonSearchSettings>>().Value;

        settings.Url.ShouldBe("http://elasticsearch:9200");
        settings.IndexName.ShouldBe("persons");
        settings.RequestTimeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("Url", "not-a-url")]
    [InlineData("Url", "ftp://host:9200")]
    [InlineData("Url", "http://user:secret@host:9200")]
    [InlineData("IndexName", "Persons")]
    [InlineData("IndexName", " ")]
    [InlineData("RequestTimeout", "00:00:00")]
    [InlineData("RequestTimeout", "01:00:00")]
    public async Task InvalidSettings_FailValidation(string key, string value)
    {
        await using var provider = BuildProvider(true, new() { [$"PersonSearchSettings:{key}"] = value });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PersonSearchSettings>>().Value);
    }

    [Fact]
    public async Task InvalidSettings_AreIgnored_WhenSearchIsDisabled()
    {
        await using var provider = BuildProvider(true, new()
        {
            ["PersonSearchSettings:Enabled"] = "false",
            ["PersonSearchSettings:Url"] = "not-a-url"
        });

        provider.GetRequiredService<IPersonSearchIndex>().ShouldBeOfType<DisabledPersonSearchIndex>();
    }

    [Fact]
    public void AdapterConstruction_LogsTheEndpointWithoutPathOrQuery()
    {
        using var index = SearchTestSupport.CreateIndex("http://127.0.0.1:1/some/path?token=abc", out var logger);

        logger.Entries.ShouldContain(entry => entry.Contains("http://127.0.0.1:1"));
        logger.Entries.ShouldAllBe(entry => !entry.Contains("token=abc") && !entry.Contains("some/path"));
    }
}
