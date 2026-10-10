using Shouldly;
using Template.Application.Services.Time;

namespace Template.Unit.Tests.ApplicationTests.Services;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcInstant()
    {
        var before = DateTime.UtcNow;

        var now = new SystemClock().UtcNow;

        var after = DateTime.UtcNow;
        now.Kind.ShouldBe(DateTimeKind.Utc);
        now.ShouldBeInRange(before, after);
    }
}
