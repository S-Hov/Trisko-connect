using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using ReactiveUI.Primitives;
using ServiceLib.Trisko;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoAutoLaunchTests
{
    [Fact]
    public void TriskoResourceProfile_AutoLaunchOnVpn_DefaultShouldBeFalse()
    {
        var profile = new TriskoResourceProfile();
        profile.AutoLaunchOnVpn.Should().BeFalse();
    }

    [Fact]
    public void EditorViewModel_ShouldInitializeAndBuildAutoLaunchOnVpn()
    {
        var profile = new TriskoResourceProfile
        {
            Id = "rdp-1",
            Type = "rdp",
            DisplayName = "Рабочий стол 1С",
            Host = "192.168.1.10",
            Port = 3389,
            UserName = "corp\\user",
            AutoLaunchOnVpn = true
        };

        var request = new TriskoResourceEditRequest(TriskoResourceType.Rdp, profile);
        var editor = new TriskoResourceEditorViewModel(request);

        editor.AutoLaunchOnVpn.Should().BeTrue();

        var success = editor.TryBuild(out var built);
        success.Should().BeTrue();
        built.Should().NotBeNull();
        built!.AutoLaunchOnVpn.Should().BeTrue();
    }

    [Fact]
    public void ItemViewModel_ToggleAutoLaunchCommand_ShouldCallCallback()
    {
        var profile = new TriskoResourceProfile
        {
            Id = "ssh-1",
            Type = "ssh",
            DisplayName = "SSH Server",
            Host = "192.168.1.20",
            Port = 22,
            UserName = "admin",
            AutoLaunchOnVpn = false
        };

        TriskoResourceProfile? toggled = null;
        var item = new TriskoResourceItemViewModel(
            profile,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            updated =>
            {
                toggled = updated;
                return Task.CompletedTask;
            });

        item.AutoLaunchOnVpn.Should().BeFalse();
        item.AutoLaunchToolTipText.Should().Contain("Включить");

        item.ToggleAutoLaunchCommand.Execute().Subscribe(new NoopObserver());

        item.AutoLaunchOnVpn.Should().BeTrue();
        item.AutoLaunchToolTipText.Should().Contain("Отключить");
        toggled.Should().NotBeNull();
        toggled!.AutoLaunchOnVpn.Should().BeTrue();
    }

    [Fact]
    public void Codec_SingleProfile_ShouldSerializeAndDeserializeAutoLaunchOnVpn()
    {
        var profile = new TriskoResourceProfile
        {
            Id = "smb-1",
            Type = "smb",
            DisplayName = "Общий диск",
            Host = "192.168.1.50",
            Port = 445,
            Path = "docs",
            AutoLaunchOnVpn = true
        };

        var json = TriskoCorporateConfigurationCodec.SerializeResourceProfile(profile);
        json.Should().Contain("\"autoLaunchOnVpn\": true");

        var parsed = TriskoCorporateConfigurationCodec.ParseResourceProfile(
            System.Text.Encoding.UTF8.GetBytes(json));

        parsed.AutoLaunchOnVpn.Should().BeTrue();
    }

    [Fact]
    public void SmbResource_ApplySmbStatus_ShouldPreserveLastSmbStatus()
    {
        var profile = new TriskoResourceProfile
        {
            Id = "smb-drive-1",
            Type = "smb",
            DisplayName = "Документы",
            Host = "192.168.1.50",
            Port = 445,
            Path = "docs",
            AutoLaunchOnVpn = false
        };

        var item = new TriskoResourceItemViewModel(profile, _ => Task.CompletedTask);
        item.UpdateAvailability(vpnConnected: true);

        var status = new TriskoSmbDriveStatus(
            TriskoSmbDriveState.Connected,
            "Z:",
            @"\\192.168.1.50\docs");
        item.ApplySmbStatus(status);

        item.SmbState.Should().Be(TriskoSmbDriveState.Connected);
        item.ActualDriveLetter.Should().Be("Z:");
        item.LastSmbStatus.Should().Be(status);
        item.AvailabilityText.Should().Be("Подключено как Z:");
    }

    private sealed class NoopObserver : IObserver<RxVoid>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) => throw error;

        public void OnNext(RxVoid value) { }
    }
}
