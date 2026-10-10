namespace Template.Application.Search.Persons;

/// <summary>Outcome categories of a person search index operation.</summary>
public enum PersonIndexStatus
{
    /// <summary>The operation completed and was acknowledged by the search engine.</summary>
    Succeeded,

    /// <summary>The person (or the index holding it) does not exist.</summary>
    NotFound,

    /// <summary>The search engine could not be reached, timed out, reported a server-side failure, or search is disabled.</summary>
    Unavailable,

    /// <summary>The search engine refused the request (for example an invalid index name or a mapping conflict).</summary>
    Rejected,

    /// <summary>A stored document could not be read as an <see cref="IndexedPerson"/>.</summary>
    IncompatibleDocument
}
