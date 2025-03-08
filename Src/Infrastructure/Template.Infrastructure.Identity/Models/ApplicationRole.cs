using Microsoft.AspNetCore.Identity;

namespace Template.Infrastructure.Identity.Models;
public class ApplicationRole(string name) : IdentityRole<Guid>(name)
{
}
