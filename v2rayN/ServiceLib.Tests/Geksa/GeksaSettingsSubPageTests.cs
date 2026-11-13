using System;
using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaSettingsSubPageTests
{
    [Fact]
    public void GeksaSettingsSubPage_EnumValues_ShouldExist()
    {
        Enum.GetNames<GeksaSettingsSubPage>().Should().Contain(new[]
        {
            nameof(GeksaSettingsSubPage.Main),
            nameof(GeksaSettingsSubPage.OneCSystem),
            nameof(GeksaSettingsSubPage.VpnRouting),
            nameof(GeksaSettingsSubPage.SmbAutomation),
            nameof(GeksaSettingsSubPage.NetworkInterface),
            nameof(GeksaSettingsSubPage.EcpServers),
            nameof(GeksaSettingsSubPage.Updates)
        });
    }

    [Theory]
    [InlineData(GeksaSettingsSubPage.Main, true, false, false, false, false, false, false)]
    [InlineData(GeksaSettingsSubPage.OneCSystem, false, true, false, false, false, false, false)]
    [InlineData(GeksaSettingsSubPage.VpnRouting, false, false, true, false, false, false, false)]
    [InlineData(GeksaSettingsSubPage.SmbAutomation, false, false, false, true, false, false, false)]
    [InlineData(GeksaSettingsSubPage.NetworkInterface, false, false, false, false, true, false, false)]
    [InlineData(GeksaSettingsSubPage.EcpServers, false, false, false, false, false, true, false)]
    [InlineData(GeksaSettingsSubPage.Updates, false, false, false, false, false, false, true)]
    public void SelectSettingsSubPage_ShouldUpdateFlagsCorrectly(
        GeksaSettingsSubPage targetPage,
        bool expectedMain,
        bool expectedOneC,
        bool expectedRouting,
        bool expectedSmb,
        bool expectedNetwork,
        bool expectedEcpServers,
        bool expectedUpdates)
    {
        var isMain = targetPage == GeksaSettingsSubPage.Main;
        var isOneC = targetPage == GeksaSettingsSubPage.OneCSystem;
        var isRouting = targetPage == GeksaSettingsSubPage.VpnRouting;
        var isSmb = targetPage == GeksaSettingsSubPage.SmbAutomation;
        var isNetwork = targetPage == GeksaSettingsSubPage.NetworkInterface;
        var isEcpServers = targetPage == GeksaSettingsSubPage.EcpServers;
        var isUpdates = targetPage == GeksaSettingsSubPage.Updates;

        isMain.Should().Be(expectedMain);
        isOneC.Should().Be(expectedOneC);
        isRouting.Should().Be(expectedRouting);
        isSmb.Should().Be(expectedSmb);
        isNetwork.Should().Be(expectedNetwork);
        isEcpServers.Should().Be(expectedEcpServers);
        isUpdates.Should().Be(expectedUpdates);
    }
}
