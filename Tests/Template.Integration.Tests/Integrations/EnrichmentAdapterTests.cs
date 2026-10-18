using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Template.Application.Interfaces.Integrations;
using Template.Infrastructure.Integrations;
using Template.Infrastructure.Integrations.Bcb;
using Template.Infrastructure.Integrations.BrasilApi;
using Template.Infrastructure.Integrations.Http;
using Template.Tests.Shared;

namespace Template.Integration.Tests.Integrations;

/// <summary>
/// BrasilAPI and BCB PTAX adapters against a loopback fake server (no external provider is contacted): success,
/// 404, 4xx, 429, 5xx with bounded retry, timeout, malformed and mismatching bodies, unreachable host, caller
/// cancellation, circuit breaking and log safety (INT-001). Response bodies follow the providers' documented fields.
/// </summary>
public sealed class EnrichmentAdapterTests : IAsyncLifetime
{
    private const string Cnpj = "11222333000181";
    private const string Cep = "01310100";

    private const string CnpjBody = """
        {"cnpj":"11222333000181","razao_social":"ACME COMERCIO LTDA","nome_fantasia":"ACME",
         "descricao_situacao_cadastral":"ATIVA","descricao_tipo_de_logradouro":"AVENIDA","logradouro":"PAULISTA",
         "numero":"1000","complemento":"ANDAR 5","bairro":"BELA VISTA","municipio":"SAO PAULO","uf":"SP","cep":"01310100",
         "email":"synthetic@example.invalid","ddd_telefone_1":"1100000000","qsa":[{"nome_socio":"SYNTHETIC PARTNER"}]}
        """;

    private const string CepBody = """
        {"cep":"01310100","state":"SP","city":"São Paulo","neighborhood":"Bela Vista","street":"Avenida Paulista","service":"open-cep",
         "location":{"type":"Point","coordinates":{}}}
        """;

    private const string PtaxBody = """
        {"@odata.context":"https://example.invalid/$metadata#_CotacaoMoedaPeriodo",
         "value":[
          {"cotacaoCompra":5.4300,"cotacaoVenda":5.4306,"dataHoraCotacao":"2026-10-16 10:04:30.312","tipoBoletim":"Abertura"},
          {"cotacaoCompra":5.4315,"cotacaoVenda":5.4321,"dataHoraCotacao":"2026-10-16 13:09:28.981","tipoBoletim":"Fechamento"},
          {"cotacaoCompra":5.4100,"cotacaoVenda":5.4106,"dataHoraCotacao":"2026-10-15 13:06:12.000","tipoBoletim":"Fechamento PTAX"}
         ]}
        """;

    private FakeElasticsearchServer server = null!;
    private readonly CapturingLoggerFactory loggers = new();
    private readonly ManualTimeProvider time = new();

    public async Task InitializeAsync() => server = await FakeElasticsearchServer.StartAsync();

    public async Task DisposeAsync() => await server.DisposeAsync();

    [Fact]
    public async Task Cnpj_Found_IsValidatedNormalized_AndIgnoresContactData()
    {
        server.Handler = _ => Json(200, CnpjBody);

        var result = await Registry().LookupAsync(Cnpj, CancellationToken.None);

        result.Outcome.ShouldBe(EnrichmentOutcome.Found);
        result.Provider.ShouldBe("BrasilAPI");
        result.RetrievedAtUtc.ShouldBe(time.GetUtcNow().UtcDateTime);
        var value = result.Value!;
        (value.TaxId, value.LegalName, value.TradeName, value.RegistrationStatus).ShouldBe((Cnpj, "ACME COMERCIO LTDA", "ACME", "ATIVA"));
        value.Address.ShouldBe(new PostalAddressSuggestion(Cep, "AVENIDA PAULISTA", "BELA VISTA", "SAO PAULO", "SP", "1000", "ANDAR 5"));
        server.Requests.Single().Path.ShouldBe($"/api/cnpj/v1/{Cnpj}");
        typeof(CompanyRegistrationSuggestion).GetProperties().Select(property => property.Name)
            .ShouldNotContain(name => name.Contains("Email") || name.Contains("Phone") || name.Contains("Partner"));
    }

    [Fact]
    public async Task Cep_Found_IsValidated()
    {
        server.Handler = _ => Json(200, CepBody);

        var result = await PostalCodes().LookupAsync(Cep, CancellationToken.None);

        result.Value.ShouldBe(new PostalAddressSuggestion(Cep, "Avenida Paulista", "Bela Vista", "São Paulo", "SP"));
        server.Requests.Single().Path.ShouldBe($"/api/cep/v2/{Cep}");
    }

    [Theory]
    [InlineData(404, EnrichmentOutcome.NotFound, 1)]
    [InlineData(400, EnrichmentOutcome.Rejected, 1)]
    [InlineData(429, EnrichmentOutcome.Unavailable, 1)] // the provider asked to slow down: never retried
    [InlineData(500, EnrichmentOutcome.Unavailable, 3)] // transient: 1 attempt + 2 retries
    [InlineData(503, EnrichmentOutcome.Unavailable, 3)]
    [InlineData(408, EnrichmentOutcome.Unavailable, 3)]
    public async Task StatusCodes_MapToExplicitOutcomes_WithBoundedRetries(int status, EnrichmentOutcome outcome, int expectedRequests)
    {
        server.Handler = _ => Json(status, """{"message":"synthetic provider text"}""");

        var result = await Registry().LookupAsync(Cnpj, CancellationToken.None);

        result.Outcome.ShouldBe(outcome);
        result.Value.ShouldBeNull();
        result.Reason!.ShouldNotContain("synthetic provider text");
        server.Requests.Count.ShouldBe(expectedRequests);
    }

    [Fact]
    public async Task TransientFailure_ThenSuccess_IsRetriedAndFound()
    {
        var calls = 0;
        server.Handler = _ => Interlocked.Increment(ref calls) == 1 ? Json(502, "{}") : Json(200, CnpjBody);

        var result = await Registry().LookupAsync(Cnpj, CancellationToken.None);

        result.Outcome.ShouldBe(EnrichmentOutcome.Found);
        server.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Timeout_IsUnavailable_AfterBoundedAttempts()
    {
        server.Handler = _ => new FakeResponse(200, CnpjBody, ElasticProductHeader: false, Delay: TimeSpan.FromSeconds(5));

        var started = DateTime.UtcNow;
        var result = await Registry(timeout: TimeSpan.FromMilliseconds(200), maxRetries: 1).LookupAsync(Cnpj, CancellationToken.None);

        (result.Outcome, result.Reason).ShouldBe((EnrichmentOutcome.Unavailable, "timeout"));
        server.Requests.Count.ShouldBe(2);
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(4));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"cnpj":"11222333000181"}""")] // missing razao_social
    [InlineData("""{"cnpj":"99999999000191","razao_social":"OTHER"}""")] // another company's record
    [InlineData("""{"cnpj":"11222333000181","razao_social":{"nested":true}}""")]
    public async Task MalformedOrMismatchingBodies_AreInvalid(string body)
    {
        server.Handler = _ => Json(200, body);

        var result = await Registry().LookupAsync(Cnpj, CancellationToken.None);

        (result.Outcome, result.Reason).ShouldBe((EnrichmentOutcome.Invalid, "schema-mismatch"));
    }

    [Theory]
    [InlineData("""{"cep":"01310100","state":"São Paulo","city":"São Paulo"}""")]
    [InlineData("""{"cep":"99999999","state":"SP","city":"São Paulo"}""")]
    [InlineData("""{"cep":"01310100","state":"SP"}""")]
    public async Task CepBodiesFailingValidation_AreInvalid(string body)
    {
        server.Handler = _ => Json(200, body);

        (await PostalCodes().LookupAsync(Cep, CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Invalid);
    }

    [Fact]
    public async Task UnreachableProvider_IsUnavailable()
    {
        var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/api/") };

        var result = await ((ISupplierRegistryLookup)new BrasilApiClient(client, Factory(TimeSpan.FromSeconds(5), maxRetries: 0), time, loggers.CreateLogger<BrasilApiClient>()))
            .LookupAsync(Cnpj, CancellationToken.None);

        // Windows retries SYNs to a closed loopback port for about 2 s before refusing; either way the call is bounded.
        result.Outcome.ShouldBe(EnrichmentOutcome.Unavailable);
        result.Reason.ShouldBeOneOf("unreachable", "timeout");
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated_AndDoesNotCountAsAFailure()
    {
        server.Handler = _ => new FakeResponse(200, CnpjBody, ElasticProductHeader: false, Delay: TimeSpan.FromSeconds(5));
        var factory = Factory(threshold: 1);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => Registry(factory).LookupAsync(Cnpj, cancellation.Token));

        server.Handler = _ => Json(200, CnpjBody);
        (await Registry(factory).LookupAsync(Cnpj, CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Found);
    }

    [Fact]
    public async Task CircuitOpens_AfterConsecutiveFailures_ShortCircuits_ThenHalfOpenRecovers()
    {
        server.Handler = _ => Json(500, "{}");
        var factory = Factory(threshold: 2, maxRetries: 0, breakDuration: TimeSpan.FromSeconds(30));

        (await Registry(factory).LookupAsync(Cnpj, CancellationToken.None)).Reason.ShouldBe("server-error");
        (await Registry(factory).LookupAsync(Cnpj, CancellationToken.None)).Reason.ShouldBe("server-error");
        server.ClearRequests();

        var open = await Registry(factory).LookupAsync(Cnpj, CancellationToken.None);
        (open.Outcome, open.Reason).ShouldBe((EnrichmentOutcome.Unavailable, "circuit-open"));
        server.Requests.ShouldBeEmpty();
        loggers.Entries.ShouldContain(entry => entry.Contains("ProviderCircuitOpened", StringComparison.Ordinal));

        time.Advance(TimeSpan.FromSeconds(31));
        server.Handler = _ => Json(200, CnpjBody);
        (await Registry(factory).LookupAsync(Cnpj, CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Found);
        (await Registry(factory).LookupAsync(Cnpj, CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Found);
        server.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task HalfOpenTrialFailure_ReopensTheCircuit()
    {
        server.Handler = _ => Json(503, "{}");
        var factory = Factory(threshold: 1, maxRetries: 0, breakDuration: TimeSpan.FromSeconds(10));
        await Registry(factory).LookupAsync(Cnpj, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(11));

        (await Registry(factory).LookupAsync(Cnpj, CancellationToken.None)).Reason.ShouldBe("server-error");
        (await Registry(factory).LookupAsync(Cnpj, CancellationToken.None)).Reason.ShouldBe("circuit-open");
    }

    [Fact]
    public async Task Logs_NeverContainTheCnpjCepOrProviderContent()
    {
        server.Handler = request => request.Path.Contains("cep") ? Json(500, "{}") : Json(200, """{"cnpj":"11222333000181","razao_social":null}""");

        await Registry().LookupAsync(Cnpj, CancellationToken.None);
        await PostalCodes().LookupAsync(Cep, CancellationToken.None);

        loggers.Entries.ShouldNotBeEmpty();
        loggers.Entries.ShouldAllBe(entry => !entry.Contains(Cnpj) && !entry.Contains(Cep) && !entry.Contains("127.0.0.1"));
    }

    [Fact]
    public async Task Ptax_ParsesBulletins_AndBuildsTheDocumentedODataRequest()
    {
        server.Handler = _ => Json(200, PtaxBody);

        var result = await Ptax().GetPtaxBulletinsAsync("USD", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 16), CancellationToken.None);

        result.Outcome.ShouldBe(EnrichmentOutcome.Found);
        result.Value!.Select(bulletin => (bulletin.QuoteDate, bulletin.SellRate, bulletin.IsClosing)).ShouldBe([
            (new DateOnly(2026, 10, 16), 5.4306m, false),
            (new DateOnly(2026, 10, 16), 5.4321m, true),
            (new DateOnly(2026, 10, 15), 5.4106m, true)]);
        var request = server.Requests.Single();
        request.Path.ShouldBe("/odata/CotacaoMoedaPeriodo(moeda=@moeda,dataInicial=@dataInicial,dataFinalCotacao=@dataFinalCotacao)");
        Uri.UnescapeDataString(request.Query).ShouldBe(
            "?@moeda='USD'&@dataInicial='10-09-2026'&@dataFinalCotacao='10-16-2026'&$format=json&$select=cotacaoCompra,cotacaoVenda,dataHoraCotacao,tipoBoletim");
    }

    [Theory]
    [InlineData("""{"value":{}}""")]
    [InlineData("""{"value":[{"cotacaoCompra":"5.43","cotacaoVenda":5.43,"dataHoraCotacao":"2026-10-16 13:09:28.981","tipoBoletim":"Fechamento"}]}""")]
    [InlineData("""{"value":[{"cotacaoCompra":5.43,"cotacaoVenda":-1,"dataHoraCotacao":"2026-10-16 13:09:28.981","tipoBoletim":"Fechamento"}]}""")]
    [InlineData("""{"value":[{"cotacaoCompra":5.43,"cotacaoVenda":5.43,"dataHoraCotacao":"16/10/2026","tipoBoletim":"Fechamento"}]}""")]
    [InlineData("""{"value":[{"cotacaoCompra":5.43,"cotacaoVenda":5.43,"dataHoraCotacao":"2026-11-30 13:00:00.000","tipoBoletim":"Fechamento"}]}""")] // outside the requested range
    public async Task Ptax_SchemaMismatches_AreInvalid(string body)
    {
        server.Handler = _ => Json(200, body);

        (await Ptax().GetPtaxBulletinsAsync("USD", new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 16), CancellationToken.None))
            .Outcome.ShouldBe(EnrichmentOutcome.Invalid);
    }

    [Fact]
    public async Task Ptax_EmptyWindow_IsFoundAndEmpty_AndInvalidRequestsNeverLeave()
    {
        server.Handler = _ => Json(200, """{"value":[]}""");

        (await Ptax().GetPtaxBulletinsAsync("EUR", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), CancellationToken.None)).Value.ShouldBeEmpty();
        (await Ptax().GetPtaxBulletinsAsync("eur", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 12), CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Rejected);
        (await Ptax().GetPtaxBulletinsAsync("EUR", new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 10), CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Rejected);
        server.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Registration_DisabledOrTest_AnswersUnavailableWithoutNetwork()
    {
        var enabled = new Dictionary<string, string?>
        {
            ["BrasilApiSettings:Enabled"] = "false",
            ["BcbPtaxSettings:Enabled"] = "false"
        };
        foreach (var useExternal in new[] { true, false })
        {
            await using var provider = Provider(enabled, useExternal);
            var registry = provider.GetRequiredService<ISupplierRegistryLookup>();
            var rates = provider.GetRequiredService<IExchangeRateProvider>();

            (await registry.LookupAsync(Cnpj, CancellationToken.None)).Reason.ShouldBe("disabled");
            (await rates.GetPtaxBulletinsAsync("USD", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), CancellationToken.None)).Reason.ShouldBe("disabled");
        }

        server.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Registration_Enabled_UsesTheTypedClients()
    {
        server.Handler = _ => Json(200, CepBody);
        await using var provider = Provider(new Dictionary<string, string?>
        {
            ["BrasilApiSettings:Enabled"] = "true",
            ["BrasilApiSettings:BaseUrl"] = $"{server.Url}api/"
        }, useExternal: true);

        (await provider.GetRequiredService<IPostalCodeLookup>().LookupAsync(Cep, CancellationToken.None)).Outcome.ShouldBe(EnrichmentOutcome.Found);
        (await provider.GetRequiredService<IExchangeRateProvider>().GetPtaxBulletinsAsync("USD", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), CancellationToken.None))
            .Reason.ShouldBe("disabled");
    }

    [Theory]
    [InlineData("http://brasilapi.example/api/", "00:00:05", "2")] // http only on loopback
    [InlineData("https://brasilapi.example/api", "00:00:05", "2")] // must end with '/'
    [InlineData("https://user:pass@brasilapi.example/api/", "00:00:05", "2")]
    [InlineData("https://brasilapi.example/api/", "00:01:00", "2")] // timeout above 30 s
    [InlineData("https://brasilapi.example/api/", "00:00:05", "3")] // more than 2 retries
    public async Task EnabledSettings_AreValidatedAtStartup(string baseUrl, string timeout, string retries)
    {
        await using var provider = Provider(new Dictionary<string, string?>
        {
            ["BrasilApiSettings:Enabled"] = "true",
            ["BrasilApiSettings:BaseUrl"] = baseUrl,
            ["BrasilApiSettings:Timeout"] = timeout,
            ["BrasilApiSettings:MaxRetries"] = retries
        }, useExternal: true);

        var error = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BrasilApiSettings>>().Value);
        error.Message.ShouldContain("BrasilApiSettings");
        error.Message.ShouldNotContain("pass");
    }

    private ISupplierRegistryLookup Registry(ProviderHttpExecutorFactory? factory = null, TimeSpan? timeout = null, int maxRetries = 2) =>
        new BrasilApiClient(BrasilApiHttpClient(), factory ?? Factory(timeout, maxRetries), time, loggers.CreateLogger<BrasilApiClient>());

    private IPostalCodeLookup PostalCodes() =>
        new BrasilApiClient(BrasilApiHttpClient(), Factory(), time, loggers.CreateLogger<BrasilApiClient>());

    private IExchangeRateProvider Ptax() =>
        new BcbPtaxClient(new HttpClient { BaseAddress = new Uri($"{server.Url}odata/") }, Factory(), time, loggers.CreateLogger<BcbPtaxClient>());

    private HttpClient BrasilApiHttpClient() => new() { BaseAddress = new Uri($"{server.Url}api/") };

    private ProviderHttpExecutorFactory Factory(
        TimeSpan? timeout = null, int maxRetries = 2, int threshold = 5, TimeSpan? breakDuration = null, TimeSpan? retryDelay = null)
    {
        var brasilApi = new BrasilApiSettings
        {
            Enabled = true,
            BaseUrl = $"{server.Url}api/",
            Timeout = timeout ?? TimeSpan.FromSeconds(2),
            MaxRetries = maxRetries,
            RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(10),
            CircuitBreakerFailureThreshold = threshold,
            CircuitBreakerBreakDuration = breakDuration ?? TimeSpan.FromSeconds(30)
        };
        var ptax = new BcbPtaxSettings { Enabled = true, BaseUrl = $"{server.Url}odata/", MaxRetries = 0 };
        return new ProviderHttpExecutorFactory(Options.Create(brasilApi), Options.Create(ptax), time, loggers);
    }

    private static ServiceProvider Provider(IDictionary<string, string?> values, bool useExternal)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegrationsInfrastructure(configuration, useExternal);
        return services.BuildServiceProvider();
    }

    private static FakeResponse Json(int status, string body) => new(status, body, ElasticProductHeader: false);

    /// <summary>Clock for the circuit breaker; delays still use real timers.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentQueue<string> entries = new();

        public IReadOnlyList<string> Entries => entries.ToArray();

        public ILogger<T> CreateLogger<T>() => new Logger<T>(this);

        public ILogger CreateLogger(string categoryName) => new Capturing(entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Capturing(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(";", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
                    : string.Empty;
                entries.Enqueue($"{eventId.Name}|{formatter(state, exception)}|{values}|{exception}");
            }
        }
    }
}
