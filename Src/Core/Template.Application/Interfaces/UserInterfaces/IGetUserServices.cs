using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Wrappers;

namespace Template.Application.Interfaces.UserInterfaces;
public interface IGetUserServices
{
    Task<PagedResponse<UserDto>> GetPagedUsers(GetAllUsersRequest model);
}
