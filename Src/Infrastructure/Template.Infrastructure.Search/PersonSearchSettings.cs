using Microsoft.Extensions.Configuration;

namespace Template.Infrastructure.Search;

/// <summary>
/// Options of the person search index, bound from the <c>PersonSearchSettings</c> section. For compatibility the
/// endpoint and index name fall back to the legacy keys <c>ElasticSearchSettings:Url</c> and
/// <c>ElasticSearchSettings:DefaultIndex</c>: a key in <c>PersonSearchSettings</c> wins over its legacy key, which
/// wins over the default below. Logging and health checks have their own options and never read these.
/// </summary>
public sealed class PersonSearchSettings
{
    public const string SectionName = nameof(PersonSearchSettings);

    public const string LegacySectionName = "ElasticSearchSettings";

    /// <summary>When false, every operation reports <c>Unavailable</c> without contacting Elasticsearch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Elasticsearch endpoint (http or https). Credentials must not be embedded in it.</summary>
    public string Url { get; set; } = "http://elasticsearch:9200";

    /// <summary>Index holding the person documents.</summary>
    public string IndexName { get; set; } = "persons";

    /// <summary>Upper bound of one request, including connecting.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    internal static void ApplyLegacyKeys(PersonSearchSettings settings, IConfiguration legacySection)
    {
        if (!string.IsNullOrWhiteSpace(legacySection["Url"]))
            settings.Url = legacySection["Url"]!;
        if (!string.IsNullOrWhiteSpace(legacySection["DefaultIndex"]))
            settings.IndexName = legacySection["DefaultIndex"]!;
    }

    public static bool IsValid(PersonSearchSettings settings) =>
        !settings.Enabled
        || (Uri.TryCreate(settings.Url, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(url.UserInfo)
            && !string.IsNullOrWhiteSpace(settings.IndexName)
            && settings.IndexName == settings.IndexName.ToLowerInvariant()
            && settings.RequestTimeout > TimeSpan.Zero
            && settings.RequestTimeout <= TimeSpan.FromMinutes(5));
}
