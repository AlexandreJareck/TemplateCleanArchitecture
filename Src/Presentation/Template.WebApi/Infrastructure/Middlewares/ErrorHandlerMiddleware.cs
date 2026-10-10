using FluentValidation;
using Template.Application.Wrappers;
using Template.WebApi.Infrastructure.Http;

namespace Template.WebApi.Infrastructure.Middlewares;

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
        catch (Exception error)
        {
            BaseResult responseModel;
            switch (error)
            {
                case ValidationException validationException:
                    responseModel = BaseResult.Failure(validationException.Errors.Select(validationFailure =>
                        new Error(
                            ErrorCode.ModelStateNotValid,
                            validationFailure.ErrorMessage,
                            validationFailure.PropertyName)));
                    break;
                case KeyNotFoundException keyNotFoundException:
                    responseModel = BaseResult.Failure(
                        new Error(ErrorCode.NotFound, keyNotFoundException.Message));
                    break;
                default:
                    logger.LogError(
                        error,
                        "Unhandled exception while processing {Method} {Path}",
                        context.Request.Method,
                        context.Request.Path);
                    responseModel = BaseResult.Failure(new Error(ErrorCode.Exception));
                    break;
            }

            await resultMapper.WriteAsync(context, resultMapper.FromResult(responseModel, context));
        }
    }
}
