using AwesomeAssertions;
using ServiceLib.Trisko;
using ServiceLib.Models.Configs;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class WindowsTriskoVirtualHereServiceTests
{
    [Fact]
    public void UiItem_EcpDefaults_ShouldEnableVirtualHereStartupAndScanEveryFiveSeconds()
    {
        var uiItem = new UIItem();

        uiItem.TriskoEcpAutoScanEnabled.Should().BeTrue();
        uiItem.TriskoRouteVirtualHereThroughVpn.Should().BeTrue();
        uiItem.TriskoAutoStartVirtualHereOnLaunch.Should().BeFalse();
        uiItem.TriskoEcpAutoScanIntervalSeconds.Should().Be(5);
        uiItem.TriskoIsSidebarCollapsed.Should().BeFalse();
    }

    [Theory]
    [InlineData("192.168.3.222", "7575", "192.168.3.222:7575")]
    [InlineData("usb-server.local", "17570", "usb-server.local:17570")]
    [InlineData("2001:db8::10", "7575", "[2001:db8::10]:7575")]
    [InlineData("[2001:db8::10]", "7575", "[2001:db8::10]:7575")]
    public void HubConfiguration_ValidInput_ShouldCreateEndpoint(
        string host,
        string port,
        string expectedEndpoint)
    {
        var success = TriskoVirtualHereHubConfiguration.TryCreate(
            host,
            port,
            out var configuration,
            out var error);

        success.Should().BeTrue(error);
        configuration.Endpoint.Should().Be(expectedEndpoint);
    }

    [Theory]
    [InlineData("", "7575")]
    [InlineData("https://server", "7575")]
    [InlineData("server:7575", "7575")]
    [InlineData("not:an:ipv6", "7575")]
    [InlineData("[2001:db8::10", "7575")]
    [InlineData("server", "0")]
    [InlineData("server", "65536")]
    public void HubConfiguration_InvalidInput_ShouldReturnValidationError(string host, string port)
    {
        var success = TriskoVirtualHereHubConfiguration.TryCreate(
            host,
            port,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ManualHub_WithConfiguredIntegrationEndpoint_ShouldRemoveAndAddAgain()
    {
        var clientPath = Environment.GetEnvironmentVariable("TRISKO_VIRTUALHERE_TEST_CLIENT");
        var endpoint = Environment.GetEnvironmentVariable("TRISKO_VIRTUALHERE_TEST_HUB");
        if (!OperatingSystem.IsWindows() ||
            string.IsNullOrWhiteSpace(clientPath) ||
            string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        using var service = new WindowsTriskoVirtualHereService(clientPath);
        try
        {
            await service.ScanAsync(TestContext.Current.CancellationToken);
            await service.AddManualHubAsync(endpoint, TestContext.Current.CancellationToken);
            await service.RemoveManualHubAsync(endpoint, TestContext.Current.CancellationToken);
            await service.AddManualHubAsync(endpoint, TestContext.Current.CancellationToken);
        }
        finally
        {
            await service.RemoveManualHubAsync(endpoint, TestContext.Current.CancellationToken);
            await service.ShutdownAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void SetGeneralSetting_ShouldAddAndUpdateBackgroundSettings()
    {
        var lines = new List<string>
        {
            "[General]",
            "AutoMinimize=0",
            "AutoFind=0",
            "[Settings]",
            "ManualHubs="
        };

        WindowsTriskoVirtualHereService.SetGeneralSetting(lines, "AutoMinimize", "1");
        WindowsTriskoVirtualHereService.SetGeneralSetting(lines, "Language", "RU-RU");

        lines.Should().Contain("AutoMinimize=1");
        lines.Should().Contain("Language=RU-RU");
        lines.IndexOf("Language=RU-RU").Should().BeLessThan(lines.IndexOf("[Settings]"));
    }

    [Fact]
    public async Task ScanAsync_WithConfiguredClient_ShouldReadLiveNamedPipe()
    {
        var clientPath = Environment.GetEnvironmentVariable("TRISKO_VIRTUALHERE_TEST_CLIENT");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(clientPath))
        {
            return;
        }

        using var service = new WindowsTriskoVirtualHereService(clientPath);
        var devices = await service.ScanAsync(TestContext.Current.CancellationToken);

        devices.Should().NotBeNull();
    }

    [Fact]
    public void ParseClientState_ShouldMapAvailableLocalAndRemoteDevices()
    {
        var xml = $$"""
            <state>
              <server>
                <connection serverName="Office USB" hostname="usb-host" port="7575" uuid="local-uuid" />
                <device vendor="Rutoken" product="ECP" address="11" state="1" nickname="Signature 1" deviceSerial="ABC" firstInterfaceClass="8" autoUse="not-set" boundConnectionUUID="empty" boundClientHostname="" clientId="" />
                <device vendor="JaCarta" product="Token" address="12" state="3" nickname="" deviceSerial="DEF" firstInterfaceClass="0" autoUse="device" boundConnectionUUID="local-uuid" boundClientHostname="{{Environment.MachineName}}" clientId="local-user" />
                <device vendor="Aladdin" product="Token" address="13" state="3" nickname="" deviceSerial="GHI" firstInterfaceClass="0" autoUse="port" boundConnectionUUID="remote-uuid" boundClientHostname="remote-pc" clientId="employee" />
              </server>
            </state>
            """;

        var devices = WindowsTriskoVirtualHereService.ParseClientState(xml);

        devices.Should().HaveCount(3);
        devices.Single(device => device.Address == "usb-host.11").State
            .Should().Be(TriskoVirtualUsbState.Available);
        devices.Single(device => device.Address == "usb-host.12").State
            .Should().Be(TriskoVirtualUsbState.InUseLocally);
        devices.Single(device => device.Address == "usb-host.13").State
            .Should().Be(TriskoVirtualUsbState.InUseRemotely);
        devices.Single(device => device.Address == "usb-host.13").InUseBy
            .Should().Be("employee");
        devices[0].DisplayName.Should().Be("Signature 1");
    }

    [Fact]
    public void ParseClientState_InvalidXml_ShouldReturnUserFacingError()
    {
        var action = () => WindowsTriskoVirtualHereService.ParseClientState("not xml");

        action.Should().Throw<TriskoVirtualHereException>()
            .WithMessage("VirtualHere вернул некорректный список USB-устройств.");
    }
}
