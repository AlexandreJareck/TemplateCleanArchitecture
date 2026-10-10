#nullable disable
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Helpers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.UserInterfaces;
using Template.Application.Wrappers;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Settings;

namespace Template.Infrastructure.Identity.Services;
public class AccountServices(
    UserManager<ApplicationUser> userManager,
    IAuthenticatedUserService authenticatedUser,
    SignInManager<ApplicationUser> signInManager,
    JwtSettings jwtSettings,
    ITranslator translator
    ) : IAccountServices
{
    public async Task<BaseResult> ChangePassword(ChangePasswordRequest model, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(authenticatedUser.UserId);

        cancellationToken.ThrowIfCancellationRequested();
        var token = await userManager.GeneratePasswordResetTokenAsync(user!);
        cancellationToken.ThrowIfCancellationRequested();
        var identityResult = await userManager.ResetPasswordAsync(user, token, model.Password);

        if (identityResult.Succeeded)
            return BaseResult.Ok();

        return identityResult.Errors.Select(p => new Error(ErrorCode.ErrorInIdentity, p.Description)).ToList();
    }
    public async Task<BaseResult> ChangeUserName(ChangeUserNameRequest model, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(authenticatedUser.UserId);

        user.UserName = model.UserName;

        cancellationToken.ThrowIfCancellationRequested();
        var identityResult = await userManager.UpdateAsync(user);

        if (identityResult.Succeeded)
            return BaseResult.Ok();

        return identityResult.Errors.Select(p => new Error(ErrorCode.ErrorInIdentity, p.Description)).ToList();
    }
    public async Task<BaseResult<AuthenticationResponse>> Authenticate(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(request.UserName);
        if (user is null)
            return new Error(ErrorCode.NotFound, translator.GetString(TranslatorMessages.AccountMessages.Account_NotFound_with_UserName(request.UserName)), nameof(request.UserName));

        cancellationToken.ThrowIfCancellationRequested();
        var signInResult = await signInManager.PasswordSignInAsync(user.UserName, request.Password, false, lockoutOnFailure: false);
        if (!signInResult.Succeeded)
            return new Error(ErrorCode.FieldDataInvalid, translator.GetString(TranslatorMessages.AccountMessages.Invalid_password()), nameof(request.Password));

        return await GetAuthenticationResponse(user, cancellationToken);
    }
    public async Task<BaseResult<AuthenticationResponse>> AuthenticateByUserName(string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
            return new Error(ErrorCode.NotFound, translator.GetString(TranslatorMessages.AccountMessages.Account_NotFound_with_UserName(username)), nameof(username));

        return await GetAuthenticationResponse(user, cancellationToken);
    }
    public async Task<BaseResult<string>> RegisterGhostAccount(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new ApplicationUser()
        {
            UserName = GenerateRandomString(7)
        };

        var identityResult = await userManager.CreateAsync(user);
        cancellationToken.ThrowIfCancellationRequested();

        if (identityResult.Succeeded)
            return user.UserName;

        return identityResult.Errors.Select(p => new Error(ErrorCode.ErrorInIdentity, p.Description)).ToList();

        string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                    .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
    private async Task<AuthenticationResponse> GetAuthenticationResponse(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await userManager.UpdateSecurityStampAsync(user);

        cancellationToken.ThrowIfCancellationRequested();
        var jwToken = await GenerateJwtToken();

        cancellationToken.ThrowIfCancellationRequested();
        var rolesList = await userManager.GetRolesAsync(user);

        return new AuthenticationResponse()
        {
            Id = user.Id.ToString(),
            JwToken = new JwtSecurityTokenHandler().WriteToken(jwToken),
            Email = user.Email,
            UserName = user.UserName,
            Roles = rolesList,
            IsVerified = user.EmailConfirmed,
        };

        async Task<JwtSecurityToken> GenerateJwtToken()
        {
            var symmetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key));
            var signingCredentials = new SigningCredentials(symmetricSecurityKey, SecurityAlgorithms.HmacSha256);

            return new JwtSecurityToken(
                issuer: jwtSettings.Issuer,
                audience: jwtSettings.Audience,
                claims: (await signInManager.ClaimsFactory.CreateAsync(user)).Claims,
                expires: DateTime.UtcNow.AddMinutes(jwtSettings.DurationInMinutes),
                signingCredentials: signingCredentials);
        }
    }
}
