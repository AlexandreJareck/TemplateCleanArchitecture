using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Infrastructure.Search;
using Template.Infrastructure.Search.Persons;
using Template.Tests.Shared;

namespace Template.Integration.Tests.Search;

/// <summary>Captures every log entry (rendered message, structured values and exception) for leakage assertions.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> entries = new();

    public IReadOnlyList<string> Entries => entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(";", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
            : string.Empty;
        entries.Enqueue($"{logLevel}|{formatter(state, exception)}|{values}|{exception}");
    }
}

public static class SearchTestSupport
{
    /// <summary>Loopback port 1: nothing listens there, so connections are refused immediately.</summary>
    public const string UnreachableUrl = "http://127.0.0.1:1";

    public static ElasticsearchPersonSearchIndex CreateIndex(
        string url,
        out CapturingLogger<ElasticsearchPersonSearchIndex> logger,
        string indexName = "persons",
        TimeSpan? requestTimeout = null)
    {
        logger = new CapturingLogger<ElasticsearchPersonSearchIndex>();
        var settings = new PersonSearchSettings
        {
            Url = url,
            IndexName = indexName,
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5)
        };
        return new ElasticsearchPersonSearchIndex(Options.Create(settings), logger);
    }

    public static ElasticsearchPersonSearchIndex CreateIndex(FakeElasticsearchServer server, out CapturingLogger<ElasticsearchPersonSearchIndex> logger, string indexName = "persons", TimeSpan? requestTimeout = null) =>
        CreateIndex(server.Url.ToString().TrimEnd('/'), out logger, indexName, requestTimeout);

    public const string Shards = """{"total":2,"successful":1,"failed":0}""";

    public static string IndexCreated(string id) =>
        $$"""{"_index":"persons","_id":"{{id}}","_version":1,"result":"created","_shards":{{Shards}},"_seq_no":0,"_primary_term":1}""";

    public static string IndexUpdated(string id) =>
        $$"""{"_index":"persons","_id":"{{id}}","_version":2,"result":"updated","_shards":{{Shards}},"_seq_no":1,"_primary_term":1}""";

    public static string Found(string id, string source) =>
        $$"""{"_index":"persons","_id":"{{id}}","_version":1,"_seq_no":0,"_primary_term":1,"found":true,"_source":{{source}}}""";

    public static string NotFoundDocument(string id) => $$"""{"_index":"persons","_id":"{{id}}","found":false}""";

    public static string Deleted(string id, string result = "deleted") =>
        $$"""{"_index":"persons","_id":"{{id}}","_version":2,"result":"{{result}}","_shards":{{Shards}},"_seq_no":1,"_primary_term":1}""";

    public static string Hits(params string[] sources)
    {
        var hits = string.Join(",", sources.Select((source, index) => $$"""{"_index":"persons","_id":"{{index}}","_score":1.0,"_source":{{source}}}"""));
        return $$$"""{"took":1,"timed_out":false,"_shards":{"total":1,"successful":1,"skipped":0,"failed":0},"hits":{"total":{"value":{{{sources.Length}}},"relation":"eq"},"max_score":1.0,"hits":[{{{hits}}}]}}""";
    }

    public static string Error(string type, string reason, int status) =>
        $$"""{"error":{"root_cause":[{"type":"{{type}}","reason":"{{reason}}"}],"type":"{{type}}","reason":"{{reason}}"},"status":{{status}}}""";

    public static string BulkItem(string id, int status, string? errorReason = null) => errorReason is null
        ? $$$"""{"update":{"_index":"persons","_id":"{{{id}}}","_version":1,"result":"created","_shards":{{{Shards}}},"_seq_no":0,"_primary_term":1,"status":{{{status}}}}}"""
        : $$$"""{"update":{"_index":"persons","_id":"{{{id}}}","status":{{{status}}},"error":{"type":"document_parsing_exception","reason":"{{{errorReason}}}"}""" + "}}";

    public static string Bulk(bool errors, params string[] items) =>
        $$"""{"took":3,"errors":{{(errors ? "true" : "false")}},"items":[{{string.Join(",", items)}}]}""";
}
