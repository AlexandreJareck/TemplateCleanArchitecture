using Shouldly;
using StackExchange.Redis;

namespace Template.Redis.Integration.Tests;

/// <summary>
/// No repository consumer uses Redis Search or Time Series (Phase 15 inventory); these tests only prove that the
/// Redis 8 target integrates both capabilities, using isolated synthetic keys under the p15: namespace.
/// </summary>
public sealed class SearchAndTimeSeriesTests(RedisServer server) : IClassFixture<RedisServer>
{
    [Fact]
    public async Task Server_ReportsIntegratedSearchAndTimeSeriesModules()
    {
        await using var admin = await server.AdminAsync();
        var modules = ((RedisResult[])(await admin.GetServer(server.Endpoint).ExecuteAsync("MODULE", "LIST"))!)
            .Select(m => ((RedisResult[])m!)[1].ToString())
            .ToArray();
        var version = (await admin.GetServer(server.Endpoint).InfoAsync("server")).SelectMany(g => g).First(p => p.Key == "redis_version").Value;

        version.ShouldBe("8.10.2");
        modules.ShouldContain("search");
        modules.ShouldContain("timeseries");
        modules.ShouldContain("ReJSON");
        modules.ShouldContain("bf");
    }

    [Fact]
    public async Task Search_IndexesHashes_SortsAndLimits_AndDropsExpiredDocuments()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var index = $"idx:p15:product:{suffix}";
        var prefix = $"p15:search:{suffix}:";
        await using var admin = await server.AdminAsync();
        var db = admin.GetDatabase();
        await db.ExecuteAsync("FT.CREATE", index, "ON", "HASH", "PREFIX", "1", prefix, "SCHEMA", "name", "TEXT", "SORTABLE", "price", "NUMERIC", "SORTABLE", "category", "TAG");
        for (var i = 1; i <= 5; i++)
            await db.HashSetAsync($"{prefix}{i}", [new("name", $"synthetic widget {i}"), new("price", i * 10), new("category", i % 2 == 0 ? "even" : "odd")]);
        await db.KeyExpireAsync($"{prefix}5", TimeSpan.FromMilliseconds(800));

        (await AuthenticationAndAclTests.WaitForSearchTotalAsync(db, index, "widget", 5)).ShouldBe(5);
        var page = (RedisResult[])(await db.ExecuteAsync("FT.SEARCH", index, "@category:{odd}", "SORTBY", "price", "DESC", "LIMIT", "0", "2", "RETURN", "1", "price"))!;
        ((long)page[0]).ShouldBe(3);
        page[1].ToString().ShouldBe($"{prefix}5");
        page[3].ToString().ShouldBe($"{prefix}3");

        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        (await AuthenticationAndAclTests.WaitForSearchTotalAsync(db, index, "widget", 4)).ShouldBe(4);
        var range = (RedisResult[])(await db.ExecuteAsync("FT.SEARCH", index, "@price:[15 35]", "NOCONTENT", "SORTBY", "price", "ASC"))!;
        range.Skip(1).Select(r => r.ToString()).ShouldBe([$"{prefix}2", $"{prefix}3"]);
    }

    [Fact]
    public async Task TimeSeries_KeepsLabelsRetentionDuplicatePolicy_RangesAndCompactionRules()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var raw = $"p15:ts:{suffix}:raw";
        var avg = $"p15:ts:{suffix}:avg";
        await using var admin = await server.AdminAsync();
        var db = admin.GetDatabase();
        await db.ExecuteAsync("TS.CREATE", raw, "RETENTION", "86400000", "DUPLICATE_POLICY", "LAST", "LABELS", "sensor", suffix, "kind", "raw");
        await db.ExecuteAsync("TS.CREATE", avg, "LABELS", "sensor", suffix, "kind", "avg");
        await db.ExecuteAsync("TS.CREATERULE", raw, avg, "AGGREGATION", "avg", "60000");
        var start = 1_760_000_000_000L - 1_760_000_000_000L % 60000;
        await db.ExecuteAsync("TS.ADD", raw, start, "10");
        await db.ExecuteAsync("TS.ADD", raw, start + 1000, "20");
        await db.ExecuteAsync("TS.ADD", raw, start + 1000, "30"); // DUPLICATE_POLICY LAST overwrites
        await db.ExecuteAsync("TS.ADD", raw, start + 60000, "50"); // closes the first compaction bucket

        var samples = (RedisResult[])(await db.ExecuteAsync("TS.RANGE", raw, "-", "+"))!;
        samples.Select(s => ((RedisResult[])s!)[1].ToString()).ShouldBe(["10", "30", "50"]);
        var compacted = (RedisResult[])(await db.ExecuteAsync("TS.RANGE", avg, "-", "+"))!;
        compacted.Length.ShouldBe(1);
        double.Parse(((RedisResult[])compacted[0]!)[1].ToString()!, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(20);
        var info = ToMap((RedisResult[])(await db.ExecuteAsync("TS.INFO", raw))!);
        ((long)info["retentionTime"]).ShouldBe(86400000);
        info["duplicatePolicy"].ToString()!.ToLowerInvariant().ShouldBe("last");
        ((RedisResult[])info["rules"]!).Length.ShouldBe(1);
        var filtered = (RedisResult[])(await db.ExecuteAsync("TS.MRANGE", "-", "+", "FILTER", $"sensor={suffix}", "kind=raw"))!;
        filtered.Length.ShouldBe(1);
    }

    internal static Dictionary<string, RedisResult> ToMap(RedisResult[] pairs)
    {
        var map = new Dictionary<string, RedisResult>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < pairs.Length; i += 2) map[pairs[i].ToString()!] = pairs[i + 1];
        return map;
    }
}
