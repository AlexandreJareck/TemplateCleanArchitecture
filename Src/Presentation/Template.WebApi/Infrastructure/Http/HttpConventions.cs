using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Operations;

namespace Template.WebApi.Infrastructure.Http;

/// <summary>
/// Optimistic-concurrency headers (docs/product/api-guidelines.md §4). The version is exposed as a strong ETag
/// <c>"&lt;version&gt;"</c>; writes send it back in <c>If-Match</c>. Weak tags, lists and <c>*</c> never match a version.
/// </summary>
public static class VersionHeaders
{
    public static void SetETag(HttpResponse response, long version) =>
        response.Headers.ETag = new EntityTagHeaderValue($"\"{VersionPrecondition.Format(version)}\"").ToString();

    /// <summary>
    /// <c>null</c> when <c>If-Match</c> is absent (→ 428); <c>0</c> when present but not a single strong version
    /// (→ 412, since no aggregate has version 0); otherwise the expected version.
    /// </summary>
    public static long? ReadIfMatch(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return EntityTagHeaderValue.TryParseStrictList([raw], out var tags)
            && tags.Count == 1
            && !tags[0].IsWeak
            && tags[0] != EntityTagHeaderValue.Any
            && VersionPrecondition.TryParse(tags[0].Tag.Value?.Trim('"'), out var version)
                ? version
                : 0;
    }
}

/// <summary>Job representation on the wire: the status is a string (docs/product/api-guidelines.md §2).</summary>
public sealed record AcceptedOperationResponse(Guid Id, string Status, DateTime RequestedAt, string? ContentUrl)
{
    public static AcceptedOperationResponse From(AcceptedOperation operation) =>
        new(operation.Id, operation.Status.ToString(), operation.RequestedAt, operation.ContentUrl);
}

/// <summary><c>202 Accepted</c> for asynchronous operations (docs/product/api-guidelines.md §8).</summary>
public static class AcceptedOperations
{
    public const int RetryAfterSeconds = 2;

    public static AcceptedResult Create(HttpResponse response, string location, AcceptedOperation operation)
    {
        response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return new AcceptedResult(location, AcceptedOperationResponse.From(operation));
    }
}

/// <summary>
/// Streamed download (docs/product/api-guidelines.md §9, FIL-003): <c>Content-Disposition: attachment</c> with the
/// sanitized name, the exact media type, <c>X-Content-Type-Options: nosniff</c> and <c>Cache-Control: private, no-store</c>.
/// The headers are written when the result executes, after result filters, so no filter can relax them.
/// </summary>
public sealed class AttachmentStreamResult(Stream content, string mediaType, string fileName, long length) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.CacheControl = "private, no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        if (length > 0)
            response.ContentLength = length;
        await new FileStreamResult(content, mediaType) { EnableRangeProcessing = false }.ExecuteResultAsync(context);
    }
}
