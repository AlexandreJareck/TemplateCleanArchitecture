using FluentValidation;
using Template.Application.Logging;
using Template.Application.Wrappers;
using Template.WebApi.Infrastructure.Http;

namespace Template.WebApi.Infrastructure.Middlewares;

/// <summary>
/// The single owner of unexpected-exception logging. Expected failures (validation, not found) are mapped through
/// <see cref="ApiResultMapper"/> without an error log; any other exception is logged exactly once with
/// <see cref="LogEvents.UnhandledException"/>, method, path and the <c>traceId</c> returned in the sanitized 500
/// Problem Details. A cancellation caused by the client aborting the request is not an error: it is logged as
/// <see cref="LogEvents.RequestCanceled"/> and answered with 499 when the response has not started.
/// </summary>
public sealed class ErrorHandlerMiddleware(
    RequestDelegate next,
    ILogger<ErrorHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ApiResultMapper resultMapper)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                LogEvents.RequestCanceled,
                "Request {RequestMethod} {RequestPath} was canceled by the client ({CorrelationId})",
                context.Request.Method,
                context.Request.Path.Value,
                context.TraceIdentifier);
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
        }
        catch (Exception error)
        {
            var responseModel = MapExpected(error);
            if (responseModel is null)
            {
                logger.LogError(
                    LogEvents.UnhandledException,
                    error,
                    "Unhandled exception while processing {RequestMethod} {RequestPath} ({CorrelationId})",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.TraceIdentifier);
                responseModel = BaseResult.Failure(new Error(ErrorCode.Exception));
            }

            if (context.Response.HasStarted)
            {
                // A partial body cannot be turned into Problem Details; abort instead of rethrowing (and relogging).
                context.Abort();
                return;
            }

            await resultMapper.WriteAsync(context, resultMapper.FromResult(responseModel, context));
        }
    }

    private static BaseResult? MapExpected(Exception error) => error switch
    {
        ValidationException validationException => BaseResult.Failure(validationException.Errors.Select(validationFailure =>
            new Error(
                ErrorCode.ModelStateNotValid,
                validationFailure.ErrorMessage,
                validationFailure.PropertyName))),
        KeyNotFoundException keyNotFoundException => BaseResult.Failure(
            new Error(ErrorCode.NotFound, keyNotFoundException.Message)),
        _ => null
    };
}
