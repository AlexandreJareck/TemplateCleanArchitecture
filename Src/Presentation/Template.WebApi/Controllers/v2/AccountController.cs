using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using FluentValidation;
using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Interfaces.UserInterfaces;
using Template.Application.Wrappers;
using Template.WebApi.Infrastructure.Http;

namespace Template.WebApi.Controllers.v2;

[ApiVersion("2.0")]
public class AccountController(
    IAccountServices accountServices,
    ILogger<AccountController> logger,
    ApiResultMapper resultMapper,
    IValidator<AuthenticationRequest> authenticationValidator,
    IValidator<ChangeUserNameRequest> userNameValidator,
    IValidator<ChangePasswordRequest> passwordValidator) : BaseApiController
{

    /// <summary>
    /// Authenticate in account.
    /// </summary>
    /// <response code="200">Authenticate successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPost("authenticate")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Authenticate([FromBody] AuthenticationRequest request, CancellationToken cancellationToken)
    {
        await authenticationValidator.ValidateAndThrowAsync(request, cancellationToken);
        var result = await accountServices.Authenticate(request, cancellationToken);

        return resultMapper.ToActionResult(this, result);
    }

    /// <summary>
    /// Update user name.
    /// </summary>
    /// <response code="200">Update user name successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPut("change-user-name"), Authorize]
    [ProducesResponseType(typeof(BaseResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ChangeUserName([FromBody] ChangeUserNameRequest model, CancellationToken cancellationToken)
    {
        await userNameValidator.ValidateAndThrowAsync(model, cancellationToken);
        var result = await accountServices.ChangeUserName(model, cancellationToken);

        return resultMapper.ToActionResult(this, result);
    }

    /// <summary>
    /// Update password.
    /// </summary>
    /// <response code="200">Update password successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPut("change-password"), Authorize]
    [ProducesResponseType(typeof(BaseResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest model, CancellationToken cancellationToken)
    {
        await passwordValidator.ValidateAndThrowAsync(model, cancellationToken);
        var result = await accountServices.ChangePassword(model, cancellationToken);

        return resultMapper.ToActionResult(this, result);
    }

    /// <summary>
    /// Create account ghost.
    /// </summary>
    /// <response code="200">Create account ghost successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPost("start")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        logger.LogTrace("Log TRACE capturado!");
        logger.LogDebug("Log DEBUG capturado!");
        logger.LogInformation("Log INFORMATION capturado!");
        logger.LogWarning("Log WARNING capturado!");
        logger.LogError("Log ERROR capturado!");
        logger.LogCritical("Log CRITICAL capturado!");

        var ghostUsername = await accountServices.RegisterGhostAccount(cancellationToken);
        var result = await accountServices.AuthenticateByUserName(ghostUsername.Data, cancellationToken);

        return resultMapper.ToActionResult(this, result);
    }
}
