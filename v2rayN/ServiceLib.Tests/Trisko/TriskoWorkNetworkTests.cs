using System.Net;
using System.Runtime.Versioning;
using System.Text.Json;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoWorkNetworkTests
{
    private static readonly TriskoWorkNetworkOptions Options =
        TriskoWorkNetworkOptions.Create("VPN.EXAMPLE.NET.", "5555", "WORK", "operator", "10.77.0.1/16", true);

    [Fact]
    public void Options_round_trip_with_canonical_names_and_keep_the_network_independent_of_username()
    {
        var restored = JsonSerializer.Deserialize<TriskoWorkNetworkOptions>(JsonSerializer.Serialize(Options));
        Assert.Equal(Options, restored);
        Assert.Equal("vpn.example.net:5555/WORK/10.77.0.0/16", restored!.NetworkKey);
        Assert.Equal(Options.NetworkKey, (Options with { Username = "another-user" }).NetworkKey);
        Assert.False(Options.UsesSameRoute(Options with { Cidr = "10.88.0.0/16" }));
    }

    [Theory]
    [InlineData("8.8.8.0/24")]
    [InlineData("10.77.0.0/25")]
    [InlineData("fd00::/16")]
    public void Rejects_subnets_that_are_unsuitable_for_the_work_adapter(string subnet) =>
        Assert.Throws<ArgumentException>(() => TriskoWorkNetworkOptions.Create("work.example.net", "443", "WORK", "user", subnet, false));

    [Theory]
    [InlineData("10.77.0.0/16", "10.77.255.255")]
    [InlineData("172.27.40.0/24", "172.27.40.255")]
    public void Assigns_stable_usable_addresses_and_broadcasts(string cidr, string broadcast)
    {
        var subnet = IPNetwork.Parse(cidr);
        Assert.Equal(IPAddress.Parse(broadcast), TriskoWorkNetwork.GetBroadcast(subnet));
        for (var i = 0; i < 100; i++)
        {
            var id = Guid.NewGuid();
            var address = TriskoWorkNetwork.GetAddress(id, subnet);
            Assert.True(subnet.Contains(address));
            Assert.NotEqual(subnet.BaseAddress, address);
            Assert.NotEqual(IPAddress.Parse(broadcast), address);
            Assert.Equal(address, TriskoWorkNetwork.GetAddress(id, subnet));
            Assert.Matches("^[1-9][0-9]{8}$", TriskoWorkNetwork.GetNumber(id));
        }
    }

    [Fact]
    public void Reads_the_source_announcement_format_and_rejects_its_own_announcement()
    {
        const string json = """{"Version":1,"Id":"10d87de5-6b69-4dd5-86ad-364fc1cc9188","Name":"workstation","Port":5902}""";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var decoded = Assert.IsType<TriskoWorkplaceDiscovery.Announcement>(
            TriskoWorkplaceDiscovery.ReadAnnouncement(bytes, Guid.Empty));
        Assert.Equal("workstation", decoded.Name);
        Assert.Equal(5902, decoded.Port);
        Assert.Null(TriskoWorkplaceDiscovery.ReadAnnouncement(bytes, decoded.Id));
        Assert.Null(TriskoWorkplaceDiscovery.ReadAnnouncement(
            System.Text.Encoding.UTF8.GetBytes(json.Replace("5902", "65536")), Guid.Empty));
        Assert.Null(TriskoWorkplaceDiscovery.ReadAnnouncement(
            System.Text.Encoding.UTF8.GetBytes(json.Replace("workstation", @"bad\nname")), Guid.Empty));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Manages_only_the_local_profile_and_the_adapter_it_created()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string managed = "TriskoWorkNetwork,Offline,vpn.example.net:5555 (HTTP proxy),WORK,VPN126\n";
        const string otherProduct = "GeksaWorkNetwork,Offline,other.invalid:5555,OTHER,VPN127\n";

        Assert.True(TriskoSoftEtherService.HasManagedProfile(otherProduct + managed, true, Options));
        Assert.False(TriskoSoftEtherService.HasManagedProfile(otherProduct, true, Options));
        Assert.Throws<IOException>(() => TriskoSoftEtherService.HasManagedProfile(managed, false, Options));
        Assert.Throws<IOException>(() => TriskoSoftEtherService.HasManagedProfile(managed, true, Options with { Port = 443 }));
        Assert.Throws<IOException>(() => TriskoSoftEtherService.HasManagedProfile(
            managed + otherProduct.Replace("VPN127", "VPN126"), true, Options));
    }
}
