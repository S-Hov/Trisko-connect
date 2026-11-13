using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaSubscriptionPolicyTests
{
    [Fact]
    public void DefaultInterval_ShouldBeFiveHoursInMinutes()
    {
        GeksaSubscriptionPolicy.DefaultIntervalMinutes.Should().Be(300);
    }

    [Theory]
    [InlineData(2, 300)]
    [InlineData(0, 0)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    public void Migration_ShouldOnlyReplaceOldDefault(int before, int expected)
    {
        GeksaSubscriptionPolicy.MigrateInterval(before).Should().Be(expected);
    }
}
