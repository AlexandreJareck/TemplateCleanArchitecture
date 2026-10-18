#nullable enable
namespace Template.Application.Common.Results;

/// <summary>
/// Outcome of a Phase 24+ use case. New endpoints return plain resource representations (B23-8), so this type is
/// never serialized; the HTTP boundary maps <see cref="Error"/> to Problem Details.
/// </summary>
public class Result
{
    protected Result(ApplicationError? error)
    {
        Error = error;
    }

    public ApplicationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(ApplicationError error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result(ApplicationError error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? value;

    private Result(T? value, ApplicationError? error)
        : base(error)
    {
        this.value = value;
    }

    /// <summary>The value of a successful result; reading it from a failure is a programming error.</summary>
    public T Value => IsSuccess ? value! : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(ApplicationError error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(ApplicationError error) => Failure(error);
}
