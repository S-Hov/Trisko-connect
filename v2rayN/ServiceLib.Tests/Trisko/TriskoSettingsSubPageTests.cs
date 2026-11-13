using System;
using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoSettingsSubPageTests
{
    [Fact]
    public void TriskoSettingsSubPage_EnumValues_ShouldExist()
    {
        Enum.GetNames<TriskoSettingsSubPage>().Should().Contain(new[]
        {
            nameof(TriskoSettingsSubPage.Main),
            nameof(TriskoSettingsSubPage.OneCSystem),
            nameof(TriskoSettingsSubPage.VpnRouting),
            nameof(TriskoSettingsSubPage.SmbAutomation),
            nameof(TriskoSettingsSubPage.NetworkInterface),
            nameof(TriskoSettingsSubPage.EcpServers),
            nameof(TriskoSettingsSubPage.Updates)
        });
    }

    [Theory]
    [InlineData(TriskoSettingsSubPage.Main, true, false, false, false, false, false, false)]
    [InlineData(TriskoSettingsSubPage.OneCSystem, false, true, false, false, false, false, false)]
    [InlineData(TriskoSettingsSubPage.VpnRouting, false, false, true, false, false, false, false)]
    [InlineData(TriskoSettingsSubPage.SmbAutomation, false, false, false, true, false, false, false)]
    [InlineData(TriskoSettingsSubPage.NetworkInterface, false, false, false, false, true, false, false)]
    [InlineData(TriskoSettingsSubPage.EcpServers, false, false, false, false, false, true, false)]
    [InlineData(TriskoSettingsSubPage.Updates, false, false, false, false, false, false, true)]
    public void SelectSettingsSubPage_ShouldUpdateFlagsCorrectly(
        TriskoSettingsSubPage targetPage,
        bool expectedMain,
        bool expectedOneC,
        bool expectedRouting,
        bool expectedSmb,
        bool expectedNetwork,
        bool expectedEcpServers,
        bool expectedUpdates)
    {
        var isMain = targetPage == TriskoSettingsSubPage.Main;
        var isOneC = targetPage == TriskoSettingsSubPage.OneCSystem;
        var isRouting = targetPage == TriskoSettingsSubPage.VpnRouting;
        var isSmb = targetPage == TriskoSettingsSubPage.SmbAutomation;
        var isNetwork = targetPage == TriskoSettingsSubPage.NetworkInterface;
        var isEcpServers = targetPage == TriskoSettingsSubPage.EcpServers;
        var isUpdates = targetPage == TriskoSettingsSubPage.Updates;

        isMain.Should().Be(expectedMain);
        isOneC.Should().Be(expectedOneC);
        isRouting.Should().Be(expectedRouting);
        isSmb.Should().Be(expectedSmb);
        isNetwork.Should().Be(expectedNetwork);
        isEcpServers.Should().Be(expectedEcpServers);
        isUpdates.Should().Be(expectedUpdates);
    }
}
