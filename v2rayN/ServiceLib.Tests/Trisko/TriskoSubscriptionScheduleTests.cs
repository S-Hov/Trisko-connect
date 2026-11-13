using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoSubscriptionScheduleTests
{
    [Fact]
    public void DefaultInterval_ShouldBeFiveHoursInMinutes()
    {
        TriskoSubscriptionPolicy.DefaultIntervalMinutes.Should().Be(300);
    }

    [Theory]
    [InlineData(2, 300)]
    [InlineData(0, 0)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    public void Migration_ShouldOnlyReplaceOldDefault(int before, int expected)
    {
        TriskoSubscriptionPolicy.MigrateInterval(before).Should().Be(expected);
    }
}
