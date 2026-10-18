#nullable enable
namespace Template.Application.Common.Concurrency;

/// <summary>
/// Raised by the unit of work when a commit violates a unique index that is not a concurrency guard, for example
/// two concurrent creations with the same master-data code (ORG-001, CAT-001, SUP-001). <see cref="ConstraintName"/>
/// is the database index name; use cases translate it into a stable duplicate error instead of a 500.
/// </summary>
public sealed class UniqueConstraintViolationException(string constraintName, Exception? innerException = null)
    : Exception("A unique constraint was violated.", innerException)
{
    public string ConstraintName { get; } = constraintName;
}
