using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Interfaces.UserInterfaces;
using Template.Application.Wrappers;

namespace Template.WebApi.Controllers.v2;

[ApiVersion("2.0")]
public class AccountController(IAccountServices accountServices, ILogger<AccountController> logger) : BaseApiController
{

    /// <summary>
    /// Authenticate in account.
    /// </summary>
    /// <response code="200">Authenticate successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPost("authenticate")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Authenticate(AuthenticationRequest request)
    {
        var result = await accountServices.Authenticate(request);

        return CustomResponse(result);
    }

    /// <summary>
    /// Update user name.
    /// </summary>
    /// <response code="200">Update user name successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPut("change-user-name"), Authorize]
    [ProducesResponseType(typeof(BaseResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeUserName(ChangeUserNameRequest model)
    {
        var result = await accountServices.ChangeUserName(model);

        return CustomResponse(result);
    }

    /// <summary>
    /// Update password.
    /// </summary>
    /// <response code="200">Update password successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPut("change-password"), Authorize]
    [ProducesResponseType(typeof(BaseResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest model)
    {
        var result = await accountServices.ChangePassword(model);

        return CustomResponse(result);
    }

    /// <summary>
    /// Create account ghost.
    /// </summary>
    /// <response code="200">Create account ghost successfully</response>
    /// <response code="400">Invalid data</response>
    [HttpPost("start")]
    [ProducesResponseType(typeof(AuthenticationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Start()
    {
        logger.LogTrace("Log TRACE capturado!");
        logger.LogDebug("Log DEBUG capturado!");
        logger.LogInformation("Log INFORMATION capturado!");
        logger.LogWarning("Log WARNING capturado!");
        logger.LogError("Log ERROR capturado!");
        logger.LogCritical("Log CRITICAL capturado!");

        var ghostUsername = await accountServices.RegisterGhostAccount();
        var result = await accountServices.AuthenticateByUserName(ghostUsername.Data);

        return CustomResponse(result);
    }
}
