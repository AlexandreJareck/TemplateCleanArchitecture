using Microsoft.AspNetCore.Mvc;
using Template.WebApi.Elastic;
using Template.WebApi.Models;

namespace Template.WebApi.Controllers.v1;

[ApiVersion("1")]
public class PersonController : BaseApiController
{
    private readonly IElasticService _elasticService;

    public PersonController(IElasticService elasticService)
    {
        _elasticService = elasticService;
    }
    [HttpPost("create-index")]
    public async Task<IActionResult> CreateIndex(string indexName)
    {
        await _elasticService.CreateIndexIfNotExistsAsync(indexName);
        return Ok($"Index {indexName} created or already exists.");
    }
    [HttpPost("add-person")]
    public async Task<IActionResult> AddPerson([FromBody] Person person)
    {
        var result = await _elasticService.AddOrUpdate(person);
        return result ? Ok("Person added or updated successfully.") : StatusCode(500, "Error adding or updating person.");
    }
    [HttpPost("update-person")]
    public async Task<IActionResult> UpdatePerson([FromBody] Person person)
    {
        var result = await _elasticService.AddOrUpdate(person);
        return result ? Ok("Person added or updated successfully.") : StatusCode(500, "Error adding or updating person.");
    }
    [HttpGet("get-person/{key}")]
    public async Task<IActionResult> GetPerson(string key)
    {
        var person = await _elasticService.Get(key);
        return person != null ? Ok(person) : NotFound("Person not found.");
    }
    [HttpGet("get-all-persons")]
    public async Task<IActionResult> GetAllPersons()
    {
        var persons = await _elasticService.GetAll();
        return persons != null ? Ok(persons) : StatusCode(500, "Error retrieving persons.");
    }
    [HttpDelete("delete-person/{key}")]
    public async Task<IActionResult> DeletePerson(string key)
    {
        var result = await _elasticService.Remove(key);
        return result ? Ok("Person deleted successfully.") : StatusCode(500, "Error deleting person.");
    }
}
