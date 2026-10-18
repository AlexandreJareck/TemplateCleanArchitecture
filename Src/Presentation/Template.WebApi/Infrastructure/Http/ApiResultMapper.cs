using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Template.Application.Common.Results;
using Template.Application.Wrappers;

namespace Template.WebApi.Infrastructure.Http;

public sealed class ApiResultMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IActionResult ToActionResult(
        ControllerBase controller,
        BaseResult result,
        Func<IActionResult>? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Success)
            return onSuccess?.Invoke() ?? controller.Ok(result);

        var problem = FromResult(result, controller.HttpContext);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public IActionResult FromModelState(ActionContext context)
    {
        var problem = CreateValidationProblem(context.ModelState, context.HttpContext);
        if (context.ActionDescriptor.EndpointMetadata.OfType<Security.StableProblemCodesAttribute>().Any())
            problem.Extensions["code"] = ProblemCodes.Validation;
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    }

    public ProblemDetails FromResult(BaseResult result, HttpContext context)
    {
        var category = result.GetErrorCategory() ?? ErrorCategory.Unexpected;
        var status = StatusFor(category);

        if (category == ErrorCategory.Validation)
        {
            var errors = (result.Errors ?? [])
                .GroupBy(error => string.IsNullOrWhiteSpace(error.FieldName) ? string.Empty : error.FieldName)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.Description ?? "The supplied value is invalid.").ToArray());
            var validationProblem = NewValidationProblem(errors, context);
            validationProblem.Extensions["errorCodes"] = (result.Errors ?? [])
                .Select(error => new { code = error.ErrorCode.ToString(), field = error.FieldName })
                .ToArray();
            return validationProblem;
        }

        var problem = CreateStatusProblem(status, context);
        if (status != StatusCodes.Status500InternalServerError)
        {
            problem.Extensions["errors"] = (result.Errors ?? [])
                .Select(error => new
                {
                    code = error.ErrorCode.ToString(),
                    field = error.FieldName,
                    message = error.Description
                })
                .ToArray();
        }

        return problem;
    }

    /// <summary>
    /// Maps a Phase 24+ <see cref="Result"/> (B23-8): success runs <paramref name="onSuccess"/>, failure becomes Problem
    /// Details with the stable <c>code</c> extension (docs/product/api-guidelines.md §5).
    /// </summary>
    public IActionResult ToActionResult(Result result, HttpContext context, Func<IActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? onSuccess() : ToProblemResult(result.Error!, context);
    }

    public IActionResult ToProblemResult(ApplicationError error, HttpContext context)
    {
        var problem = FromError(error, context);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public ProblemDetails FromError(ApplicationError error, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = StatusFor(error.Category);
        var problem = error.Category == ErrorCategory.Validation
            ? NewValidationProblem(
                error.FieldErrors?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, string[]>(),
                context)
            : CreateStatusProblem(status, context);

        if (status != StatusCodes.Status500InternalServerError)
        {
            problem.Detail = error.Message;
            problem.Extensions["code"] = error.Code;
            if (error.RuleId is not null)
                problem.Extensions["ruleId"] = error.RuleId;
            foreach (var (key, value) in error.Extensions ?? new Dictionary<string, object?>())
                problem.Extensions[key] = value;
        }

        return problem;
    }

    public static int StatusFor(ErrorCategory category) => category switch
    {
        ErrorCategory.Validation => StatusCodes.Status400BadRequest,
        ErrorCategory.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorCategory.Forbidden => StatusCodes.Status403Forbidden,
        ErrorCategory.NotFound => StatusCodes.Status404NotFound,
        ErrorCategory.Conflict => StatusCodes.Status409Conflict,
        ErrorCategory.Gone => StatusCodes.Status410Gone,
        ErrorCategory.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorCategory.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorCategory.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
        ErrorCategory.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCategory.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError
    };

    public ProblemDetails CreateStatusProblem(int statusCode, HttpContext context)
    {
        var title = statusCode switch
        {
            StatusCodes.Status400BadRequest => "One or more validation errors occurred.",
            StatusCodes.Status401Unauthorized => "Authentication is required.",
            StatusCodes.Status403Forbidden => "Access to this resource is forbidden.",
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status409Conflict => "The request conflicts with the current resource state.",
            StatusCodes.Status410Gone => "The requested resource is no longer available.",
            StatusCodes.Status412PreconditionFailed => "A request precondition failed.",
            StatusCodes.Status422UnprocessableEntity => "The request violates a business rule.",
            StatusCodes.Status428PreconditionRequired => "The request requires a precondition.",
            StatusCodes.Status429TooManyRequests => "Too many requests.",
            StatusCodes.Status503ServiceUnavailable => "An optional external provider is unavailable.",
            _ => "An unexpected error occurred."
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = statusCode is StatusCodes.Status428PreconditionRequired or StatusCodes.Status429TooManyRequests
                ? $"https://www.rfc-editor.org/rfc/rfc6585#section-{(statusCode == StatusCodes.Status428PreconditionRequired ? 3 : 4)}"
                : $"https://www.rfc-editor.org/rfc/rfc9110#name-{StatusName(statusCode)}",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }

    public async Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            problem,
            problem.GetType(),
            JsonOptions,
            context.RequestAborted);
    }

    private ValidationProblemDetails CreateValidationProblem(ModelStateDictionary modelState, HttpContext context)
    {
        var errors = modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(error =>
                    string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The supplied value is invalid." : error.ErrorMessage).ToArray());
        return NewValidationProblem(errors, context);
    }

    private static ValidationProblemDetails NewValidationProblem(
        IDictionary<string, string[]> errors,
        HttpContext context)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://www.rfc-editor.org/rfc/rfc9110#name-400-bad-request",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }

    private static string StatusName(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "400-bad-request",
        StatusCodes.Status401Unauthorized => "401-unauthorized",
        StatusCodes.Status403Forbidden => "403-forbidden",
        StatusCodes.Status404NotFound => "404-not-found",
        StatusCodes.Status409Conflict => "409-conflict",
        StatusCodes.Status410Gone => "410-gone",
        StatusCodes.Status412PreconditionFailed => "412-precondition-failed",
        StatusCodes.Status422UnprocessableEntity => "422-unprocessable-content",
        // RFC 6585 status codes are not in RFC 9110.
        StatusCodes.Status428PreconditionRequired => "428-precondition-required",
        StatusCodes.Status429TooManyRequests => "429-too-many-requests",
        StatusCodes.Status503ServiceUnavailable => "503-service-unavailable",
        _ => "500-internal-server-error"
    };
}
