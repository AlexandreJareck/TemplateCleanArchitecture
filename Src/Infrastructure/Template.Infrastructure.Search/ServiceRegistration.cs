using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Search;
using Template.Infrastructure.Search.Persons;

namespace Template.Infrastructure.Search;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers the person search index. With <paramref name="useSearchEngine"/> and
    /// <c>PersonSearchSettings:Enabled</c>, operations go to Elasticsearch; otherwise they report <c>Unavailable</c>
    /// without network calls. Building the container or the adapter never contacts Elasticsearch.
    /// </summary>
    public static IServiceCollection AddSearchInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useSearchEngine)
    {
        services.AddOptions<PersonSearchSettings>()
            .Configure(settings => PersonSearchSettings.ApplyLegacyKeys(
                settings, configuration.GetSection(PersonSearchSettings.LegacySectionName)))
            .Bind(configuration.GetSection(PersonSearchSettings.SectionName))
            .Validate(PersonSearchSettings.IsValid, "PersonSearchSettings requires an absolute http(s) Url without credentials, a lowercase IndexName and a RequestTimeout between 0 and 5 minutes when Enabled.")
            .ValidateOnStart();

        if (!useSearchEngine)
        {
            services.AddSingleton<IPersonSearchIndex, DisabledPersonSearchIndex>();
            return services;
        }

        services.AddSingleton<ElasticsearchPersonSearchIndex>();
        services.AddSingleton<IPersonSearchIndex>(provider =>
            provider.GetRequiredService<IOptions<PersonSearchSettings>>().Value.Enabled
                ? provider.GetRequiredService<ElasticsearchPersonSearchIndex>()
                : new DisabledPersonSearchIndex());
        return services;
    }
}
