using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Interfaces.UserInterfaces;
using Template.Application.Wrappers;

namespace Template.WebApi.Controllers.v1;

[ApiVersion("1")]
public class AccountController(IAccountServices accountServices, ILogger<AccountController> logger) : BaseApiController
{
    [HttpPost]
    public async Task<BaseResult<AuthenticationResponse>> Authenticate(AuthenticationRequest request)
        => await accountServices.Authenticate(request);

    [HttpPut, Authorize]
    public async Task<BaseResult> ChangeUserName(ChangeUserNameRequest model)
        => await accountServices.ChangeUserName(model);

    [HttpPut, Authorize]
    public async Task<BaseResult> ChangePassword(ChangePasswordRequest model)
        => await accountServices.ChangePassword(model);

    [HttpPost]
    public async Task<BaseResult<AuthenticationResponse>> Start()
    {
        logger.LogTrace("Log TRACE capturado!");
        logger.LogDebug("Log DEBUG capturado!");
        logger.LogInformation("Log INFORMATION capturado!");
        logger.LogWarning("Log WARNING capturado!");
        logger.LogError("Log ERROR capturado!");
        logger.LogCritical("Log CRITICAL capturado!");

        var ghostUsername = await accountServices.RegisterGhostAccount();
        return await accountServices.AuthenticateByUserName(ghostUsername.Data);
    }
}
