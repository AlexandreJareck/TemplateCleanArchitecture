using Shouldly;
using Template.Application.Logging;

namespace Template.Unit.Tests.ApplicationTests.Logging;

public sealed class CorrelationIdTests
{
    [Theory]
    [InlineData("a3b8d1b6-0b3b-4b1a-9c1a-1a2b3c4d5e6f")]
    [InlineData("0HN7:00000001")]
    [InlineData("order_42.retry")]
    public void SafeValues_AreKept(string value)
    {
        CorrelationId.IsValid(value).ShouldBeTrue();
        CorrelationId.Normalize(value, "fallback").ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("<script>")]
    [InlineData("Bearer eyJhbGciOi")]
    public void UnsafeValues_AreReplaced(string? value)
    {
        CorrelationId.IsValid(value).ShouldBeFalse();
        CorrelationId.Normalize(value, "fallback").ShouldBe("fallback");
    }

    [Fact]
    public void OversizedValues_AreReplaced_AndNewIdsAreSafe()
    {
        CorrelationId.Normalize(new string('a', CorrelationId.MaxLength + 1), "fallback").ShouldBe("fallback");
        CorrelationId.IsValid(new string('a', CorrelationId.MaxLength)).ShouldBeTrue();
        var created = CorrelationId.New();
        CorrelationId.IsValid(created).ShouldBeTrue();
        created.ShouldNotBe(CorrelationId.New());
    }
}
