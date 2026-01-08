#nullable disable
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.Application.Wrappers;

namespace Template.WebApi.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    private IMediator _mediator;
    protected IMediator Mediator => _mediator ??= HttpContext.RequestServices.GetService<IMediator>();

    protected IActionResult FromResult(BaseResult result)
    {
        if (result.Success)
            return Ok();

        return MapError(result);
    }

    protected IActionResult FromResult<T>(BaseResult<T> result)
    {
        if (result.Success)
            return Ok(result.Data);

        return MapError(result);
    }

    protected IActionResult FromPagedResult<T>(PagedResponse<T> result)
    {
        if (result.Success)
        {
            return Ok(new
            {
                result.Data,
                result.PageNumber,
                result.PageSize,
                result.TotalPages,
                result.TotalItems
            });
        }

        return MapError(result);
    }

    private IActionResult MapError(BaseResult result)
    {
        var error = result.Errors.First();

        return error.ErrorCode switch
        {
            ErrorCode.ModelStateNotValid => BadRequest(ToProblemDetails(result)),
            ErrorCode.FieldDataInvalid => UnprocessableEntity(ToProblemDetails(result)),
            ErrorCode.NotFound => NotFound(ToProblemDetails(result)),
            ErrorCode.AccessDenied => Forbid(),
            ErrorCode.ErrorInIdentity => Unauthorized(ToProblemDetails(result)),
            ErrorCode.Exception => StatusCode(500, ToProblemDetails(result)),
            _ => StatusCode(500, ToProblemDetails(result))
        };
    }

    private static object ToProblemDetails(BaseResult result)
    {
        return new
        {
            errors = result.Errors.Select(e => new
            {
                code = e.ErrorCode.ToString(),
                field = e.FieldName,
                message = e.Description
            })
        };
    }
}
