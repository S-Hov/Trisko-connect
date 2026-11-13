using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoCorporateRoutingTargetProviderTests
{
    [Fact]
    public void UiItem_VirtualHereRouting_ShouldBeEnabledForEveryModeByDefault()
    {
        var uiItem = new UIItem();

        foreach (var mode in Enum.GetValues<TriskoVpnRoutingMode>())
        {
            TriskoCorporateRoutingTargetProvider
                .ShouldIncludeVirtualHereHubs(uiItem, mode)
                .Should()
                .BeTrue();
        }
    }

    [Fact]
    public void ShouldIncludeVirtualHereHubs_ShouldRespectMasterAndCurrentModeSwitches()
    {
        var uiItem = new UIItem
        {
            TriskoRouteVirtualHereThroughVpn = true,
            TriskoRouteVirtualHereInCorporateOnly = false,
            TriskoRouteVirtualHereInRussianSites = true,
            TriskoRouteVirtualHereInGlobalExceptLocal = true,
            TriskoRouteVirtualHereInGlobal = true,
        };

        TriskoCorporateRoutingTargetProvider.ShouldIncludeVirtualHereHubs(
                uiItem,
                TriskoVpnRoutingMode.CorporateOnly)
            .Should()
            .BeFalse();
        TriskoCorporateRoutingTargetProvider.ShouldIncludeVirtualHereHubs(
                uiItem,
                TriskoVpnRoutingMode.RussianSites)
            .Should()
            .BeTrue();

        uiItem.TriskoRouteVirtualHereThroughVpn = false;
        foreach (var mode in Enum.GetValues<TriskoVpnRoutingMode>())
        {
            TriskoCorporateRoutingTargetProvider
                .ShouldIncludeVirtualHereHubs(uiItem, mode)
                .Should()
                .BeFalse();
        }
    }

    [Fact]
    public void FromResources_ShouldProxyOnlyVpnResourcesAndDeduplicateTargets()
    {
        var resources = new[]
        {
            Resource("smb", "192.168.3.230", requiresVpn: true),
            Resource("smb-copy", "192.168.3.230", requiresVpn: true),
            Resource("rdp", "192.168.3.8", requiresVpn: true),
            Resource("ssh", "srv.corp.example", requiresVpn: true),
            Resource("public", "45.87.142.182", requiresVpn: false),
        };

        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            resources,
            ["192.168.3.222"]);

        targets.IpCidrs.Should().BeEquivalentTo([
            "192.168.3.222/32",
            "192.168.3.230/32",
            "192.168.3.8/32",
        ]);
        targets.Domains.Should().BeEquivalentTo(["srv.corp.example"]);
    }

    [Fact]
    public void FromResources_ShouldPreferExactHostRoutesOverBroadPrivateNetworks()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources([
            Resource("office", "192.168.0.172", requiresVpn: true),
        ]);

        targets.IpCidrs.Should().ContainSingle().Which.Should().Be("192.168.0.172/32");
        targets.IpCidrs.Should().NotContain("192.168.0.0/16");
    }

    [Fact]
    public void FromResources_ShouldNotContainHardcodedRoutes()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources([]);

        targets.IpCidrs.Should().BeEmpty();
        targets.Domains.Should().BeEmpty();
        targets.SystemRouteCidrs.Should().BeEmpty();
    }

    [Fact]
    public void FromResources_ShouldAddConfiguredOneCAddressWithoutDuplicates()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            ["192.168.3.222"],
            TriskoVpnRoutingMode.CorporateOnly,
            oneCServerAddress: "192.168.3.222");

        targets.IpCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.3.222/32");
        targets.SystemRouteCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.3.222/32");
    }

    [Fact]
    public void FromResources_ShouldIgnoreInvalidOneCAddress()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            oneCServerAddress: "not-an-ip");

        targets.IpCidrs.Should().BeEmpty();
    }

    [Fact]
    public void FromResources_CorporateOnly_ShouldCreateOnlyExactSystemRoutes()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [Resource("rdp", "192.168.3.8", requiresVpn: true)],
            ["192.168.3.222", "10.40.0.0/16"],
            TriskoVpnRoutingMode.CorporateOnly);

        targets.SystemRouteCidrs.Should().BeEquivalentTo([
            "10.40.0.0/16",
            "192.168.3.8/32",
            "192.168.3.222/32",
        ]);
    }

    [Fact]
    public void FromResources_GlobalExceptLocal_ShouldProxyPublicAndExactCorporatePrivateAddresses()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            ["192.168.3.222"],
            TriskoVpnRoutingMode.GlobalExceptLocal);

        Covers(targets.SystemRouteCidrs, "8.8.8.8").Should().BeTrue();
        Covers(targets.SystemRouteCidrs, "192.168.3.221").Should().BeFalse();
        Covers(targets.SystemRouteCidrs, "192.168.3.222").Should().BeTrue();
        Covers(targets.SystemRouteCidrs, "169.254.10.1").Should().BeFalse();
    }

    [Fact]
    public void FromResources_Global_ShouldRouteBothAddressFamiliesCompletely()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            mode: TriskoVpnRoutingMode.Global);

        targets.SystemRouteCidrs.Should().BeEquivalentTo(["0.0.0.0/0", "::/0"]);
    }

    [Fact]
    public void FromResources_RussianSites_ShouldExposeSuffixesAndCaptureTrafficForClassification()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [Resource("office", "192.168.3.8", requiresVpn: true)],
            mode: TriskoVpnRoutingMode.RussianSites);

        targets.SystemRouteCidrs.Should().BeEquivalentTo(["0.0.0.0/0", "::/0"]);
        targets.IpCidrs.Should().Contain("192.168.3.8/32");
        targets.DomainSuffixes.Should().BeEquivalentTo([
            "ru",
            "su",
            "xn--p1ai",
            "xn--p1acf",
            "xn--80adxhks",
            "moscow",
            "xn--d1acj3b",
            "tatar",
            "xn--80asehdb",
            "xn--80aswg",
        ]);
    }

    [Fact]
    public void FromResources_RussianSites_ShouldTreatVirtualHereHubsAsCorporateTargets()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            mode: TriskoVpnRoutingMode.RussianSites,
            virtualHereHubs:
            [
                VirtualHereHub("192.168.3.230"),
                VirtualHereHub("ecp.corp.example"),
                VirtualHereHub("fd00::230"),
                VirtualHereHub("192.168.3.230"),
            ]);

        targets.IpCidrs.Should().BeEquivalentTo([
            "192.168.3.230/32",
            "fd00::230/128",
        ]);
        targets.Domains.Should().ContainSingle()
            .Which.Should().Be("ecp.corp.example");
        targets.SystemRouteCidrs.Should().BeEquivalentTo(["0.0.0.0/0", "::/0"]);
    }

    [Theory]
    [InlineData(TriskoVpnRoutingMode.CorporateOnly)]
    [InlineData(TriskoVpnRoutingMode.GlobalExceptLocal)]
    [InlineData(TriskoVpnRoutingMode.Global)]
    public void FromResources_AllModes_ShouldTreatVirtualHereHubsAsCorporateTargets(
        TriskoVpnRoutingMode mode)
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            mode: mode,
            virtualHereHubs: [VirtualHereHub("192.168.3.230")]);

        targets.IpCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.3.230/32");
    }

    [Fact]
    public void FromResources_CorporateOnly_ShouldAddVirtualHereHubToSystemRoutes()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            mode: TriskoVpnRoutingMode.CorporateOnly,
            virtualHereHubs: [VirtualHereHub("192.168.0.172")]);

        targets.SystemRouteCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.0.172/32");
    }

    [Fact]
    public void FromResources_GlobalExceptLocal_ShouldOverrideLocalExclusionForVirtualHereHub()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            mode: TriskoVpnRoutingMode.GlobalExceptLocal,
            virtualHereHubs: [VirtualHereHub("192.168.0.172")]);

        Covers(targets.SystemRouteCidrs, "192.168.0.172").Should().BeTrue();
        Covers(targets.SystemRouteCidrs, "192.168.0.173").Should().BeFalse();
    }

    [Fact]
    public void FromResources_DisabledVirtualHereRouting_ShouldIgnoreConfiguredHubs()
    {
        var targets = TriskoCorporateRoutingTargetProvider.FromResources(
            [],
            mode: TriskoVpnRoutingMode.RussianSites,
            virtualHereHubs: [VirtualHereHub("192.168.3.230")],
            includeVirtualHereHubs: false);

        targets.IpCidrs.Should().BeEmpty();
        targets.Domains.Should().BeEmpty();
    }

    private static bool Covers(IEnumerable<string> routes, string address)
    {
        var parsedAddress = IPAddress.Parse(address);
        return routes
            .Select(IPNetwork2.Parse)
            .Any(network => network.Contains(parsedAddress));
    }

    private static TriskoResourceProfile Resource(string id, string host, bool requiresVpn) => new()
    {
        Id = id,
        Type = "ssh",
        DisplayName = id,
        Host = host,
        Port = 22,
        UserName = "user",
        RequiresVpn = requiresVpn,
    };

    private static TriskoVirtualHereHubConfiguration VirtualHereHub(string host) => new()
    {
        Host = host,
        Port = 7575,
    };
}
