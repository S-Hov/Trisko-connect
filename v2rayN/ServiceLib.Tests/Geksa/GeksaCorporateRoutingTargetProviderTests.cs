using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaCorporateRoutingTargetProviderTests
{
    [Fact]
    public void UiItem_VirtualHereRouting_ShouldBeEnabledForEveryModeByDefault()
    {
        var uiItem = new UIItem();

        foreach (var mode in Enum.GetValues<GeksaVpnRoutingMode>())
        {
            GeksaCorporateRoutingTargetProvider
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
            GeksaRouteVirtualHereThroughVpn = true,
            GeksaRouteVirtualHereInCorporateOnly = false,
            GeksaRouteVirtualHereInRussianSites = true,
            GeksaRouteVirtualHereInGlobalExceptLocal = true,
            GeksaRouteVirtualHereInGlobal = true,
        };

        GeksaCorporateRoutingTargetProvider.ShouldIncludeVirtualHereHubs(
                uiItem,
                GeksaVpnRoutingMode.CorporateOnly)
            .Should()
            .BeFalse();
        GeksaCorporateRoutingTargetProvider.ShouldIncludeVirtualHereHubs(
                uiItem,
                GeksaVpnRoutingMode.RussianSites)
            .Should()
            .BeTrue();

        uiItem.GeksaRouteVirtualHereThroughVpn = false;
        foreach (var mode in Enum.GetValues<GeksaVpnRoutingMode>())
        {
            GeksaCorporateRoutingTargetProvider
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

        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
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
        var targets = GeksaCorporateRoutingTargetProvider.FromResources([
            Resource("office", "192.168.0.172", requiresVpn: true),
        ]);

        targets.IpCidrs.Should().ContainSingle().Which.Should().Be("192.168.0.172/32");
        targets.IpCidrs.Should().NotContain("192.168.0.0/16");
    }

    [Fact]
    public void FromResources_ShouldNotContainHardcodedRoutes()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources([]);

        targets.IpCidrs.Should().BeEmpty();
        targets.Domains.Should().BeEmpty();
        targets.SystemRouteCidrs.Should().BeEmpty();
    }

    [Fact]
    public void FromResources_ShouldAddConfiguredOneCAddressWithoutDuplicates()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            ["192.168.3.222"],
            GeksaVpnRoutingMode.CorporateOnly,
            oneCServerAddress: "192.168.3.222");

        targets.IpCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.3.222/32");
        targets.SystemRouteCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.3.222/32");
    }

    [Fact]
    public void FromResources_ShouldIgnoreInvalidOneCAddress()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            oneCServerAddress: "not-an-ip");

        targets.IpCidrs.Should().BeEmpty();
    }

    [Fact]
    public void FromResources_CorporateOnly_ShouldCreateOnlyExactSystemRoutes()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [Resource("rdp", "192.168.3.8", requiresVpn: true)],
            ["192.168.3.222", "10.40.0.0/16"],
            GeksaVpnRoutingMode.CorporateOnly);

        targets.SystemRouteCidrs.Should().BeEquivalentTo([
            "10.40.0.0/16",
            "192.168.3.8/32",
            "192.168.3.222/32",
        ]);
    }

    [Fact]
    public void FromResources_GlobalExceptLocal_ShouldProxyPublicAndExactCorporatePrivateAddresses()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            ["192.168.3.222"],
            GeksaVpnRoutingMode.GlobalExceptLocal);

        Covers(targets.SystemRouteCidrs, "8.8.8.8").Should().BeTrue();
        Covers(targets.SystemRouteCidrs, "192.168.3.221").Should().BeFalse();
        Covers(targets.SystemRouteCidrs, "192.168.3.222").Should().BeTrue();
        Covers(targets.SystemRouteCidrs, "169.254.10.1").Should().BeFalse();
    }

    [Fact]
    public void FromResources_Global_ShouldRouteBothAddressFamiliesCompletely()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            mode: GeksaVpnRoutingMode.Global);

        targets.SystemRouteCidrs.Should().BeEquivalentTo(["0.0.0.0/0", "::/0"]);
    }

    [Fact]
    public void FromResources_RussianSites_ShouldExposeSuffixesAndCaptureTrafficForClassification()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [Resource("office", "192.168.3.8", requiresVpn: true)],
            mode: GeksaVpnRoutingMode.RussianSites);

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
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            mode: GeksaVpnRoutingMode.RussianSites,
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
    [InlineData(GeksaVpnRoutingMode.CorporateOnly)]
    [InlineData(GeksaVpnRoutingMode.GlobalExceptLocal)]
    [InlineData(GeksaVpnRoutingMode.Global)]
    public void FromResources_AllModes_ShouldTreatVirtualHereHubsAsCorporateTargets(
        GeksaVpnRoutingMode mode)
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            mode: mode,
            virtualHereHubs: [VirtualHereHub("192.168.3.230")]);

        targets.IpCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.3.230/32");
    }

    [Fact]
    public void FromResources_CorporateOnly_ShouldAddVirtualHereHubToSystemRoutes()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            mode: GeksaVpnRoutingMode.CorporateOnly,
            virtualHereHubs: [VirtualHereHub("192.168.0.172")]);

        targets.SystemRouteCidrs.Should().ContainSingle()
            .Which.Should().Be("192.168.0.172/32");
    }

    [Fact]
    public void FromResources_GlobalExceptLocal_ShouldOverrideLocalExclusionForVirtualHereHub()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            mode: GeksaVpnRoutingMode.GlobalExceptLocal,
            virtualHereHubs: [VirtualHereHub("192.168.0.172")]);

        Covers(targets.SystemRouteCidrs, "192.168.0.172").Should().BeTrue();
        Covers(targets.SystemRouteCidrs, "192.168.0.173").Should().BeFalse();
    }

    [Fact]
    public void FromResources_DisabledVirtualHereRouting_ShouldIgnoreConfiguredHubs()
    {
        var targets = GeksaCorporateRoutingTargetProvider.FromResources(
            [],
            mode: GeksaVpnRoutingMode.RussianSites,
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

    private static GeksaResourceProfile Resource(string id, string host, bool requiresVpn) => new()
    {
        Id = id,
        Type = "ssh",
        DisplayName = id,
        Host = host,
        Port = 22,
        UserName = "user",
        RequiresVpn = requiresVpn,
    };

    private static GeksaVirtualHereHubConfiguration VirtualHereHub(string host) => new()
    {
        Host = host,
        Port = 7575,
    };
}
