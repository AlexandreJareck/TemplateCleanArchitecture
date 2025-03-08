using FluentValidation;
using Template.Application.Helpers;
using Template.Application.Interfaces;

namespace Template.Application.DTOs.Account.Request;
public class AuthenticationRequest
{
    public string UserName { get; set; }

    public string Password { get; set; }
}
public class AuthenticationRequestValidator : AbstractValidator<AuthenticationRequest>
{
    public AuthenticationRequestValidator(ITranslator translator)
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .NotNull()
            .WithName(p => translator[nameof(p.UserName)]);

        RuleFor(x => x.Password)
        .NotEmpty()
            .NotNull()
            .Matches(Regexs.Password)
            .WithName(p => translator[nameof(p.Password)]);
    }
}
