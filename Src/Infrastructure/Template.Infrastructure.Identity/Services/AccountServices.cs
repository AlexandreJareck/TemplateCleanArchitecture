using Microsoft.AspNetCore.Identity;
using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Helpers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.UserInterfaces;
using Template.Application.Wrappers;
using Template.Infrastructure.Identity.Authentication;
using Template.Infrastructure.Identity.Models;

namespace Template.Infrastructure.Identity.Services;

/// <summary>
/// Identity adapter for <see cref="IAccountServices"/>. ASP.NET Core Identity APIs do not accept a
/// <see cref="CancellationToken"/>, so cancellation is checked before and between their calls.
/// </summary>
public class AccountServices(
    UserManager<ApplicationUser> userManager,
    IAuthenticatedUserService authenticatedUser,
    SignInManager<ApplicationUser> signInManager,
    JwtTokenFactory tokenFactory,
    ITranslator translator
    ) : IAccountServices
{
    public async Task<BaseResult> ChangePassword(ChangePasswordRequest model, CancellationToken cancellationToken = default)
    {
        var user = await FindCurrentUserAsync(cancellationToken);
        if (user is null)
            return CurrentUserUnavailable();

        cancellationToken.ThrowIfCancellationRequested();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        cancellationToken.ThrowIfCancellationRequested();
        // ResetPasswordAsync replaces the hash and rotates the security stamp, so tokens issued
        // before this call fail stamp validation afterwards.
        var identityResult = await userManager.ResetPasswordAsync(user, token, model.Password);

        if (identityResult.Succeeded)
            return BaseResult.Ok();

        return identityResult.Errors.Select(p => new Error(ErrorCode.ErrorInIdentity, p.Description)).ToList();
    }
    public async Task<BaseResult> ChangeUserName(ChangeUserNameRequest model, CancellationToken cancellationToken = default)
    {
        var user = await FindCurrentUserAsync(cancellationToken);
        if (user is null)
            return CurrentUserUnavailable();

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
        var signInResult = await signInManager.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: false);
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

    private async Task<ApplicationUser?> FindCurrentUserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return authenticatedUser.UserId is { Length: > 0 } userId
            ? await userManager.FindByIdAsync(userId)
            : null;
    }

    // No current user (unauthenticated, missing/malformed subject, or deleted account): the HTTP
    // boundary maps ErrorInIdentity to 401 without revealing which case applied.
    private static Error CurrentUserUnavailable() => new(ErrorCode.ErrorInIdentity, "Authentication is required.");

    private async Task<AuthenticationResponse> GetAuthenticationResponse(ApplicationUser user, CancellationToken cancellationToken)
    {
        // Every sign-in rotates the stamp, so a new token invalidates tokens issued earlier for this user.
        cancellationToken.ThrowIfCancellationRequested();
        await userManager.UpdateSecurityStampAsync(user);

        cancellationToken.ThrowIfCancellationRequested();
        var principal = await signInManager.ClaimsFactory.CreateAsync(user);

        cancellationToken.ThrowIfCancellationRequested();
        var rolesList = await userManager.GetRolesAsync(user);

        return new AuthenticationResponse()
        {
            Id = user.Id.ToString(),
            JwToken = tokenFactory.CreateToken(principal.Claims),
            Email = user.Email,
            UserName = user.UserName,
            Roles = rolesList,
            IsVerified = user.EmailConfirmed,
        };
    }
}
