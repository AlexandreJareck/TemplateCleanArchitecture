using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Options;
using Template.Application.Settings;
using Template.Domain.Person;

namespace Template.Application.Services.Elastic
{
    public class ElasticService : IElasticService
    {
        private readonly ElasticsearchClient _client;
        private readonly ElasticSearchSettings _elasticSettings;

        public ElasticService(IOptions<ElasticSearchSettings> options)
        {
            _elasticSettings = options.Value;
            var settings = new ElasticsearchClientSettings(new Uri(_elasticSettings.Url))
                // .Authentication(new BasicAuthentication(_elasticSettings.Username, _elasticSettings.Password))
                .DefaultIndex(_elasticSettings.DefaultIndex);
            _client = new ElasticsearchClient(settings);
        }

        public async Task CreateIndexIfNotExistsAsync(string indexName)
        {
            var existsResponse = await _client.Indices.ExistsAsync(indexName);
            if (!existsResponse.Exists)
            {
                await _client.Indices.CreateAsync(indexName);
            }
        }
        public async Task<bool> AddOrUpdate(Person user)
        {
            var response = await _client.IndexAsync(user, idx => idx
                .Index(_elasticSettings.DefaultIndex)
                .Id(user.Id)
                .Refresh(Refresh.WaitFor));
            return response.IsValidResponse;
        }
        public async Task<bool> AddOrUpdateBulk(IEnumerable<Person> persons, string indexName)
        {
            var response = await _client.BulkAsync(b => b
                .Index(_elasticSettings.DefaultIndex)
                .UpdateMany(persons, (ud, u) => ud.Doc(u).DocAsUpsert(true)));
            return response.IsValidResponse;
        }
        public async Task<Person> Get(string key)
        {
            var response = await _client.GetAsync<Person>(key,
                g => g.Index(_elasticSettings.DefaultIndex));
            return response.Source;
        }
        public async Task<List<Person>> GetAll()
        {
            var response = await _client.SearchAsync<Person>(s => s
                .Index(_elasticSettings.DefaultIndex));
            return response.IsValidResponse ? response.Documents.ToList() : default;
        }
        public async Task<bool> Remove(string key)
        {
            var response = await _client.DeleteAsync<Person>(key,
                d => d.Index(_elasticSettings.DefaultIndex));
            return response.IsValidResponse;
        }
        public async Task<long> RemoveAll()
        {
            var response = await _client.DeleteByQueryAsync<Person>(_elasticSettings.DefaultIndex, d => d
                    .Query(q => q.MatchAll(new MatchAllQuery())));

            return response.IsValidResponse && response.Deleted.HasValue ? response.Deleted.Value : default;
        }
    }
}
