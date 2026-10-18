#nullable enable
namespace Template.Application.Common.Operations;

/// <summary>State of an asynchronous operation (docs/product/api-guidelines.md §8).</summary>
public enum OperationStatus
{
    Queued,
    Running,
    Ready,
    Failed,
    Expired
}

/// <summary>
/// Representation returned with <c>202 Accepted</c> and by the job resource. <see cref="ContentUrl"/> is an API path,
/// never a storage URL, and is set only when <see cref="Status"/> is <see cref="OperationStatus.Ready"/>.
/// </summary>
public sealed record AcceptedOperation(Guid Id, OperationStatus Status, DateTime RequestedAt, string? ContentUrl = null);
