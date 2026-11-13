using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaNetworkBindingSelectorTests
{
    private static readonly IPAddress EndpointAddress = IPAddress.Parse("203.0.113.10");

    [Fact]
    public async Task SelectAsync_AutomaticMode_ShouldChooseReachableAdapterWithGateway()
    {
        var incy = Candidate("wwan99", "198.18.0.1", hasDefaultGateway: false, NetworkInterfaceType.Ethernet, 8);
        var ethernet = Candidate("Ethernet", "192.168.0.174", hasDefaultGateway: true, NetworkInterfaceType.Ethernet, 12);
        var hyperV = Candidate("vEthernet", "172.20.0.1", hasDefaultGateway: false, NetworkInterfaceType.Ethernet, 20);
        var selector = CreateSelector([incy, ethernet, hyperV], [incy, ethernet, hyperV]);

        var result = await selector.SelectAsync(
            "vpn.example.test",
            443,
            requiredInterfaceName: null,
            TestContext.Current.CancellationToken);

        result.InterfaceName.Should().Be("Ethernet");
        result.LocalAddress.Should().Be(IPAddress.Parse("192.168.0.174"));
    }

    [Fact]
    public async Task SelectAsync_ManualMode_ShouldUseSelectedAdapterWithoutGateway()
    {
        var cellular = Candidate("Mobile connection", "10.20.30.40", hasDefaultGateway: false,
            NetworkInterfaceType.Ppp, 7);
        var selector = CreateSelector([cellular], [cellular]);

        var result = await selector.SelectAsync(
            "vpn.example.test",
            443,
            cellular.InterfaceName,
            TestContext.Current.CancellationToken);

        result.InterfaceName.Should().Be(cellular.InterfaceName);
        result.LocalAddress.Should().Be(cellular.LocalAddress);
    }

    [Fact]
    public async Task SelectAsync_ManualMode_ShouldNotSilentlyUseAnotherAdapter()
    {
        var selected = Candidate("Wi-Fi", "192.168.10.20", hasDefaultGateway: true,
            NetworkInterfaceType.Wireless80211, 5);
        var ethernet = Candidate("Ethernet", "10.0.0.20", hasDefaultGateway: true,
            NetworkInterfaceType.Ethernet, 12);
        var selector = CreateSelector([selected, ethernet], [ethernet]);

        var action = () => selector.SelectAsync(
            "vpn.example.test",
            443,
            selected.InterfaceName,
            TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<GeksaVpnOperationException>();
        exception.Which.Code.Should().Be("selectedNetworkInterfaceUnreachable");
    }

    [Fact]
    public async Task SelectAsync_AutomaticMode_ShouldIgnoreReachableAdapterWithoutGateway()
    {
        var otherVpn = Candidate("Another VPN", "198.18.0.1", hasDefaultGateway: false,
            NetworkInterfaceType.Ethernet, 8);
        var selector = CreateSelector([otherVpn], [otherVpn]);

        var action = () => selector.SelectAsync(
            "vpn.example.test",
            443,
            requiredInterfaceName: null,
            TestContext.Current.CancellationToken);

        var exception = await action.Should().ThrowAsync<GeksaVpnOperationException>();
        exception.Which.Code.Should().Be("networkInterfaceUnavailable");
    }

    [Fact]
    public void GetAvailableOptions_ShouldIncludeAutomaticAndEveryActiveCandidate()
    {
        var selector = CreateSelector(
            [
                Candidate("Ethernet", "192.168.0.174", true, NetworkInterfaceType.Ethernet, 12),
                Candidate("wwan99", "198.18.0.1", false, NetworkInterfaceType.Ethernet, 8)
            ],
            []);

        var options = selector.GetAvailableOptions();

        options.Should().HaveCount(3);
        options[0].IsAutomatic.Should().BeTrue();
        options.Should().Contain(option => option.InterfaceName == "Ethernet");
        options.Should().Contain(option => option.InterfaceName == "wwan99");
    }

    private static GeksaNetworkBindingSelector CreateSelector(
        IReadOnlyList<GeksaNetworkInterfaceCandidate> candidates,
        IReadOnlyList<GeksaNetworkInterfaceCandidate> reachableCandidates)
    {
        return new GeksaNetworkBindingSelector(
            () => candidates,
            (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([EndpointAddress]),
            (candidate, _, _, _) => Task.FromResult(
                reachableCandidates.Contains(candidate)
                    ? new GeksaNetworkProbeResult(candidate, EndpointAddress)
                    : null));
    }

    private static GeksaNetworkInterfaceCandidate Candidate(
        string name,
        string localAddress,
        bool hasDefaultGateway,
        NetworkInterfaceType type,
        int index)
    {
        return new GeksaNetworkInterfaceCandidate(
            name,
            IPAddress.Parse(localAddress),
            hasDefaultGateway,
            type,
            1_000_000_000,
            index);
    }
}
