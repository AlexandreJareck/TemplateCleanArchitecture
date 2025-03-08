#nullable disable
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Template.WebApi.Infrastructure.Filters;

namespace Template.WebApi.Controllers;
[ApiController]
[ApiResultFilter]
[Route("api/v{version:apiVersion}/[controller]/[action]")]
public abstract class BaseApiController : ControllerBase
{
    private IMediator _mediator;
    protected IMediator Mediator => _mediator ??= HttpContext.RequestServices.GetService<IMediator>();
}
