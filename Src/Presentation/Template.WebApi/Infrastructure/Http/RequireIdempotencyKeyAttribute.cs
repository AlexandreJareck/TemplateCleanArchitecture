using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Template.Application.Common.Results;
using Template.Application.Interfaces.Idempotency;
using Template.Application.Wrappers;

namespace Template.WebApi.Infrastructure.Http;

/// <summary>
/// <c>Idempotency-Key</c> semantics of docs/product/api-guidelines.md §6 for one action:
/// <list type="bullet">
/// <item>missing or longer than 64 characters → 400 <c>idempotency.key-required</c>;</item>
/// <item>same key + same operation + same body (SHA-256 of the exact request bytes) → the stored status, body and
/// <c>Location</c> are replayed with <c>Idempotent-Replayed: true</c>;</item>
/// <item>same key with a different operation or body → 422 <c>idempotency.key-reused</c>;</item>
/// <item>same key while the first request still runs → 409 <c>idempotency.in-progress</c>.</item>
/// </list>
/// Keys are scoped to the authenticated user and live 24 hours. 5xx outcomes and exceptions release the key. It
/// complements, never replaces, domain idempotency.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIdempotencyKeyAttribute : Attribute, IFilterFactory
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";
    public const int MaxKeyLength = 64;

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => new Filter(
        serviceProvider.GetRequiredService<IIdempotencyStore>(),
        serviceProvider.GetRequiredService<ApiResultMapper>());

    private sealed class Filter(IIdempotencyStore store, ApiResultMapper resultMapper) : IAsyncResourceFilter
    {
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            var http = context.HttpContext;
            var key = http.Request.Headers[HeaderName].ToString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
            {
                context.Result = resultMapper.ToProblemResult(
                    new ApplicationError(
                        ProblemCodes.IdempotencyKeyRequired,
                        ErrorCategory.Validation,
                        $"The {HeaderName} header (1-{MaxKeyLength} characters) is required.",
                        FieldErrors: new Dictionary<string, string[]> { [HeaderName] = [$"{HeaderName} must have 1 to {MaxKeyLength} characters."] }),
                    http);
                return;
            }

            var request = new IdempotencyRequest(
                Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty,
                key,
                $"{http.Request.Method} {context.ActionDescriptor.AttributeRouteInfo?.Template ?? http.Request.Path.Value}",
                await HashBodyAsync(http.Request));

            var begin = await store.BeginAsync(request, http.RequestAborted);
            switch (begin.Status)
            {
                case IdempotencyBeginStatus.Replay:
                    Replay(context, begin.Response!);
                    return;
                case IdempotencyBeginStatus.InProgress:
                    context.Result = resultMapper.ToProblemResult(new ApplicationError(
                        ProblemCodes.IdempotencyInProgress, ErrorCategory.Conflict, "A request with this idempotency key is still running."), http);
                    return;
                case IdempotencyBeginStatus.KeyReused:
                    context.Result = resultMapper.ToProblemResult(new ApplicationError(
                        ProblemCodes.IdempotencyKeyReused, ErrorCategory.BusinessRule, "This idempotency key was used for a different request."), http);
                    return;
            }

            var originalBody = http.Response.Body;
            await using var capture = new MemoryStream();
            http.Response.Body = capture;
            ResourceExecutedContext executed;
            try
            {
                executed = await next();
            }
            catch
            {
                await store.AbandonAsync(request, CancellationToken.None);
                throw;
            }
            finally
            {
                http.Response.Body = originalBody;
            }

            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                // MVC rethrows the exception after this filter; the key is released so a retry executes again.
                await store.AbandonAsync(request, CancellationToken.None);
                return;
            }

            capture.Position = 0;
            var body = capture.Length == 0 ? null : await new StreamReader(capture).ReadToEndAsync(http.RequestAborted);
            if (http.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                await store.AbandonAsync(request, CancellationToken.None);
            else
                await store.CompleteAsync(
                    request,
                    new StoredIdempotentResponse(http.Response.StatusCode, http.Response.ContentType, body, http.Response.Headers.Location.ToString() is { Length: > 0 } location ? location : null),
                    CancellationToken.None);

            capture.Position = 0;
            await capture.CopyToAsync(originalBody, http.RequestAborted);
        }

        private static void Replay(ResourceExecutingContext context, StoredIdempotentResponse stored)
        {
            var response = context.HttpContext.Response;
            response.Headers[ReplayedHeaderName] = "true";
            if (stored.Location is not null)
                response.Headers.Location = stored.Location;
            context.Result = new ContentResult
            {
                StatusCode = stored.StatusCode,
                ContentType = stored.ContentType,
                Content = stored.Body ?? string.Empty
            };
        }

        private static async Task<string> HashBodyAsync(HttpRequest request)
        {
            request.EnableBuffering();
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
                sha.AppendData(buffer, 0, read);
            request.Body.Position = 0;
            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }
    }
}
