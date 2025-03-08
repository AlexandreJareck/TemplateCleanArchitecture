using Template.Application.Parameters;

namespace Template.Application.DTOs.Account.Request;
public class GetAllUsersRequest : PaginationRequestParameter
{
    public string Name { get; set; }
}
