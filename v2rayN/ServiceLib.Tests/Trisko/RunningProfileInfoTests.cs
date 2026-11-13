using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class RunningProfileInfoTests
{
    [Fact]
    public void From_ShouldUseOriginalProfileInsteadOfGeneratedInternalNode()
    {
        var original = new ProfileItem
        {
            IndexId = "subscription-profile", Remarks = "Trisko Hysteria2",
            Address = "vpn.example.test", Port = 443, ConfigType = EConfigType.Hysteria2
        };
        var context = new CoreConfigContext
        {
            RootNode = original,
            Node = new ProfileItem { IndexId = "4954921868460612647", Remarks = "internal" },
            RunCoreType = ECoreType.sing_box
        };

        var running = RunningProfileInfo.From(context);
        original.IndexId = "replacement-id";
        original.Remarks = "replacement-name";
        original.Address = "other.example.test";

        running.IndexId.Should().Be("subscription-profile");
        running.Name.Should().Be("Trisko Hysteria2");
        running.Address.Should().Be("vpn.example.test");
        running.Protocol.Should().Be(EConfigType.Hysteria2);
        running.CoreType.Should().Be(ECoreType.sing_box);
    }

    [Fact]
    public void From_WithoutRootOrName_ShouldUseNodeAndAddress()
    {
        var running = RunningProfileInfo.From(new CoreConfigContext
        {
            Node = new ProfileItem { IndexId = "direct", Remarks = " ", Address = "vpn.example.test" },
            RunCoreType = ECoreType.Xray
        });

        running.IndexId.Should().Be("direct");
        running.Name.Should().Be("vpn.example.test");
    }
}
