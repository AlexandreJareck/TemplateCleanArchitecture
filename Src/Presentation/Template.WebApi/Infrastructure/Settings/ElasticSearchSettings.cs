﻿namespace Template.WebApi.Infrastructure.Settings;

public class ElasticSearchSettings
{
    public string Name { get; init; } = "elasticsearch";
    public string Url { get; init; } = "http://elasticsearch:9200";
    public string Type { get; init; } = "logs-api";
    public string DataSet { get; init; } = "example";
    public string Namespace { get; init; } = "demo";
}
