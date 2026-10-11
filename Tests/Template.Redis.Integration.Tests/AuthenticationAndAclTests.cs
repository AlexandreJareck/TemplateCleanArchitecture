using Shouldly;
using StackExchange.Redis;
using Template.Infrastructure.Caching;
using Template.Infrastructure.Caching.Products;

namespace Template.Redis.Integration.Tests;

/// <summary>Authentication and least-privilege ACL rules for the cache, Search and Time Series on Redis 8.</summary>
public sealed class AuthenticationAndAclTests(RedisServer server) : IClassFixture<RedisServer>
{
    // Least privilege for the product cache: its key namespace only, data and connection commands, no module,
    // administrative or dangerous commands. StackExchange.Redis also subscribes to its own topology channel.
    internal static readonly string[] CacheUserRules =
    [
        "on", "~template:product:*", "&__Booksleeve_MasterChanged", "+@connection", "+@read", "+@write", "+@keyspace",
        "+@hash", "+@transaction", "+@scripting", "+info", "+subscribe", "+unsubscribe", "+client",
        "-@dangerous", "-@admin", "-@search", "-@timeseries", "-@json"
    ];

    [Fact]
    public async Task WrongPassword_IsMiss_WithoutLeakingSecrets()
    {
        var wrongPassword = RedisImage.NewSecret();
        var logger = new CapturingLogger<DistributedProductCache>();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(password: wrongPassword), new CacheSettings { OperationTimeout = TimeSpan.FromSeconds(3) }, logger);

        (await cache.GetAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        await cache.SetAsync(CacheFactory.Product(Guid.NewGuid()), CancellationToken.None);

        logger.Messages.ShouldNotBeEmpty();
        logger.Messages.ShouldAllBe(m => !m.Contains(wrongPassword) && !m.Contains(server.Password));
    }

    [Fact]
    public async Task UnauthenticatedCommands_AreRejected()
    {
        await using var anonymous = await ConnectionMultiplexer.ConnectAsync($"{server.Endpoint},abortConnect=false,connectTimeout=2000,syncTimeout=2000,asyncTimeout=2000");

        var error = await Should.ThrowAsync<RedisException>(() => anonymous.GetDatabase().StringGetAsync("template:product:v1:any"));
        error.Message.ShouldNotContain(server.Password);
    }

    [Fact]
    public async Task LeastPrivilegeCacheUser_RunsTheCache_AndIsDeniedEverythingElse()
    {
        var user = $"p15-cache-{Guid.NewGuid():N}";
        var password = RedisImage.NewSecret();
        await using var admin = await server.AdminAsync();
        await admin.GetServer(server.Endpoint).ExecuteAsync("ACL", ["SETUSER", user, "reset", $">{password}", .. CacheUserRules]);

        var id = Guid.NewGuid();
        var logger = new CapturingLogger<DistributedProductCache>();
        var (cache, _) = CacheFactory.Create(server.ConnectionString(user, password), new CacheSettings { OperationTimeout = TimeSpan.FromSeconds(3) }, logger);
        await cache.SetAsync(CacheFactory.Product(id), CancellationToken.None);
        (await cache.GetAsync(id, CancellationToken.None)).ShouldNotBeNull(string.Join(Environment.NewLine, logger.Messages));
        await cache.RemoveAsync(id, CancellationToken.None);
        (await cache.GetAsync(id, CancellationToken.None)).ShouldBeNull();
        logger.Messages.ShouldBeEmpty();

        await using var restricted = await ConnectionMultiplexer.ConnectAsync(server.ConnectionString(user, password));
        var db = restricted.GetDatabase();
        await AssertDenied(db, "SET", "other:namespace", "x");
        await AssertDenied(db, "FLUSHALL");
        await AssertDenied(db, "CONFIG", "SET", "maxmemory-policy", "allkeys-lru");
        await AssertDenied(db, "KEYS", "*");
        await AssertDenied(db, "FT.CREATE", "idx:denied", "ON", "HASH", "PREFIX", "1", "template:product:", "SCHEMA", "name", "TEXT");
        await AssertDenied(db, "TS.CREATE", "template:product:ts");
        await AssertDenied(db, "JSON.SET", "template:product:json", "$", "{}");
    }

    [Fact]
    public async Task Redis8AclCategories_CoverModuleCommands()
    {
        await using var admin = await server.AdminAsync();
        var adminServer = admin.GetServer(server.Endpoint);

        string[] Names(RedisResult result) => ((RedisResult[])result!).Select(r => r.ToString()!.ToLowerInvariant()).ToArray();

        var categories = Names(await adminServer.ExecuteAsync("ACL", "CAT"));
        categories.ShouldContain("search");
        categories.ShouldContain("timeseries");
        categories.ShouldContain("json");
        var search = Names(await adminServer.ExecuteAsync("ACL", "CAT", "search"));
        search.ShouldContain("ft.search");
        search.ShouldContain("ft.create");
        var timeseries = Names(await adminServer.ExecuteAsync("ACL", "CAT", "timeseries"));
        timeseries.ShouldContain("ts.add");
        timeseries.ShouldContain("ts.range");
    }

    [Fact]
    public async Task SearchReader_CanQuery_ButNotWriteSeriesOrDropIndexes()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var index = $"idx:p15:acl:{suffix}";
        var prefix = $"p15:acl:{suffix}:";
        await using var admin = await server.AdminAsync();
        var adminDb = admin.GetDatabase();
        await adminDb.ExecuteAsync("FT.CREATE", index, "ON", "HASH", "PREFIX", "1", prefix, "SCHEMA", "name", "TEXT");
        await adminDb.HashSetAsync($"{prefix}1", "name", "alpha");
        await adminDb.ExecuteAsync("TS.CREATE", $"{prefix}series");
        var user = $"p15-search-{suffix}";
        var password = RedisImage.NewSecret();
        await admin.GetServer(server.Endpoint).ExecuteAsync("ACL", "SETUSER", user, "reset", $">{password}", "on", $"~{prefix}*", "+@connection", "+@search", "+@timeseries", "+@read", "+info", "-@write", "-ft.dropindex", "-ft.create", "-ft.alter");

        await using var reader = await ConnectionMultiplexer.ConnectAsync(server.ConnectionString(user, password));
        var db = reader.GetDatabase();
        (await WaitForSearchTotalAsync(db, index, "@name:alpha", 1)).ShouldBe(1);
        ((RedisResult[])(await db.ExecuteAsync("TS.RANGE", $"{prefix}series", "-", "+"))!).Length.ShouldBe(0);
        await AssertDenied(db, "TS.ADD", $"{prefix}series", "*", "1");
        await AssertDenied(db, "FT.DROPINDEX", index);
        await AssertDenied(db, "HSET", $"{prefix}2", "name", "beta");
    }

    internal static async Task<long> WaitForSearchTotalAsync(IDatabase db, string index, string query, long expected)
    {
        long total = -1;
        for (var attempt = 0; attempt < 50 && total != expected; attempt++)
        {
            total = (long)((RedisResult[])(await db.ExecuteAsync("FT.SEARCH", index, query, "NOCONTENT"))!)[0];
            if (total != expected) await Task.Delay(100);
        }

        return total;
    }

    private static async Task AssertDenied(IDatabase db, string command, params object[] arguments)
    {
        var error = await Should.ThrowAsync<RedisServerException>(() => db.ExecuteAsync(command, arguments));
        error.Message.ShouldStartWith("NOPERM", Case.Insensitive, $"{command} should be denied by the ACL");
    }
}
