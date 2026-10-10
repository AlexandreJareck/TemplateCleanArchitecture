#nullable enable
namespace Template.Application.Search.Persons;

/// <summary>
/// A person as stored in and returned by the person search index. It is the application contract of the index, not
/// the domain <c>Person</c> (the v1 HTTP body) and not the stored document format, which belongs to the adapter.
/// </summary>
public sealed record IndexedPerson(int Id, string? FirstName, string? LastName);
