using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Template.Application.DTOs.Account.Request;
using Template.Application.Interfaces;
using Template.Application.Wrappers;
using Template.Functional.Tests.Common;
using Template.Infrastructure.Identity.Authentication;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Services;
using Template.Infrastructure.Identity.Settings;
using Template.WebApi.Infrastructure.Services;
using static Template.Functional.Tests.Security.SecurityTestSupport;

namespace Template.Functional.Tests.Security;

/// <summary>Component tests for token issuance, validation parameters, the current-user adapter and account failures.</summary>
public sealed class IdentityComponentTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private static readonly DateTime FixedNow = new(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public async Task TokenFactory_UsesConfiguredIssuerAudienceLifetimeAndClock()
    {
        var settings = JwtSettings(factory.Services);
        var (userName, claims) = await CreateUserWithRoleAsync();
        var tokenFactory = new JwtTokenFactory(Options.Create(settings), new FixedClock(FixedNow));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(tokenFactory.CreateToken(claims));

        token.Issuer.ShouldBe(settings.Issuer);
        token.Audiences.ShouldBe([settings.Audience]);
        token.ValidFrom.ShouldBe(FixedNow);
        token.ValidTo.ShouldBe(FixedNow.AddMinutes(settings.DurationInMinutes));
        token.Header.Alg.ShouldBe(SecurityAlgorithms.HmacSha256);
        // The token carries the Identity claim types verbatim (no outbound short-name mapping); clients rely on them.
        token.Claims.ShouldContain(claim => claim.Type == ClaimTypes.NameIdentifier && claim.Value.Length == 36);
        token.Claims.ShouldContain(claim => claim.Type == ClaimTypes.Name && claim.Value == userName);
        token.Claims.ShouldContain(claim => claim.Type == ClaimTypes.Role && claim.Value == "Admin");
        token.Claims.ShouldContain(claim => claim.Type == "AspNet.Identity.SecurityStamp" && claim.Value.Length > 0);
    }

    [Fact]
    public void ValidationParameters_KeepEveryCheckEnabled_WithExplicitZeroSkew()
    {
        var parameters = JwtTokenFactory.CreateValidationParameters(JwtSettings(factory.Services));

        parameters.ValidateIssuerSigningKey.ShouldBeTrue();
        parameters.ValidateIssuer.ShouldBeTrue();
        parameters.ValidateAudience.ShouldBeTrue();
        parameters.ValidateLifetime.ShouldBeTrue();
        parameters.RequireExpirationTime.ShouldBeTrue();
        parameters.RequireSignedTokens.ShouldBeTrue();
        parameters.ClockSkew.ShouldBe(TimeSpan.Zero);
        parameters.ValidAlgorithms.ShouldBe([SecurityAlgorithms.HmacSha256]);
    }

    [Fact]
    public void JwtBearerOptions_AreComposedFromValidatedSettings()
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        options.RequireHttpsMetadata.ShouldBeTrue();
        options.IncludeErrorDetails.ShouldBeFalse();
        options.SaveToken.ShouldBeFalse();
        options.Authority.ShouldBeNull();
        options.MetadataAddress.ShouldBeNullOrEmpty();
        options.TokenValidationParameters.ValidIssuer.ShouldBe(JwtSettings(factory.Services).Issuer);
        options.Events.OnTokenValidated.ShouldNotBeNull();
    }

    [Fact]
    public void CurrentUser_MapsAuthenticatedClaims()
    {
        var id = Guid.NewGuid();
        var user = CurrentUser(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id.ToString()), new(ClaimTypes.Name, "synthetic-user")], "Bearer"));

        user.UserId.ShouldBe(id.ToString());
        user.UserName.ShouldBe("synthetic-user");
    }

    [Fact]
    public void CurrentUser_MissingIdentifier_IsNoUser()
    {
        var user = CurrentUser(new ClaimsIdentity([new(ClaimTypes.Name, "synthetic-user")], "Bearer"));

        user.UserId.ShouldBeNull();
        user.UserName.ShouldBe("synthetic-user");
    }

    [Fact]
    public void CurrentUser_MalformedIdentifierAndEmptyName_AreNoUser()
    {
        var user = CurrentUser(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "not-a-guid"), new(ClaimTypes.Name, " ")], "Bearer"));

        user.UserId.ShouldBeNull();
        user.UserName.ShouldBeNull();
    }

    [Fact]
    public void CurrentUser_UnauthenticatedPrincipal_IsNoUser()
    {
        var user = CurrentUser(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())])); // no auth type

        user.UserId.ShouldBeNull();
        user.UserName.ShouldBeNull();
    }

    [Fact]
    public void CurrentUser_WithoutHttpContext_IsNoUser()
    {
        var user = new AuthenticatedUserService(new HttpContextAccessor());

        user.UserId.ShouldBeNull();
        user.UserName.ShouldBeNull();
    }

    [Fact]
    public void CurrentUser_ReadsClaimsOnAccess_NotAtConstruction()
    {
        var accessor = new HttpContextAccessor();
        var user = new AuthenticatedUserService(accessor);
        var id = Guid.NewGuid();

        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id.ToString())], "Bearer"))
        };

        user.UserId.ShouldBe(id.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountServices_WithoutCurrentUser_ReturnsIdentityError(bool unknownUser)
    {
        using var scope = factory.Services.CreateScope();
        var services = CreateAccountServices(scope.ServiceProvider, new StubCurrentUser(unknownUser ? Guid.NewGuid().ToString() : null));

        var password = await services.ChangePassword(new ChangePasswordRequest { Password = SyntheticPassword, ConfirmPassword = SyntheticPassword });
        var userName = await services.ChangeUserName(new ChangeUserNameRequest { UserName = "SyntheticName" });

        foreach (var result in new[] { password, userName })
        {
            result.Success.ShouldBeFalse();
            result.Errors!.Single().ErrorCode.ShouldBe(ErrorCode.ErrorInIdentity);
            result.GetErrorCategory().ShouldBe(ErrorCategory.Unauthorized);
        }
    }

    [Fact]
    public async Task AccountServices_HonorsCanceledToken()
    {
        using var scope = factory.Services.CreateScope();
        var services = CreateAccountServices(scope.ServiceProvider, new StubCurrentUser(Guid.NewGuid().ToString()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => services.RegisterGhostAccount(cts.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => services.AuthenticateByUserName("Admin", cts.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => services.Authenticate(new AuthenticationRequest { UserName = "Admin", Password = SyntheticPassword }, cts.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => services.ChangePassword(new ChangePasswordRequest(), cts.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => services.ChangeUserName(new ChangeUserNameRequest(), cts.Token));
    }

    [Fact]
    public void JwtSettingsValidator_RejectsInvalidValues_WithoutEchoingThem()
    {
        const string shortKey = "synthetic-short-key";
        var result = new JwtSettingsValidator().Validate(null, new JwtSettings
        {
            Key = shortKey,
            Issuer = " ",
            Audience = "",
            DurationInMinutes = 0
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("JwtSettings:Key");
        result.FailureMessage.ShouldContain("JwtSettings:Issuer");
        result.FailureMessage.ShouldContain("JwtSettings:Audience");
        result.FailureMessage.ShouldContain("JwtSettings:DurationInMinutes");
        result.FailureMessage.ShouldNotContain(shortKey);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(1441)]
    [InlineData(double.NaN)]
    public void JwtSettingsValidator_RejectsLifetimeOutOfRange(double minutes)
    {
        var settings = JwtSettings(factory.Services);
        var result = new JwtSettingsValidator().Validate(null, new JwtSettings
        {
            Key = settings.Key,
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            DurationInMinutes = minutes
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("JwtSettings:DurationInMinutes");
    }

    [Fact]
    public void ConfiguredTestSettings_AreValid()
    {
        new JwtSettingsValidator().Validate(null, JwtSettings(factory.Services)).Succeeded.ShouldBeTrue();
        var identity = factory.Services.GetRequiredService<IOptions<IdentitySettings>>().Value;
        new IdentitySettingsValidator().Validate(null, identity).Succeeded.ShouldBeTrue();
        factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Password.RequiredLength.ShouldBe(identity.PasswordRequiredLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(129)]
    public void IdentitySettingsValidator_RejectsInvalidPasswordLength(int length)
    {
        var result = new IdentitySettingsValidator().Validate(null, new IdentitySettings { PasswordRequiredLength = length });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("IdentitySettings:PasswordRequiredLength");
    }

    private async Task<(string UserName, IReadOnlyList<Claim> Claims)> CreateUserWithRoleAsync()
    {
        var userName = "synthetic" + Guid.NewGuid().ToString("N")[..8];
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = userName };
            (await userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
            (await userManager.AddToRoleAsync(user, "Admin")).Succeeded.ShouldBeTrue();
        }

        return (userName, await CurrentClaimsAsync(factory.Services, userName));
    }

    private static AuthenticatedUserService CurrentUser(ClaimsIdentity identity) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } });

    private static AccountServices CreateAccountServices(IServiceProvider provider, IAuthenticatedUserService currentUser) => new(
        provider.GetRequiredService<UserManager<ApplicationUser>>(),
        currentUser,
        provider.GetRequiredService<SignInManager<ApplicationUser>>(),
        provider.GetRequiredService<JwtTokenFactory>(),
        provider.GetRequiredService<ITranslator>());

    private sealed record StubCurrentUser(string? UserId) : IAuthenticatedUserService
    {
        public string? UserName => null;
    }
}
