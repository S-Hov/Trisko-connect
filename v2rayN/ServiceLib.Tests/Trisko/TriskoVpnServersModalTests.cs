using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AwesomeAssertions;
using ReactiveUI.Builder;
using ReactiveUI.Primitives.Signals;
using ServiceLib.Enums;
using ServiceLib.Trisko;
using ServiceLib.Tests.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.Trisko;

public class TriskoVpnServersModalTests
{
    static TriskoVpnServersModalTests()
    {
        try
        {
            RxAppBuilder.CreateReactiveUIBuilder()
                .WithCoreServices()
                .BuildApp();
        }
        catch
        {
        }
    }

    private sealed class TestVpnRuntime : ITriskoVpnRuntime
    {
        public bool IsCoreRunning { get; set; }
        public int ConnectCalls { get; private set; }
        public bool StartCoreOnConnect { get; set; } = true;

        public int DisconnectCalls { get; private set; }

        public Task<TriskoVpnRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new TriskoVpnRuntimeSnapshot(
                IsCoreRunning,
                IsCoreRunning,
                "corporate-vpn",
                "Corporate VPN",
                ECoreType.Xray));

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCalls++;
            IsCoreRunning = StartCoreOnConnect;
            return Task.CompletedTask;
        }
        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            IsCoreRunning = false;
            return Task.CompletedTask;
        }
    }

    private static TriskoShellViewModel CreateViewModel(TriskoVpnController? controller = null, bool autoConnectOnStartup = false)
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.UiItem.TriskoAutoConnectVpnOnStartup = autoConnectOnStartup;
        CoreConfigTestFactory.BindAppManagerConfig(config);

        controller ??= new TriskoVpnController(new TestVpnRuntime());
        var configService = new TriskoCorporateConfigurationService(config);
        var userResourceService = new TriskoUserResourceService();
        var launcher = new WindowsTriskoResourceLauncher();
        var smb = new WindowsTriskoSmbDriveService();
        var netSelector = new TriskoNetworkBindingSelector();
        var oneC = new WindowsTriskoOneCSystemService();
        var vh = new WindowsTriskoVirtualHereService();

        return new TriskoShellViewModel(
            controller,
            configService,
            userResourceService,
            launcher,
            smb,
            netSelector,
            oneC,
            vh);
    }

    [Fact]
    public void IsVpnServersModalOpen_DefaultsToFalse()
    {
        using var vm = CreateViewModel();
        vm.IsVpnServersModalOpen.Should().BeFalse();
    }

    [Fact]
    public async Task StartupWithDefaultSettingsDoesNotConnectVpn()
    {
        var runtime = new TestVpnRuntime();
        using var vm = CreateViewModel(new TriskoVpnController(runtime));
        CompleteStartup(vm);

        await vm.TryAutoConnectOnStartupAsync();

        Assert.False(vm.AutoConnectVpnOnStartup);
        Assert.Equal(0, runtime.ConnectCalls);
    }

    [Fact]
    public async Task StartupDoesNotReconnectAnAlreadyRunningVpn()
    {
        var runtime = new TestVpnRuntime { IsCoreRunning = true };
        var controller = new TriskoVpnController(runtime);
        await controller.RefreshAsync(TestContext.Current.CancellationToken);
        using var vm = CreateViewModel(controller, autoConnectOnStartup: true);
        CompleteStartup(vm);

        await vm.TryAutoConnectOnStartupAsync();
        await vm.TryAutoConnectOnStartupAsync();

        Assert.Equal(0, runtime.ConnectCalls);
        Assert.Equal(0, runtime.DisconnectCalls);
    }

    [Fact]
    public async Task StartupWithoutProfilesDoesNotLoopOrConnectAfterLaterUiChanges()
    {
        var runtime = new TestVpnRuntime();
        using var vm = CreateViewModel(new TriskoVpnController(runtime), autoConnectOnStartup: true);
        CompleteStartup(vm);
        vm.CanConnect = false;

        await vm.TryAutoConnectOnStartupAsync();
        vm.CanConnect = true;
        await vm.TryAutoConnectOnStartupAsync();

        Assert.Equal(0, runtime.ConnectCalls);
    }

    private static void CompleteStartup(TriskoShellViewModel vm) =>
        ((TaskCompletionSource<bool>)typeof(TriskoShellViewModel)
            .GetField("_initializationCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(vm)!).TrySetResult(true);

    [Fact]
    public async Task FailedInitializationDoesNotStartVpn()
    {
        var runtime = new TestVpnRuntime();
        using var vm = CreateViewModel(new TriskoVpnController(runtime), autoConnectOnStartup: true);
        ((TaskCompletionSource<bool>)typeof(TriskoShellViewModel)
            .GetField("_initializationCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(vm)!).TrySetResult(false);
        vm.CanConnect = true;

        await vm.TryAutoConnectOnStartupAsync();

        Assert.Equal(0, runtime.ConnectCalls);
    }

    [Fact]
    public async Task RecoveryWaitsForCountdownAndCancelPreventsConnection()
    {
        var runtime = new TestVpnRuntime();
        using var vm = CreateViewModel(new TriskoVpnController(runtime));
        SetPrivate(vm, "_desiredConnected", true);

        await CheckRecovery(vm);
        Assert.True(vm.IsReconnectPending);
        Assert.Contains("через", vm.ReconnectCountdownText);
        Assert.Equal(0, runtime.ConnectCalls);

        await vm.CancelReconnectCommand.Execute();
        await CheckRecovery(vm);
        Assert.False(vm.IsReconnectPending);
        Assert.Equal(0, runtime.ConnectCalls);
    }

    [Fact]
    public async Task RetryNowReconnectsAndClearsCountdown()
    {
        var runtime = new TestVpnRuntime();
        using var vm = CreateViewModel(new TriskoVpnController(runtime));
        SetPrivate(vm, "_desiredConnected", true);
        await CheckRecovery(vm);

        await vm.RetryConnectionNowCommand.Execute();
        await CheckRecovery(vm);

        Assert.Equal(1, runtime.ConnectCalls);
        Assert.True(vm.IsConnected);
        Assert.False(vm.IsReconnectPending);
    }

    [Fact]
    public async Task FailedRecoverySchedulesNextAttemptWithBackoff()
    {
        var runtime = new TestVpnRuntime { StartCoreOnConnect = false };
        using var vm = CreateViewModel(new TriskoVpnController(runtime));
        SetPrivate(vm, "_desiredConnected", true);
        await CheckRecovery(vm);
        await vm.RetryConnectionNowCommand.Execute();
        await CheckRecovery(vm);

        Assert.Equal(1, runtime.ConnectCalls);
        Assert.True(vm.IsReconnectPending);
        Assert.Contains("попытка 2", vm.ReconnectCountdownText);
        await CheckRecovery(vm);
        Assert.Equal(1, runtime.ConnectCalls);
    }

    private static void SetPrivate(TriskoShellViewModel vm, string name, object value) =>
        typeof(TriskoShellViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    private static Task CheckRecovery(TriskoShellViewModel vm) =>
        (Task)typeof(TriskoShellViewModel).GetMethod("CheckVpnRecoveryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [TestContext.Current.CancellationToken])!;

    [Fact]
    public async Task GlobalSearchNavigatesToExactSettingsSubpageAndClosesModal()
    {
        using var vm = CreateViewModel();
        await vm.OpenGlobalSearchCommand.Execute();
        Assert.True(vm.IsAnyModalOpen);
        vm.GlobalSearchQuery = "маршрутизация";
        var result = Assert.Single(Assert.Single(vm.GlobalSearchGroups).Items);
        await vm.ActivateSearchResultCommand.Execute(result);
        Assert.False(vm.IsGlobalSearchOpen);
        Assert.True(vm.IsSettingsPage);
        Assert.Equal(TriskoSettingsSubPage.VpnRouting, vm.CurrentSettingsSubPage);
    }

    [Fact]
    public async Task GlobalSearchGroupsConnectionsAndSeparatesNavigationFromOpening()
    {
        using var vm = CreateViewModel();
        var opened = 0;
        vm.Resources.Add(new TriskoResourceItemViewModel(new TriskoResourceProfile
        {
            Id = "search-test", Type = "ssh", DisplayName = "Москва", Host = "192.168.1.10", Port = 22
        }, _ => { opened++; return Task.CompletedTask; }));
        await vm.OpenGlobalSearchCommand.Execute();
        vm.GlobalSearchQuery = "москва ssh";
        var group = Assert.Single(vm.GlobalSearchGroups);
        Assert.Equal("Подключения → SSH", group.Name);
        var result = Assert.Single(group.Items);
        await vm.OpenSearchResultSectionCommand.Execute(result);
        Assert.True(vm.IsSshAccessPage);
        Assert.Equal(0, opened);
        await vm.ActivateSearchResultCommand.Execute(result);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void OpenVpnServersModalCommand_OpensModal()
    {
        using var vm = CreateViewModel();
        vm.IsVpnServersModalOpen = false;

        ((ICommand)vm.OpenVpnServersModalCommand).Execute(null);

        vm.IsVpnServersModalOpen.Should().BeTrue();
    }

    [Fact]
    public void CloseVpnServersModalCommand_ClosesModal()
    {
        using var vm = CreateViewModel();
        vm.IsVpnServersModalOpen = true;

        ((ICommand)vm.CloseVpnServersModalCommand).Execute(null);

        vm.IsVpnServersModalOpen.Should().BeFalse();
    }

    [Fact]
    public async Task SelectVpnProfileAsync_DoesNotCloseModal()
    {
        using var vm = CreateViewModel();
        vm.CanSelectVpnProfile = true;
        vm.IsVpnServersModalOpen = true;
        var profile = TriskoVpnProfileOption.CreateAutomatic();

        await vm.SelectVpnProfileAsync(profile);

        vm.IsVpnServersModalOpen.Should().BeTrue();
    }

    [Fact]
    public async Task SelectVpnProfileAsync_WhenVpnIsRunning_ShowsInstructionAndKeepsSelection()
    {
        var runtime = new TestVpnRuntime { IsCoreRunning = true };
        var controller = new TriskoVpnController(runtime);
        await controller.RefreshAsync(TestContext.Current.CancellationToken);
        using var vm = CreateViewModel(controller);
        var selectedBefore = vm.SelectedVpnProfile;
        var requested = new TriskoVpnProfileOption(
            "different-profile",
            "Другой сервер",
            "VLESS",
            string.Empty,
            "vpn.example.test",
            443);
        string? shownMessage = null;
        using var registration = vm.ShowVpnProfileSwitchBlockedInteraction.RegisterHandler(interaction =>
        {
            shownMessage = interaction.Input;
            interaction.SetOutput(default);
        });

        await vm.SelectVpnProfileAsync(requested);

        shownMessage.Should().Contain("сначала отключите VPN");
        vm.SelectedVpnProfile.Should().BeSameAs(selectedBefore);
        runtime.DisconnectCalls.Should().Be(0);
    }

    [Fact]
    public async Task ConnectVpnProfileCommand_WhenVpnIsRunning_DoesNotDisconnectAutomatically()
    {
        var runtime = new TestVpnRuntime { IsCoreRunning = true };
        var controller = new TriskoVpnController(runtime);
        await controller.RefreshAsync(TestContext.Current.CancellationToken);
        using var vm = CreateViewModel(controller);
        var requested = new TriskoVpnProfileOption(
            "different-profile",
            "Другой сервер",
            "VLESS",
            string.Empty,
            "vpn.example.test",
            443);
        var messageShown = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = vm.ShowVpnProfileSwitchBlockedInteraction.RegisterHandler(interaction =>
        {
            messageShown.TrySetResult(interaction.Input);
            interaction.SetOutput(default);
        });

        ((ICommand)vm.ConnectVpnProfileCommand).Execute(requested);
        var shownMessage = await messageShown.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        shownMessage.Should().Contain("другому серверу");
        runtime.DisconnectCalls.Should().Be(0);
        controller.Snapshot.IsCoreRunning.Should().BeTrue();
    }

    [Fact]
    public void ConnectionGroupsExpanded_DefaultsToTrue()
    {
        using var vm = CreateViewModel();
        vm.IsRegularVpnServersExpanded.Should().BeTrue();
        vm.IsAnyDeskVpnServersExpanded.Should().BeTrue();
    }

    [Fact]
    public void ToggleRegularVpnServersExpandedCommand_TogglesState()
    {
        using var vm = CreateViewModel();
        vm.IsRegularVpnServersExpanded.Should().BeTrue();

        ((ICommand)vm.ToggleRegularVpnServersExpandedCommand).Execute(null);
        vm.IsRegularVpnServersExpanded.Should().BeFalse();

        ((ICommand)vm.ToggleRegularVpnServersExpandedCommand).Execute(null);
        vm.IsRegularVpnServersExpanded.Should().BeTrue();
    }

    [Fact]
    public void ToggleAnyDeskVpnServersExpandedCommand_TogglesState()
    {
        using var vm = CreateViewModel();
        vm.IsAnyDeskVpnServersExpanded.Should().BeTrue();

        ((ICommand)vm.ToggleAnyDeskVpnServersExpandedCommand).Execute(null);
        vm.IsAnyDeskVpnServersExpanded.Should().BeFalse();

        ((ICommand)vm.ToggleAnyDeskVpnServersExpandedCommand).Execute(null);
        vm.IsAnyDeskVpnServersExpanded.Should().BeTrue();
    }
}
