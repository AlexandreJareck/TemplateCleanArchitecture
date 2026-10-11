#nullable enable
namespace Template.Application.Search.Persons;

/// <summary>Result of a person search index operation that returns no value.</summary>
public sealed class PersonIndexResult
{
    private static readonly PersonIndexResult SucceededResult = new(PersonIndexStatus.Succeeded);

    private PersonIndexResult(PersonIndexStatus status) => Status = status;

    public PersonIndexStatus Status { get; }

    public bool Succeeded => Status == PersonIndexStatus.Succeeded;

    public static PersonIndexResult Success() => SucceededResult;

    public static PersonIndexResult Failure(PersonIndexStatus status) =>
        status == PersonIndexStatus.Succeeded
            ? throw new ArgumentOutOfRangeException(nameof(status), status, "A failure needs a non-success status.")
            : new PersonIndexResult(status);
}

/// <summary>
/// Result of a person search index operation that returns a value. <see cref="Value"/> is set only when
/// <see cref="Succeeded"/> is true, so a missing person is never represented by a null value.
/// </summary>
public sealed class PersonIndexResult<T>
    where T : notnull
{
    private PersonIndexResult(PersonIndexStatus status, T? value)
    {
        Status = status;
        Value = value;
    }

    public PersonIndexStatus Status { get; }

    public T? Value { get; }

    public bool Succeeded => Status == PersonIndexStatus.Succeeded;

    public static PersonIndexResult<T> Success(T value) =>
        new(PersonIndexStatus.Succeeded, value ?? throw new ArgumentNullException(nameof(value)));

    public static PersonIndexResult<T> Failure(PersonIndexStatus status) =>
        status == PersonIndexStatus.Succeeded
            ? throw new ArgumentOutOfRangeException(nameof(status), status, "A failure needs a non-success status.")
            : new PersonIndexResult<T>(status, default);
}
