using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class WindowsTriskoResourceLauncherTests
{
    [Theory]
    [InlineData("user1", "user1", true)]
    [InlineData("User1", "user1", true)]
    [InlineData("CORP\\user1", "user1", true)]
    [InlineData("user1", "CORP\\user1", true)]
    [InlineData("CORP\\user1", "CORP\\user1", true)]
    [InlineData("user1@corp.local", "user1", true)]
    [InlineData("user1", "user2", false)]
    [InlineData("CORP\\user1", "CORP\\user2", false)]
    [InlineData("user1", "user10", false)]
    [InlineData("", "user1", false)]
    [InlineData(null, "user1", false)]
    public void UserNamesMatch_ShouldCompareCorrectly(
        string? existing,
        string? expected,
        bool shouldMatch)
    {
        var result = WindowsTriskoResourceLauncher.UserNamesMatch(existing, expected);
        result.Should().Be(shouldMatch);
    }

    [Theory]
    [InlineData("192.168.1.100", "user1", "TriskoConnect/RdpVault/192.168.1.100/user1")]
    [InlineData("SERVER.CORP", "DOMAIN\\User2", "TriskoConnect/RdpVault/server.corp/user2")]
    [InlineData(" 192.168.1.100 ", "admin@domain.com", "TriskoConnect/RdpVault/192.168.1.100/admin")]
    public void GetVaultTargetName_ShouldFormatPredictably(
        string host,
        string user,
        string expectedTarget)
    {
        var target = WindowsTriskoResourceLauncher.GetVaultTargetName(host, user);
        target.Should().Be(expectedTarget);
    }
}
