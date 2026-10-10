using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Wrappers;

namespace Template.Application.Interfaces.UserInterfaces;
public interface IAccountServices
{
    Task<BaseResult<string>> RegisterGhostAccount(CancellationToken cancellationToken = default);
    Task<BaseResult> ChangePassword(ChangePasswordRequest model, CancellationToken cancellationToken = default);
    Task<BaseResult> ChangeUserName(ChangeUserNameRequest model, CancellationToken cancellationToken = default);
    Task<BaseResult<AuthenticationResponse>> Authenticate(AuthenticationRequest request, CancellationToken cancellationToken = default);
    Task<BaseResult<AuthenticationResponse>> AuthenticateByUserName(string username, CancellationToken cancellationToken = default);
}
