using System.Runtime.InteropServices;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoVncSettingsTests
{
    [Theory]
    [InlineData("192.168.1.10", 5900)]
    [InlineData("office-pc", 65535)]
    [InlineData("::1", 1)]
    public void Accepts_direct_addresses(string host, int port) => TriskoVncSettings.ValidateEndpoint(host, port);



    [Fact]
    public void Accepts_the_displayed_endpoint_with_a_custom_port() =>
        Assert.Equal(("10.77.250.2", 5901), TriskoVncSettings.ResolveEndpoint("10.77.250.2:5901", 5900, null));

    [Theory]
    [InlineData("LocalSubnet", false, true)]
    [InlineData("10.77.250.0/24", false, true)]
    [InlineData("10.77.250.3", false, true)]
    [InlineData("10.77.250.2", false, false)]
    [InlineData("LocalSubnet", true, false)]
    public void Pilot_identity_requires_an_allowed_peer(string allowed, bool loopbackOnly, bool expected) =>
        Assert.Equal(expected, TriskoVncSettings.AllowsWorkPeer(new(LoopbackOnly: loopbackOnly, AllowedAddresses: allowed),
            System.Net.IPAddress.Parse("10.77.250.3")));

    [Theory]
    [InlineData("vnc://host", 5900)]
    [InlineData("host:5900", 5900)]
    [InlineData("host", 0)]
    [InlineData("host", 65536)]
    [InlineData("", 5900)]
    public void Rejects_invalid_endpoints(string host, int port) =>
        Assert.Throws<ArgumentException>(() => TriskoVncSettings.ValidateEndpoint(host, port));

    [Theory]
    [InlineData("Any")]
    [InlineData("*")]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.1 profile=any")]
    [InlineData("192.168.1.0/33")]
    [InlineData("::/0")]
    [InlineData("")]
    public void Rejects_unrestricted_or_malformed_firewall_scopes(string value) =>
        Assert.Throws<ArgumentException>(() => TriskoVncSettings.NormalizeAllowedAddresses(value));

    [Fact]
    public void Allows_explicit_vpn_subnets_and_normalizes_spacing() =>
        Assert.Equal("LocalSubnet,10.80.0.0/24,192.168.1.2,fd00::/64",
            TriskoVncSettings.NormalizeAllowedAddresses(" LocalSubnet, 10.80.0.0/24, 192.168.1.2, fd00::/64 "));

    [Theory]
    [InlineData("short")]
    [InlineData("longpassword")]
    [InlineData("пароль12")]
    [InlineData("1234 678")]
    public void Rejects_passwords_that_would_be_truncated_or_reencoded(string value) =>
        Assert.Throws<ArgumentException>(() => TriskoVncSettings.EncodePassword(value));

    [Fact]
    public void Configuration_requires_authentication_and_disables_unused_services()
    {
        var ini = TriskoVncSettings.BuildServerConfiguration(new(15990, false, true), "Test123!");
        Assert.Contains("AuthRequired=1", ini);
        Assert.Contains("HTTPConnect=0", ini);
        Assert.Contains("FileTransferEnabled=0", ini);
        Assert.Contains("LoopbackOnly=1", ini);
        Assert.Contains("PortNumber=15990", ini);
        Assert.DoesNotContain("Test123!", ini);
        Assert.DoesNotContain("passwd2=", ini);
    }

    [Fact]
    public void Password_storage_is_readable_by_the_same_Windows_API_as_UltraVNC()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "trisko-vnc-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, TriskoVncSettings.BuildServerConfiguration(new(), "password"));
            var encrypted = new byte[8];
            Assert.True(GetPrivateProfileStruct("ultravnc", "passwd", encrypted, 8, path));
            // Known classic VNC password-file vector for the ASCII password "password".
            Assert.Equal("DBD83CFD727A1458", Convert.ToHexString(encrypted));
        }
        finally { File.Delete(path); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPrivateProfileStruct(string section, string key, byte[] buffer, uint size, string fileName);
}
