using Shouldly;
using Template.Application.Wrappers;

namespace Template.Unit.Tests.ApplicationTests.Wrappers;

public class ErrorCategoryTests
{
    [Theory]
    [InlineData(ErrorCode.ModelStateNotValid, ErrorCategory.Validation)]
    [InlineData(ErrorCode.FieldDataInvalid, ErrorCategory.Validation)]
    [InlineData(ErrorCode.NotFound, ErrorCategory.NotFound)]
    [InlineData(ErrorCode.AccessDenied, ErrorCategory.Forbidden)]
    [InlineData(ErrorCode.ErrorInIdentity, ErrorCategory.Unauthorized)]
    [InlineData(ErrorCode.Exception, ErrorCategory.Unexpected)]
    public void ToCategory_MapsEveryPublicErrorCode(ErrorCode errorCode, ErrorCategory expected)
    {
        errorCode.ToCategory().ShouldBe(expected);
        new Error(errorCode).GetCategory().ShouldBe(expected);
    }

    [Fact]
    public void ToCategory_UnknownErrorCode_FallsBackToUnexpected()
    {
        ((ErrorCode)999).ToCategory().ShouldBe(ErrorCategory.Unexpected);
    }

    [Fact]
    public void ErrorCode_PublicNumericValuesAreUnchanged()
    {
        // Public contract: these values are serialized in HTTP responses and must not be renumbered.
        ((short)ErrorCode.ModelStateNotValid).ShouldBe((short)0);
        ((short)ErrorCode.FieldDataInvalid).ShouldBe((short)1);
        ((short)ErrorCode.NotFound).ShouldBe((short)2);
        ((short)ErrorCode.AccessDenied).ShouldBe((short)3);
        ((short)ErrorCode.ErrorInIdentity).ShouldBe((short)4);
        ((short)ErrorCode.Exception).ShouldBe((short)5);
        Enum.GetValues<ErrorCode>().Length.ShouldBe(6);
    }

    [Fact]
    public void GetErrorCategory_SuccessfulResult_ReturnsNull()
    {
        BaseResult.Ok().GetErrorCategory().ShouldBeNull();
    }

    [Fact]
    public void GetErrorCategory_FailureWithoutErrors_ReturnsUnexpected()
    {
        BaseResult.Failure().GetErrorCategory().ShouldBe(ErrorCategory.Unexpected);
    }

    [Fact]
    public void GetErrorCategory_UsesFirstErrorLikeTheHttpBoundary()
    {
        var result = BaseResult.Failure([new Error(ErrorCode.NotFound), new Error(ErrorCode.Exception)]);

        result.GetErrorCategory().ShouldBe(ErrorCategory.NotFound);
    }

    [Fact]
    public void GetErrorCategory_GenericAndPagedResults_AreSupported()
    {
        BaseResult<string> generic = new Error(ErrorCode.FieldDataInvalid);
        PagedResponse<string> paged = new Error(ErrorCode.ErrorInIdentity);

        generic.GetErrorCategory().ShouldBe(ErrorCategory.Validation);
        paged.GetErrorCategory().ShouldBe(ErrorCategory.Unauthorized);
    }
}
