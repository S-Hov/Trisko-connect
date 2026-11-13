using ServiceLib.Geksa;
using ServiceLib.Services.CoreConfig;
using ServiceLib.Tests.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaVpnTestConfigurationTests
{
    private static readonly GeksaNetworkInterfaceCandidate Ethernet = new(
        "Ethernet", IPAddress.Parse("192.168.0.206"), true, NetworkInterfaceType.Ethernet, 1_000_000_000, 12);

    private static GeksaNetworkBindingSelector Selector(params GeksaNetworkInterfaceCandidate[] candidates) => new(
        () => candidates,
        (_, _) => throw new InvalidOperationException("Test setup must not resolve a single endpoint"),
        (_, _, _, _) => throw new InvalidOperationException("Test setup must not reject UDP-only or offline profiles"));

    private static Config StaleConfig()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.CoreBasicItem.BindInterface = "Wi-Fi";
        config.CoreBasicItem.SendThrough = "10.169.120.251";
        config.TunModeItem.EnableTun = true;
        config.TunModeItem.GeksaAutoSelectInterface = true;
        return config;
    }

    [Fact]
    public void AutomaticTestsRefreshAdapterAndIpWithoutChangingLiveVpnConfiguration()
    {
        var config = StaleConfig();
        var virtualAdapter = Ethernet with { InterfaceName = "happ-xray", LocalAddress = IPAddress.Parse("172.19.0.1"), HasDefaultGateway = false };
        var snapshot = Selector(virtualAdapter, Ethernet).CreateSpeedtestConfiguration(config);

        Assert.Equal("Ethernet", snapshot.CoreBasicItem.BindInterface);
        Assert.Equal("192.168.0.206", snapshot.CoreBasicItem.SendThrough);
        Assert.False(snapshot.TunModeItem.EnableTun);
        Assert.Equal("Wi-Fi", config.CoreBasicItem.BindInterface);
        Assert.Equal("10.169.120.251", config.CoreBasicItem.SendThrough);
        Assert.True(config.TunModeItem.EnableTun);
        Assert.NotSame(config.CoreBasicItem, snapshot.CoreBasicItem);
    }

    [Fact]
    public void ManualTestsRespectSelectedAdapterAndRefreshItsAddress()
    {
        var config = StaleConfig();
        config.TunModeItem.GeksaAutoSelectInterface = false;
        config.TunModeItem.GeksaPreferredInterface = "Wi-Fi";
        var wifi = Ethernet with { InterfaceName = "Wi-Fi", LocalAddress = IPAddress.Parse("192.168.1.50") };

        var snapshot = Selector(Ethernet, wifi).CreateSpeedtestConfiguration(config);

        Assert.Equal("Wi-Fi", snapshot.CoreBasicItem.BindInterface);
        Assert.Equal("192.168.1.50", snapshot.CoreBasicItem.SendThrough);
    }

    [Fact]
    public void UnavailableManualAdapterProducesSpecificErrorInsteadOfSilentFallback()
    {
        var config = StaleConfig();
        config.TunModeItem.GeksaAutoSelectInterface = false;
        config.TunModeItem.GeksaPreferredInterface = "Wi-Fi";

        var error = Assert.Throws<GeksaVpnOperationException>(() => Selector(Ethernet).CreateSpeedtestConfiguration(config));

        Assert.Equal("selectedNetworkInterfaceUnavailable", error.Code);
        Assert.Contains("Wi-Fi", error.UserMessage);
    }

    [Fact]
    public void AutomaticTestsDoNotUseLinkLocalOrVirtualAdapterWithoutGateway()
    {
        var config = StaleConfig();
        var linkLocal = Ethernet with { InterfaceName = "Wi-Fi", LocalAddress = IPAddress.Parse("169.254.23.108") };
        var virtualAdapter = Ethernet with { InterfaceName = "vEthernet", HasDefaultGateway = false };

        var error = Assert.Throws<GeksaVpnOperationException>(() => Selector(linkLocal, virtualAdapter).CreateSpeedtestConfiguration(config));

        Assert.Equal("networkInterfaceUnavailable", error.Code);
    }

    [Theory]
    [InlineData(ECoreType.Xray, false)]
    [InlineData(ECoreType.Xray, true)]
    [InlineData(ECoreType.sing_box, false)]
    [InlineData(ECoreType.sing_box, true)]
    public void GeneratedSingleAndBatchTestsUseFreshBinding(ECoreType core, bool batch)
    {
        var config = Selector(Ethernet).CreateSpeedtestConfiguration(StaleConfig());
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(core);
        var context = CoreConfigTestFactory.CreateContext(config, node, core);
        var tests = new List<ServerTestItem>
        {
            new() { IndexId = node.IndexId, Profile = node, ConfigType = node.ConfigType, CoreType = core, Port = node.Port }
        };
        var result = core == ECoreType.Xray
            ? batch ? new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(tests)
                    : new CoreConfigV2rayService(context).GenerateClientSpeedtestConfig(21936)
            : batch ? new CoreConfigSingboxService(context).GenerateClientSpeedtestConfig(tests)
                    : new CoreConfigSingboxService(context).GenerateClientSpeedtestConfig(21936);

        Assert.True(result.Success, result.Msg);
        var json = JsonNode.Parse(result.Data.ToString())!;
        var proxy = json["outbounds"]!.AsArray().First(outbound => outbound?[core == ECoreType.Xray ? "protocol" : "type"]?.ToString() == "vmess")!;
        Assert.Equal("Ethernet", core == ECoreType.Xray
            ? proxy["streamSettings"]?["sockopt"]?["interface"]?.ToString() : proxy["bind_interface"]?.ToString());
        Assert.Equal("192.168.0.206", proxy[core == ECoreType.Xray ? "sendThrough" : "inet4_bind_address"]?.ToString());
        Assert.DoesNotContain("10.169.120.251", json.ToJsonString());
    }
}
