#nullable enable
using MediatR;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Auditing;

namespace Template.Application.Features.Auditing.Queries;

/// <summary>
/// Global audit query (AUD-004, permission <c>audit.read</c>). Paging, filtering and sorting follow
/// docs/product/api-guidelines.md §3; the default order is newest first.
/// </summary>
public sealed record ListAuditRecordsQuery(
    int? Page = null,
    int? PageSize = null,
    string? Sort = null,
    string? Action = null,
    Guid? ActorId = null,
    string? ResourceType = null,
    string? ResourceId = null,
    DateTime? From = null,
    DateTime? To = null) : IRequest<Result<PagedResult<AuditRecordView>>>;

public sealed class ListAuditRecordsQueryHandler(IAuditRecordReader reader)
    : IRequestHandler<ListAuditRecordsQuery, Result<PagedResult<AuditRecordView>>>
{
    public const string DefaultSort = "-occurredAt";

    public async Task<Result<PagedResult<AuditRecordView>>> Handle(ListAuditRecordsQuery request, CancellationToken cancellationToken)
    {
        var page = PageRequest.Create(request.Page, request.PageSize, request.Sort, IAuditRecordReader.SortFields, DefaultSort);
        if (!page.IsSuccess)
            return page.Error!;

        var from = ToUtc(request.From);
        var to = ToUtc(request.To);
        if (from is not null && to is not null && from >= to)
            return ApplicationError.Validation("from", "from must be earlier than to.");

        var filter = new AuditRecordFilter(request.Action, request.ActorId, request.ResourceType, request.ResourceId, from, to);
        return await reader.ListAsync(filter, page.Value, cancellationToken);
    }

    // Query-string instants without an offset are read as UTC (docs/product/api-guidelines.md §2).
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        var unspecified => DateTime.SpecifyKind(unspecified.Value, DateTimeKind.Utc)
    };
}
