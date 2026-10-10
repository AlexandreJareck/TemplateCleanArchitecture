namespace Template.WebApi.Infrastructure.Logging;

/// <summary>
/// Options of the Serilog Elasticsearch sink, bound from <c>ElasticLoggingSettings</c>. The endpoint and data-stream
/// parts fall back to the legacy keys <c>ElasticSearchSettings:Url</c>, <c>Type</c>, <c>DataSet</c> and
/// <c>Namespace</c>; a key in the new section wins over its legacy key, which wins over the default below. The sink
/// is independent of the person search index: neither reads the other's options.
/// </summary>
public sealed class ElasticLoggingSettings
{
    public const string SectionName = nameof(ElasticLoggingSettings);

    private const string LegacySectionName = "ElasticSearchSettings";

    /// <summary>When false, no Elasticsearch sink is added by code; console logging is unaffected.</summary>
    public bool Enabled { get; set; } = true;

    public string Url { get; set; } = "http://elasticsearch:9200";

    /// <summary>Data stream type, dataset and namespace; the defaults produce <c>logs-api-example-demo</c>.</summary>
    public string Type { get; set; } = "logs-api";

    public string DataSet { get; set; } = "example";

    public string Namespace { get; set; } = "demo";

    /// <summary>
    /// Upper bound of one export or bootstrap request. The silent bootstrap runs while the logger is built, so an
    /// endpoint that never answers delays startup by roughly a ping plus this timeout before the sink gives up.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public static ElasticLoggingSettings From(IConfiguration configuration)
    {
        var settings = new ElasticLoggingSettings();
        var legacy = configuration.GetSection(LegacySectionName);
        if (!string.IsNullOrWhiteSpace(legacy["Url"]))
            settings.Url = legacy["Url"]!;
        if (!string.IsNullOrWhiteSpace(legacy["Type"]))
            settings.Type = legacy["Type"]!;
        if (!string.IsNullOrWhiteSpace(legacy["DataSet"]))
            settings.DataSet = legacy["DataSet"]!;
        if (!string.IsNullOrWhiteSpace(legacy["Namespace"]))
            settings.Namespace = legacy["Namespace"]!;
        configuration.GetSection(SectionName).Bind(settings);
        return settings;
    }

    public bool TryGetEndpoint(out Uri endpoint) =>
        Uri.TryCreate(Url, UriKind.Absolute, out endpoint!)
        && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(endpoint.UserInfo);
}
