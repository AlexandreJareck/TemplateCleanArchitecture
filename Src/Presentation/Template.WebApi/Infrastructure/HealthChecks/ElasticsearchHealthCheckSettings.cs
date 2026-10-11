using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Template.WebApi.Infrastructure.HealthChecks;

/// <summary>
/// Options of the Elasticsearch health check, bound from <c>ElasticsearchHealthCheckSettings</c>. The endpoint and
/// check name fall back to the legacy keys <c>ElasticSearchSettings:Url</c> and <c>ElasticSearchSettings:Name</c>;
/// a key in the new section wins over its legacy key, which wins over the default below. Only the physical endpoint
/// is shared with search and logging; their options are separate.
/// </summary>
public sealed class ElasticsearchHealthCheckSettings
{
    public const string SectionName = nameof(ElasticsearchHealthCheckSettings);

    private const string LegacySectionName = "ElasticSearchSettings";

    /// <summary>When false, no Elasticsearch entry is registered under <c>/health</c>.</summary>
    public bool Enabled { get; set; } = true;

    public string Url { get; set; } = "http://elasticsearch:9200";

    /// <summary>Entry name under <c>/health</c>.</summary>
    public string Name { get; set; } = "elasticsearch";

    /// <summary>Upper bound of one check.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Status reported when Elasticsearch is unreachable, answers with an error, or times out.</summary>
    public HealthStatus FailureStatus { get; set; } = HealthStatus.Unhealthy;

    public static ElasticsearchHealthCheckSettings From(IConfiguration configuration)
    {
        var settings = new ElasticsearchHealthCheckSettings();
        var legacy = configuration.GetSection(LegacySectionName);
        if (!string.IsNullOrWhiteSpace(legacy["Url"]))
            settings.Url = legacy["Url"]!;
        if (!string.IsNullOrWhiteSpace(legacy["Name"]))
            settings.Name = legacy["Name"]!;
        configuration.GetSection(SectionName).Bind(settings);
        return settings;
    }

    public bool TryGetEndpoint(out Uri endpoint) =>
        Uri.TryCreate(Url, UriKind.Absolute, out endpoint!)
        && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(endpoint.UserInfo);

    public bool IsValid() =>
        !Enabled
        || (TryGetEndpoint(out _)
            && !string.IsNullOrWhiteSpace(Name)
            && Timeout > TimeSpan.Zero
            && Timeout <= TimeSpan.FromMinutes(1)
            && FailureStatus != HealthStatus.Healthy);
}
