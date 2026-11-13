using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaResourceEditorViewModelTests
{
    [Theory]
    [InlineData(GeksaResourceType.Rdp, "3389")]
    [InlineData(GeksaResourceType.Ssh, "22")]
    [InlineData(GeksaResourceType.Smb, "445")]
    public void NewResource_ShouldUseStandardPort(
        GeksaResourceType resourceType,
        string expectedPort)
    {
        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(resourceType));

        viewModel.PortText.Should().Be(expectedPort);
        viewModel.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void TryBuild_Ssh_ShouldRequireUserName()
    {
        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(GeksaResourceType.Ssh))
        {
            DisplayName = "SSH",
            Host = "ssh.corp.test",
            UserName = string.Empty
        };

        var success = viewModel.TryBuild(out var resource);

        success.Should().BeFalse();
        resource.Should().BeNull();
        viewModel.ValidationMessage.Should().Contain("имя пользователя");
    }

    [Fact]
    public void TryBuild_Edit_ShouldKeepOriginalIdentifier()
    {
        var original = new GeksaResourceProfile
        {
            Id = "managed-rdp",
            Type = "rdp",
            DisplayName = "Старое имя",
            Host = "rdp.corp.test",
            Port = 3389,
            RequiresVpn = true
        };
        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(GeksaResourceType.Rdp, original))
        {
            DisplayName = "Новое имя"
        };

        var success = viewModel.TryBuild(out var resource);

        success.Should().BeTrue();
        resource!.Id.Should().Be("managed-rdp");
        resource.DisplayName.Should().Be("Новое имя");
    }

    [Fact]
    public void ApplyProfile_NewResource_ShouldPreserveImportedIdentifier()
    {
        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(GeksaResourceType.Ssh));
        var imported = new GeksaResourceProfile
        {
            Id = "ssh-imported",
            Type = "ssh",
            DisplayName = "Imported SSH",
            Host = "10.0.0.8",
            Port = 22,
            UserName = "operator"
        };

        viewModel.ApplyProfile(imported);
        var success = viewModel.TryBuild(out var resource);

        success.Should().BeTrue();
        resource!.Id.Should().Be("ssh-imported");
    }

    [Fact]
    public void ApplyProfile_DifferentResourceType_ShouldRejectInsteadOfConverting()
    {
        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(GeksaResourceType.Rdp));
        var imported = new GeksaResourceProfile
        {
            Id = "ssh-imported",
            Type = "ssh",
            DisplayName = "Imported SSH",
            Host = "10.0.0.8",
            Port = 22,
            UserName = "operator"
        };

        var action = () => viewModel.ApplyProfile(imported);

        action.Should().Throw<GeksaConfigurationException>()
            .WithMessage("*SSH*редактор RDP*");
    }

    [Fact]
    public void NewSmbResource_ShouldUseAutomaticDriveLetterByDefault()
    {
        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(GeksaResourceType.Smb));

        viewModel.DriveLetter.Should().BeEmpty();
    }

    [Fact]
    public void TryBuild_Smb_ShouldNormalizeDriveLetter()
    {
        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(GeksaResourceType.Smb))
        {
            DisplayName = "Документы",
            Host = "files.corp.test",
            Path = "documents",
            DriveLetter = "y:"
        };

        var success = viewModel.TryBuild(out var resource);

        success.Should().BeTrue();
        resource!.DriveLetter.Should().Be("Y:");
    }

    [Fact]
    public void EditConnectedSmb_ShouldShowDisconnectAction()
    {
        var resource = new GeksaResourceProfile
        {
            Id = "smb-documents",
            Type = "smb",
            DisplayName = "Документы",
            Host = "files.corp.test",
            Port = 445,
            Path = "documents",
            RequiresVpn = true,
        };

        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(
                GeksaResourceType.Smb,
                resource,
                CanDisconnectSmb: true));

        viewModel.CanDisconnectSmb.Should().BeTrue();
    }

    [Theory]
    [InlineData(GeksaResourceType.Smb, false)]
    [InlineData(GeksaResourceType.Rdp, true)]
    public void DisconnectAction_ShouldOnlyBeShownForConnectedExistingSmb(
        GeksaResourceType resourceType,
        bool canDisconnect)
    {
        var resource = new GeksaResourceProfile
        {
            Id = "resource",
            Type = resourceType.ToString().ToLowerInvariant(),
            DisplayName = "Ресурс",
            Host = "server.corp.test",
            Port = resourceType == GeksaResourceType.Smb ? 445 : 3389,
            Path = resourceType == GeksaResourceType.Smb ? "share" : string.Empty,
            RequiresVpn = true,
        };

        var viewModel = new GeksaResourceEditorViewModel(
            new GeksaResourceEditRequest(resourceType, resource, canDisconnect));

        viewModel.CanDisconnectSmb.Should().BeFalse();
    }
}
