using System.Text.Json;
using System.Text.Json.Serialization;
using Template.Application.Search.Persons;

namespace Template.Infrastructure.Search.Persons;

/// <summary>
/// Stored format of a person in the index, pinned to what the previous implementation wrote through the client's
/// default source serializer: camelCase names, null names omitted, and the numeric id repeated in the document and
/// used as the document id (for example <c>{"id":5,"firstName":"Eva"}</c>). Unknown fields are ignored, missing ones
/// read as defaults, and a numeric id written as a JSON string is accepted, as before.
/// </summary>
internal sealed class PersonDocument
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("firstName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstName { get; init; }

    [JsonPropertyName("lastName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastName { get; init; }

    public static PersonDocument From(IndexedPerson person) => new()
    {
        Id = person.Id,
        FirstName = person.FirstName,
        LastName = person.LastName
    };

    /// <summary>Maps a stored <c>_source</c>; returns false when it cannot be read as a person.</summary>
    public static bool TryRead(JsonElement source, out IndexedPerson person)
    {
        person = null!;
        if (source.ValueKind != JsonValueKind.Object)
            return false;

        try
        {
            var document = source.Deserialize<PersonDocument>(ReadOptions);
            if (document is null)
                return false;

            person = new IndexedPerson(document.Id, document.FirstName, document.LastName);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
