#nullable enable
namespace Template.Application.Common.Concurrency;

/// <summary>
/// Raised by the unit of work when a versioned aggregate changed after it was read (spike SP-1). Use cases translate
/// it into a deterministic outcome; it never reaches HTTP as a 500.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
