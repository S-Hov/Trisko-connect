using AwesomeAssertions;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoResourceEditorViewModelTests
{
    [Theory]
    [InlineData(TriskoResourceType.Rdp, "3389")]
    [InlineData(TriskoResourceType.Ssh, "22")]
    [InlineData(TriskoResourceType.Smb, "445")]
    public void NewResource_ShouldUseStandardPort(
        TriskoResourceType resourceType,
        string expectedPort)
    {
        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(resourceType));

        viewModel.PortText.Should().Be(expectedPort);
        viewModel.IsEditing.Should().BeFalse();
    }

    [Fact]
    public void TryBuild_Ssh_ShouldRequireUserName()
    {
        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(TriskoResourceType.Ssh))
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
        var original = new TriskoResourceProfile
        {
            Id = "managed-rdp",
            Type = "rdp",
            DisplayName = "Старое имя",
            Host = "rdp.corp.test",
            Port = 3389,
            RequiresVpn = true
        };
        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(TriskoResourceType.Rdp, original))
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
        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(TriskoResourceType.Ssh));
        var imported = new TriskoResourceProfile
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
        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(TriskoResourceType.Rdp));
        var imported = new TriskoResourceProfile
        {
            Id = "ssh-imported",
            Type = "ssh",
            DisplayName = "Imported SSH",
            Host = "10.0.0.8",
            Port = 22,
            UserName = "operator"
        };

        var action = () => viewModel.ApplyProfile(imported);

        action.Should().Throw<TriskoConfigurationException>()
            .WithMessage("*SSH*редактор RDP*");
    }

    [Fact]
    public void NewSmbResource_ShouldUseAutomaticDriveLetterByDefault()
    {
        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(TriskoResourceType.Smb));

        viewModel.DriveLetter.Should().BeEmpty();
    }

    [Fact]
    public void TryBuild_Smb_ShouldNormalizeDriveLetter()
    {
        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(TriskoResourceType.Smb))
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
        var resource = new TriskoResourceProfile
        {
            Id = "smb-documents",
            Type = "smb",
            DisplayName = "Документы",
            Host = "files.corp.test",
            Port = 445,
            Path = "documents",
            RequiresVpn = true,
        };

        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(
                TriskoResourceType.Smb,
                resource,
                CanDisconnectSmb: true));

        viewModel.CanDisconnectSmb.Should().BeTrue();
    }

    [Theory]
    [InlineData(TriskoResourceType.Smb, false)]
    [InlineData(TriskoResourceType.Rdp, true)]
    public void DisconnectAction_ShouldOnlyBeShownForConnectedExistingSmb(
        TriskoResourceType resourceType,
        bool canDisconnect)
    {
        var resource = new TriskoResourceProfile
        {
            Id = "resource",
            Type = resourceType.ToString().ToLowerInvariant(),
            DisplayName = "Ресурс",
            Host = "server.corp.test",
            Port = resourceType == TriskoResourceType.Smb ? 445 : 3389,
            Path = resourceType == TriskoResourceType.Smb ? "share" : string.Empty,
            RequiresVpn = true,
        };

        var viewModel = new TriskoResourceEditorViewModel(
            new TriskoResourceEditRequest(resourceType, resource, canDisconnect));

        viewModel.CanDisconnectSmb.Should().BeFalse();
    }
}
