using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaResourceItemViewModelTests
{
    [Fact]
    public void UpdateAvailability_VpnResource_ShouldFollowVpnState()
    {
        var viewModel = CreateResource(requiresVpn: true);

        viewModel.UpdateAvailability(vpnConnected: false);
        viewModel.CanOpen.Should().BeFalse();
        viewModel.AvailabilityText.Should().Be("Нужен VPN");

        viewModel.UpdateAvailability(vpnConnected: true);
        viewModel.CanOpen.Should().BeTrue();
        viewModel.AvailabilityText.Should().Be("Доступно");
    }

    [Fact]
    public void UpdateAvailability_PublicResource_ShouldStayAvailableWithoutVpn()
    {
        var viewModel = CreateResource(requiresVpn: false);

        viewModel.UpdateAvailability(vpnConnected: false);

        viewModel.CanOpen.Should().BeTrue();
        viewModel.AvailabilityText.Should().Be("Доступно");
    }

    [Fact]
    public void SshResource_ShouldExposeSafeTargetAndActionText()
    {
        var viewModel = CreateResource(requiresVpn: true);

        viewModel.TargetText.Should().Be("operator@ssh.corp.test:22");
        viewModel.OpenButtonText.Should().Be("Подключиться");
    }

    [Fact]
    public void SmbResource_ShouldRequireMappingBeforeOpening()
    {
        var viewModel = CreateSmbResource();

        viewModel.UpdateAvailability(vpnConnected: true);

        viewModel.OpenButtonText.Should().Be("Подключить диск");
        viewModel.AvailabilityText.Should().Be("Диск не подключён");
        viewModel.CanOpen.Should().BeTrue();
    }

    [Fact]
    public void SmbResource_ConnectedStatus_ShouldExposeDriveActions()
    {
        var viewModel = CreateSmbResource();
        viewModel.UpdateAvailability(vpnConnected: true);

        viewModel.ApplySmbStatus(new GeksaSmbDriveStatus(
            GeksaSmbDriveState.Connected,
            "Z:",
            @"\\files.corp.test\documents"));

        viewModel.OpenButtonText.Should().Be("Открыть диск");
        viewModel.AvailabilityText.Should().Be("Подключено как Z:");
        viewModel.TargetText.Should().StartWith("Z:");
    }

    [Theory]
    [InlineData("rdp", true, false, false)]
    [InlineData("smb", false, true, false)]
    [InlineData("ssh", false, false, true)]
    public void ProtocolFlags_ShouldMatchResourceType(
        string type,
        bool expectedRdp,
        bool expectedSmb,
        bool expectedSsh)
    {
        var vm = new GeksaResourceItemViewModel(
            new GeksaResourceProfile { Id = "test-1", Type = type, DisplayName = "Test" },
            _ => Task.CompletedTask);

        vm.IsRdp.Should().Be(expectedRdp);
        vm.IsSmb.Should().Be(expectedSmb);
        vm.IsSsh.Should().Be(expectedSsh);
    }

    [Theory]
    [InlineData("files.corp.test", "documents", @"\\files.corp.test\documents")]
    [InlineData("files.corp.test", "/teams/accounting/", @"\\files.corp.test\teams\accounting")]
    [InlineData("files.corp.test", @"\users\", @"\\files.corp.test\users")]
    public void SmbPath_Create_ShouldBuildCanonicalUncPath(
        string host,
        string path,
        string expected)
    {
        GeksaSmbPath.Create(host, path).Should().Be(expected);
    }

    private static GeksaResourceItemViewModel CreateResource(bool requiresVpn)
    {
        return new GeksaResourceItemViewModel(
            new GeksaResourceProfile
            {
                Id = "ssh-test",
                Type = "ssh",
                DisplayName = "SSH",
                Host = "ssh.corp.test",
                Port = 22,
                UserName = "operator",
                RequiresVpn = requiresVpn
            },
            _ => Task.CompletedTask);
    }

    private static GeksaResourceItemViewModel CreateSmbResource()
    {
        return new GeksaResourceItemViewModel(
            new GeksaResourceProfile
            {
                Id = "smb-test",
                Type = "smb",
                DisplayName = "Документы",
                Host = "files.corp.test",
                Port = 445,
                Path = "documents",
                RequiresVpn = true
            },
            _ => Task.CompletedTask);
    }
}
