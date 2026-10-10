using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using FluentValidation;
using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Interfaces.UserInterfaces;
using Template.Application.Wrappers;

namespace Template.WebApi.Controllers.v1;

[ApiVersion("1", Deprecated = true)]
public class AccountController(
    IAccountServices accountServices,
    ILogger<AccountController> logger,
    IValidator<AuthenticationRequest> authenticationValidator,
    IValidator<ChangeUserNameRequest> userNameValidator,
    IValidator<ChangePasswordRequest> passwordValidator) : BaseApiController
{
    [HttpPost("authenticate")]
    public async Task<BaseResult<AuthenticationResponse>> Authenticate([FromBody] AuthenticationRequest request, CancellationToken cancellationToken)
    {
        await authenticationValidator.ValidateAndThrowAsync(request, cancellationToken);
        return await accountServices.Authenticate(request, cancellationToken);
    }

    [HttpPut("change-user-name"), Authorize]
    public async Task<BaseResult> ChangeUserName([FromBody] ChangeUserNameRequest model, CancellationToken cancellationToken)
    {
        await userNameValidator.ValidateAndThrowAsync(model, cancellationToken);
        return await accountServices.ChangeUserName(model, cancellationToken);
    }

    [HttpPut("change-password"), Authorize]
    public async Task<BaseResult> ChangePassword([FromBody] ChangePasswordRequest model, CancellationToken cancellationToken)
    {
        await passwordValidator.ValidateAndThrowAsync(model, cancellationToken);
        return await accountServices.ChangePassword(model, cancellationToken);
    }

    [HttpPost("start")]
    public async Task<BaseResult<AuthenticationResponse>> Start(CancellationToken cancellationToken)
    {
        logger.LogTrace("Log TRACE capturado!");
        logger.LogDebug("Log DEBUG capturado!");
        logger.LogInformation("Log INFORMATION capturado!");
        logger.LogWarning("Log WARNING capturado!");
        logger.LogError("Log ERROR capturado!");
        logger.LogCritical("Log CRITICAL capturado!");

        var ghostUsername = await accountServices.RegisterGhostAccount(cancellationToken);
        return await accountServices.AuthenticateByUserName(ghostUsername.Data, cancellationToken);
    }
}
