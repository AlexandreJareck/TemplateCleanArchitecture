using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" }
        };
    }

    public ProblemDetails FromResult(BaseResult result, HttpContext context)
    {
        var category = result.GetErrorCategory() ?? ErrorCategory.Unexpected;
        var status = category switch
        {
            ErrorCategory.Validation => StatusCodes.Status400BadRequest,
            ErrorCategory.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorCategory.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCategory.NotFound => StatusCodes.Status404NotFound,
            ErrorCategory.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

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

    public ProblemDetails CreateStatusProblem(int statusCode, HttpContext context)
    {
        var title = statusCode switch
        {
            StatusCodes.Status400BadRequest => "One or more validation errors occurred.",
            StatusCodes.Status401Unauthorized => "Authentication is required.",
            StatusCodes.Status403Forbidden => "Access to this resource is forbidden.",
            StatusCodes.Status404NotFound => "The requested resource was not found.",
            StatusCodes.Status409Conflict => "The request conflicts with the current resource state.",
            _ => "An unexpected error occurred."
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://www.rfc-editor.org/rfc/rfc9110#name-{StatusName(statusCode)}",
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
        _ => "500-internal-server-error"
    };
}
