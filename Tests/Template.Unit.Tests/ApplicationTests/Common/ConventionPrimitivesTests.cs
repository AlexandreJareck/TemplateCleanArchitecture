using Shouldly;
using Template.Application.Common.Concurrency;
using Template.Application.Common.Paging;
using Template.Application.Common.Results;
using Template.Application.Settings;
using Template.Application.Wrappers;

namespace Template.Unit.Tests.ApplicationTests.Common;

/// <summary>Paging/sorting, version precondition and result primitives of docs/product/api-guidelines.md §3-§5.</summary>
public sealed class ConventionPrimitivesTests
{
    private static readonly string[] Allowed = ["occurredAt", "action"];

    [Fact]
    public void PageRequest_Defaults_AndParsesAllowListedSort()
    {
        var page = PageRequest.Create(null, null, null, Allowed, "-occurredAt");

        page.IsSuccess.ShouldBeTrue();
        page.Value.Page.ShouldBe(1);
        page.Value.PageSize.ShouldBe(20);
        page.Value.Sort.ShouldBe([new SortField("occurredAt", true)]);

        var custom = PageRequest.Create(3, 100, "action, -OCCURREDAT", Allowed, "-occurredAt").Value;
        custom.Skip.ShouldBe(200);
        custom.Sort.ShouldBe([new SortField("action", false), new SortField("occurredAt", true)]);
    }

    [Theory]
    [InlineData(0, 20, null, "page")]
    [InlineData(1, 0, null, "pageSize")]
    [InlineData(1, 101, null, "pageSize")]
    [InlineData(1, 20, "unknown", "sort")]
    [InlineData(1, 20, "action,action", "sort")]
    [InlineData(1, 20, "action,", "sort")]
    public void PageRequest_InvalidValues_AreValidationErrorsNamingTheParameter(int page, int pageSize, string? sort, string field)
    {
        var result = PageRequest.Create(page, pageSize, sort, Allowed, "-occurredAt");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe(ProblemCodes.Validation);
        result.Error.Category.ShouldBe(ErrorCategory.Validation);
        result.Error.FieldErrors!.Keys.ShouldContain(field);
    }

    [Fact]
    public void PagedResult_ComputesTotalPages()
    {
        new PagedResult<int>([], 1, 20, 0).TotalPages.ShouldBe(0);
        new PagedResult<int>([], 1, 20, 135).TotalPages.ShouldBe(7);
        new PagedResult<int>([], 1, 20, 140).TotalPages.ShouldBe(7);
    }

    [Fact]
    public void VersionPrecondition_MissingIsPreconditionRequired_StaleIsVersionMismatch()
    {
        VersionPrecondition.Check(5, 5).ShouldBeNull();

        var missing = VersionPrecondition.Check(null, 5)!;
        missing.Code.ShouldBe(ProblemCodes.PreconditionRequired);
        missing.Category.ShouldBe(ErrorCategory.PreconditionRequired);

        var stale = VersionPrecondition.Check(4, 5)!;
        stale.Code.ShouldBe(ProblemCodes.VersionMismatch);
        stale.Category.ShouldBe(ErrorCategory.PreconditionFailed);
        stale.Extensions!["version"].ShouldBe("5");
    }

    [Theory]
    [InlineData("7", true, 7)]
    [InlineData("0", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData(null, false, 0)]
    public void VersionPrecondition_ParsesOnlyPositiveIntegers(string? value, bool parsed, long expected)
    {
        VersionPrecondition.TryParse(value, out var version).ShouldBe(parsed);
        if (parsed)
            version.ShouldBe(expected);
    }

    [Fact]
    public void Result_SuccessAndFailure_AreExclusive()
    {
        Result<int> success = 3;
        Result<int> failure = ApplicationError.Validation("field", "message");

        success.IsSuccess.ShouldBeTrue();
        success.Value.ShouldBe(3);
        failure.IsSuccess.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => failure.Value);
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
    }

    [Fact]
    public void SessionSettingsValidator_AcceptsPolicyV1_AndRejectsUnsafeValues()
    {
        var validator = new SessionSettingsValidator();
        validator.Validate(null, new SessionSettings()).Succeeded.ShouldBeTrue();
        new SessionSettings().ToPolicy().ShouldBe(Template.Domain.Access.SessionPolicy.Version1);

        var invalid = validator.Validate(null, new SessionSettings
        {
            AbsoluteLifetime = TimeSpan.FromDays(91),
            IdleTimeout = TimeSpan.Zero,
            ReuseGraceWindow = TimeSpan.FromMinutes(2),
            MaxActiveFamilies = 0,
            RateLimitPermitLimit = 0,
            RateLimitWindow = TimeSpan.Zero
        });

        invalid.Failed.ShouldBeTrue();
        invalid.Failures!.Count().ShouldBe(6);
        invalid.Failures!.ShouldAllBe(message => message.StartsWith("SessionSettings:"));
    }
}
