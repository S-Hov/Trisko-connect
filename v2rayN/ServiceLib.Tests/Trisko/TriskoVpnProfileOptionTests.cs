using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Trisko;
using ServiceLib.Models.Dto;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoVpnProfileOptionTests
{
    [Fact]
    public void From_ShouldExposeProfileProtocolAndSubscription()
    {
        var profile = new ProfileItemModel
        {
            IndexId = "profile-1",
            Remarks = "Москва · основной",
            ConfigType = EConfigType.Hysteria2,
            SubRemarks = "Корпоративная подписка",
            Address = "vpn.example.test",
            Port = 8443
        };

        var option = TriskoVpnProfileOption.From(profile);

        option.IndexId.Should().Be("profile-1");
        option.DisplayName.Should().Be("Москва · основной");
        option.ProtocolName.Should().Be("Hysteria2");
        option.DetailsText.Should().Be("Hysteria2 · Корпоративная подписка");
        option.EndpointText.Should().Be("vpn.example.test:8443");
    }

    [Fact]
    public void From_ShouldFallBackToAddressWhenProfileHasNoName()
    {
        var profile = new ProfileItemModel
        {
            IndexId = "profile-2",
            Remarks = " ",
            Address = "vpn.example.test",
            ConfigType = EConfigType.VLESS
        };

        var option = TriskoVpnProfileOption.From(profile);

        option.DisplayName.Should().Be("vpn.example.test");
        option.DetailsText.Should().Be("VLESS");
    }

    [Theory]
    [InlineData("Trisko Cascade №1|📊100.00GB", "Trisko Cascade №1")]
    [InlineData("Moscow ｜ Traffic 42 GB", "Moscow")]
    [InlineData("Frankfurt|обычное имя", "Frankfurt|обычное имя")]
    public void From_ShouldRemoveSubscriptionMetadataFromVisibleServerName(string source, string expected)
    {
        var profile = new ProfileItemModel
        {
            IndexId = "metadata-profile",
            Remarks = source,
            ConfigType = EConfigType.VLESS
        };

        TriskoVpnProfileOption.From(profile).DisplayName.Should().Be(expected);
    }

    [Fact]
    public void From_RunningProfile_ShouldKeepMetadataOutOfConnectedServerHeading()
    {
        var running = new RunningProfileInfo(
            "running",
            "Trisko Cascade №1|📊100.00GB",
            EConfigType.VLESS,
            "vpn.example.test",
            443,
            ECoreType.Xray);

        TriskoVpnProfileOption.From(running).DisplayName.Should().Be("Trisko Cascade №1");
    }

    [Fact]
    public void CreateAutomatic_ShouldDescribeAutomaticSelectionAndTrackDelayState()
    {
        var option = TriskoVpnProfileOption.CreateAutomatic();

        option.IsAutomatic.Should().BeTrue();
        option.IndexId.Should().Be(TriskoVpnProfileOption.AutomaticIndexId);
        option.EndpointText.Should().Contain("сервер перед подключением");

        option.MarkTesting();
        option.IsTesting.Should().BeTrue();
        option.DelayText.Should().Be("Проверка…");
        option.ApplyDelay(42);
        option.IsTesting.Should().BeFalse();
        option.DelayText.Should().Be("42 мс");
    }

    [Theory]
    [InlineData("Cascade AnyDesk Vienna", false)]
    [InlineData("anydesk eu CASCADE", false)]
    [InlineData("Cascade Vienna", false)]
    [InlineData("AnyDesk Vienna", false)]
    [InlineData("Обычный сервер", false)]
    public void From_TreatsEveryServerAsAnOrdinaryVpnProfile(string name, bool expected)
    {
        var profile = new ProfileItemModel
        {
            IndexId = Guid.NewGuid().ToString(),
            Remarks = name,
            ConfigType = EConfigType.VLESS,
            Address = "vpn.example.test",
            Port = 443
        };

        TriskoVpnProfileOption.From(profile).IsAnyDesk.Should().Be(expected);
    }



    [Fact]
    public void MarkTestingAndApplyDelay_ShouldUpdateIsTestingState()
    {
        var option = TriskoVpnProfileOption.CreateAutomatic();
        option.IsTesting.Should().BeFalse();

        option.MarkTesting();
        option.IsTesting.Should().BeTrue();
        option.DelayText.Should().Be("Проверка…");

        option.ApplyDelay(85);
        option.IsTesting.Should().BeFalse();
        option.DelayText.Should().Be("85 мс");
        option.DelayColor.Should().Be("#3FD69A");

        option.ApplyDelay(-1);
        option.IsTesting.Should().BeFalse();
        option.DelayText.Should().Be("Нет ответа");
        option.DelayColor.Should().Be("#F0717E");
    }

    [Fact]
    public void RefreshTheme_ShouldSwitchBetweenDarkAndLightColors()
    {
        var option = TriskoVpnProfileOption.CreateAutomatic();
        option.ApplyDelay(85);
        option.DelayColor.Should().Be("#3FD69A");

        option.RefreshTheme(TriskoThemeMode.Light);
        option.DelayColor.Should().Be("#059669");

        option.RefreshTheme(TriskoThemeMode.Dark);
        option.DelayColor.Should().Be("#3FD69A");

        option.ApplyDelay(-1);
        option.RefreshTheme(TriskoThemeMode.Light);
        option.DelayColor.Should().Be("#DC2626");

        option.RefreshTheme(TriskoThemeMode.Brand);
        option.DelayColor.Should().Be("#F0717E");
    }
}
