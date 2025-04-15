using Template.Domain.Person;

namespace Template.Application.Services.Elastic;

public interface IElasticService
{
    Task CreateIndexIfNotExistsAsync(string indexName);
    Task<bool> AddOrUpdate(Person person);
    Task<bool> AddOrUpdateBulk(IEnumerable<Person> person, string indexName);
    Task<Person> Get(string key);
    Task<List<Person>> GetAll();
    Task<bool> Remove(string key);
    Task<long> RemoveAll();
}
