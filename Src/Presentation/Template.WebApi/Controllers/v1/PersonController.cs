using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Features.Persons.Commands;
using Template.Application.Features.Persons.Queries;
using Template.Application.Search.Persons;
using Template.Domain.Person;

namespace Template.WebApi.Controllers.v1;

[ApiVersion("1", Deprecated = true)]
public class PersonController(ISender sender) : BaseApiController
{
    // The domain Person stays as the v1 request body and response item so the published OpenAPI schema and JSON are
    // unchanged. Search failures keep the v1 "500 + text" convention; a missing person is 404 only on get-person.
    [HttpPost("create-index")]
    public async Task<IActionResult> CreateIndex([FromQuery] string indexName, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new EnsurePersonIndexCommand(indexName), cancellationToken);
        return result.Succeeded ? Ok($"Index {indexName} created or already exists.") : StatusCode(500, "Error creating index.");
    }

    [HttpPost("add-person")]
    public Task<IActionResult> AddPerson([FromBody] Person person, CancellationToken cancellationToken) =>
        IndexPerson(person, cancellationToken);

    [HttpPost("update-person")]
    public Task<IActionResult> UpdatePerson([FromBody] Person person, CancellationToken cancellationToken) =>
        IndexPerson(person, cancellationToken);

    [HttpGet("get-person/{key}")]
    public async Task<IActionResult> GetPerson([FromRoute] string key, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPersonQuery(key), cancellationToken);
        return result.Status switch
        {
            PersonIndexStatus.Succeeded => Ok(ToPerson(result.Value!)),
            PersonIndexStatus.NotFound => NotFound("Person not found."),
            _ => StatusCode(500, "Error retrieving person.")
        };
    }

    [HttpGet("get-all-persons")]
    public async Task<IActionResult> GetAllPersons(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListPersonsQuery(), cancellationToken);
        return result.Succeeded ? Ok(result.Value!.Select(ToPerson).ToList()) : StatusCode(500, "Error retrieving persons.");
    }

    [HttpDelete("delete-person/{key}")]
    public async Task<IActionResult> DeletePerson([FromRoute] string key, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RemovePersonCommand(key), cancellationToken);
        return result.Succeeded ? Ok("Person deleted successfully.") : StatusCode(500, "Error deleting person.");
    }

    private async Task<IActionResult> IndexPerson(Person person, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new IndexPersonCommand(person.Id, person.FirstName, person.LastName), cancellationToken);
        return result.Succeeded ? Ok("Person added or updated successfully.") : StatusCode(500, "Error adding or updating person.");
    }

    private static Person ToPerson(IndexedPerson person) => new()
    {
        Id = person.Id,
        FirstName = person.FirstName,
        LastName = person.LastName
    };
}
