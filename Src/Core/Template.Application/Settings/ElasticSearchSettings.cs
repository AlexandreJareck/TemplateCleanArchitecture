namespace Template.Application.Settings
{
    public class ElasticSearchSettings
    {
        public string Name { get; init; } = "elasticsearch";
        public string Url { get; init; } = "http://elasticsearch:9200";
        public string DefaultIndex { get; init; } = "persons";
        public string Type { get; init; } = "logs-api";
        public string DataSet { get; init; } = "example";
        public string Namespace { get; init; } = "demo";
    }
}
