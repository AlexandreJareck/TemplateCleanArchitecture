using Shouldly;
using Template.Application.DTOs.Account.Request;
using Template.Application.DTOs.Account.Response;
using Template.Application.Wrappers;
using Template.Functional.Tests.Common;

namespace Template.Functional.Tests.ApiEndpoints;

[Collection("AccountFunctionalTests")]
public class AccountFunctionalTests(CustomWebApplicationFactory<Program> factory) : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Authenticate_WithInvalidCredentials_ShouldReturnBadRequest()
    {
        // Arrange
        var url = ApiRoutes.V1.Account.Authenticate;
        var model = new AuthenticationRequest()
        {
            UserName = "definable",
            Password = "12365478"
        };

        // Act
        var result = await client.PostAndDeserializeAsync<BaseResult<AuthenticationResponse>>(url, model);

        // Assert
        result.Success.ShouldBeFalse();
        result.Errors.ShouldNotBeNull();
        result.Errors[0].ErrorCode.ShouldBe(ErrorCode.ModelStateNotValid);
    }

    [Fact]
    public async Task Authenticate_WithInvalidCredentials_ShouldReturnAccountNotFound()
    {
        // Arrange
        var url = ApiRoutes.V1.Account.Authenticate;
        var model = new AuthenticationRequest()
        {
            UserName = "Adm_i_x",
            Password = "Adm@123456"
        };

        // Act
        var result = await client.PostAndDeserializeAsync<BaseResult<AuthenticationResponse>>(url, model);

        // Assert
        result.Success.ShouldBeFalse();
        result.Errors.ShouldNotBeNull();
        result.Errors[0].ErrorCode.ShouldBe(ErrorCode.NotFound);
    }

    [Fact]
    public async Task Authenticate_WithValidCredentials_ShouldReturnAccountInformation()
    {
        // Arrange
        var url = ApiRoutes.V1.Account.Authenticate;
        var model = new AuthenticationRequest()
        {
            UserName = "Admin",
            Password = "Adm@12345"
        };

        // Act
        var result = await client.PostAndDeserializeAsync<BaseResult<AuthenticationResponse>>(url, model);

        // Assert
        result.Success.ShouldBeTrue();
        result.Errors.ShouldBeNull();
        result.Data.JwToken.ShouldNotBeNull();
        result.Data.UserName.ShouldBe(model.UserName);
        result.Data.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task StartGhostAccount_ShouldReturnGhostAccountInformation()
    {
        // Arrange

        var url = ApiRoutes.V1.Account.Start;

        // Act
        var result = await client.PostAndDeserializeAsync<BaseResult<AuthenticationResponse>>(url);

        // Assert
        result.Success.ShouldBeTrue();
        result.Errors.ShouldBeNull();
        result.Data.JwToken.ShouldNotBeNull();
        result.Data.IsVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangePassword_ShouldSucceed()
    {
        // Arrange
        var ghostAccount = await client.GetGhostAccount();

        var url = ApiRoutes.V1.Account.ChangePassword;
        var model = new ChangePasswordRequest()
        {
            Password = "Adm@7654321",
            ConfirmPassword = "Adm@7654321"
        };

        // Act
        var result = await client.PutAndDeserializeAsync<BaseResult>(url, model, ghostAccount.JwToken);

        // Assert
        result.Success.ShouldBeTrue();
        result.Errors.ShouldBeNull();
    }

    [Fact]
    public async Task ChangeUserName_ShouldSucceed()
    {
        // Arrange
        var ghostAccount = await client.GetGhostAccount();

        var url = ApiRoutes.V1.Account.ChangeUserName;
        var model = new ChangeUserNameRequest()
        {
            UserName = "TestUserName" + ghostAccount.UserName
        };

        // Act
        var result = await client.PutAndDeserializeAsync<BaseResult>(url, model, ghostAccount.JwToken);

        // Assert
        result.Success.ShouldBeTrue();
        result.Errors.ShouldBeNull();
    }
}
