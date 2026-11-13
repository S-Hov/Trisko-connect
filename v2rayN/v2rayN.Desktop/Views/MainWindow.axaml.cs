using Avalonia.Platform.Storage;
using DialogHostAvalonia;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;
using v2rayN.Desktop.Geksa;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    private static Config _config;
    private readonly SingleReplaceableDisposable _layoutBindingsDisposable = new();
    private BackupAndRestoreView? _backupAndRestoreView;
    private VelopackGeksaApplicationUpdateService? _gekUpdateService;
    private GeksaTrayMenuManager? _geksaTrayMenuManager;
    private readonly SemaphoreSlim _gekUpdateDialogGate = new(1, 1);
    private readonly CancellationTokenSource _updateLoopCts = new();
    private bool _blCloseByUser = false;
    private bool _exitPreparationInProgress;
    private bool _exitPrepared;
    private bool _subscriptionOnboardingStarted;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        NotificationThemeScope.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;
        GeksaThemeVariants.CurrentVariantChanged += NotificationThemeChanged;
        Closed += (_, _) => GeksaThemeVariants.CurrentVariantChanged -= NotificationThemeChanged;

        KeyDown += MainWindow_KeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuClose.Click += MenuClose_Click;
        btnBackToGeksa.Click += BtnBackToGeksa_Click;

        conTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomOutboundServerCmd, v => v.menuAddCustomOutboundServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.IsVisible).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.StatusBarViewModel, v => v.contentStatusBarView.Content).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.GeksaShellViewModel, v => v.contentGeksaShell.Content).DisposeWith(disposables);

            _layoutBindingsDisposable.DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateLayout)
                .DisposeWith(disposables);

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(async interaction =>
            {
                var result = await AvaUtils.GetClipboardData(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(async interaction =>
            {
                ShowHideWindow(false);
                await Task.Delay(200);
                var result = QRCodeAvaloniaUtils.CaptureScreen();
                ShowHideWindow(true);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.OpenFileDialog(null);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ShowAdvancedVpnSettingsInteraction.RegisterHandler(interaction =>
            {
                ShowAdvancedVpnSettings(true);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.BrowseCorporateConfigurationInteraction.RegisterHandler(async interaction =>
            {
                var filter = new Avalonia.Platform.Storage.FilePickerFileType(global::ServiceLib.Geksa.TriskoLocalization.Text("Полный комплект Trisko"))
                {
                    Patterns = ["*.gac3", "*.json"]
                };
                var result = await UI.OpenFileDialog(filter);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ShowUpdateManagerInteraction.RegisterHandler(async interaction =>
            {
                await ShowGeksaUpdateWindowAsync(checkImmediately: true);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.EditResourceInteraction.RegisterHandler(async interaction =>
            {
                var dialog = new Geksa.GeksaResourceEditorWindow(interaction.Input);
                var result = await dialog.ShowDialog<GeksaResourceEditResult?>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.AddManualVpnInteraction.RegisterHandler(async interaction =>
            {
                var dialog = new Geksa.GeksaManualVpnWindow();
                var result = await dialog.ShowDialog<GeksaManualVpnProfile?>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.BrowseAnyDeskExecutableInteraction.RegisterHandler(async interaction =>
            {
                var filter = new Avalonia.Platform.Storage.FilePickerFileType(global::ServiceLib.Geksa.TriskoLocalization.Text("Portable-клиент AnyDesk"))
                {
                    Patterns = ["AnyDesk.exe", "*.exe"]
                };
                var result = await UI.OpenFileDialog(filter);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ShowAnyDeskWarningInteraction.RegisterHandler(async interaction =>
            {
                await UI.ShowMessage(interaction.Input, global::ServiceLib.Geksa.TriskoLocalization.Text("Предупреждение AnyDesk"));
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ConfigureVpnSubscriptionInteraction.RegisterHandler(async interaction =>
            {
                var dialog = new Geksa.GeksaVpnSubscriptionWindow(interaction.Input);
                var result = await dialog.ShowDialog<GeksaVpnSubscriptionRequest?>(this);
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.SaveCorporateConfigurationInteraction.RegisterHandler(async interaction =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider is null)
                {
                    interaction.SetOutput(null);
                    return;
                }
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = global::ServiceLib.Geksa.TriskoLocalization.Text("Экспорт корпоративного комплекта"),
                    SuggestedFileName = interaction.Input,
                    DefaultExtension = "gac3",
                    FileTypeChoices =
                    [
                        new Avalonia.Platform.Storage.FilePickerFileType(global::ServiceLib.Geksa.TriskoLocalization.Text("Комплект Trisko (*.gac3)"))
                        {
                            Patterns = ["*.gac3"]
                        }
                    ]
                });
                interaction.SetOutput(file?.TryGetLocalPath());
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ConfirmCorporateConfigurationImportInteraction.RegisterHandler(async interaction =>
            {
                var dialog = new Geksa.GeksaConfigurationImportPreviewWindow(interaction.Input);
                var decision = await dialog.ShowDialog<GeksaConfigurationImportDecision?>(this);
                interaction.SetOutput(decision ?? new GeksaConfigurationImportDecision(
                    Confirmed: false,
                    KeepLocalChanges: true));
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ConfirmOneCSystemChangeInteraction.RegisterHandler(async interaction =>
            {
                var result = await UI.ShowYesNo(interaction.Input);
                interaction.SetOutput(result == ButtonResult.Yes);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ShowVpnProfileSwitchBlockedInteraction.RegisterHandler(async interaction =>
            {
                await UI.ShowMessage(interaction.Input, global::ServiceLib.Geksa.TriskoLocalization.Text("Смена VPN-сервера"));
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.GeksaShellViewModel.ShowFormatHelpInteraction.RegisterHandler(async interaction =>
            {
                var dialog = new Geksa.GeksaFormatHelpWindow();
                await dialog.ShowDialog(this);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(byUser =>
              {
                  _updateLoopCts.Cancel();
                  Shutdown(byUser);
              })
              .DisposeWith(disposables);
        });

        if (Utils.IsWindows())
        {
            Title = $"Trisko connect {Utils.GetVersionInfo()} — " +
                global::ServiceLib.Geksa.TriskoLocalization.Format("{0}", (Utils.IsAdministrator() ? global::ServiceLib.Geksa.TriskoLocalization.Text("запущено от администратора") : global::ServiceLib.Geksa.TriskoLocalization.Text("требуются права администратора для TUN")));

            if (!Design.IsDesignMode)
            {
                ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
                HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
            }
        }
        else
        {
            Title = $"{Utils.GetVersion()}";
            menuAddServerViaScan.IsVisible = false;
        }

        if (_config.UiItem.AutoHideStartup && Utils.IsWindows())
        {
            WindowState = WindowState.Minimized;
        }

        AddHelpMenuItem();
    }

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Dispatcher.UIThread.Post(() =>
                ShowHideWindow(true),
            DispatcherPriority.Default);
    }

    private async Task DelegateSnackMsg(string content)
    {
        var kind = content == ResUI.OperationFailed || content == ResUI.CheckServerSettings
            ? GeksaNotificationKind.Error
            : content == ResUI.OperationSuccess ? GeksaNotificationKind.Success : GeksaNotificationKind.Information;
        ViewModel?.GeksaShellViewModel.Notifications.Show("Trisko connect", content, kind);
        await Task.CompletedTask;
    }

    private void NotificationThemeChanged(object? sender, EventArgs e) =>
        NotificationThemeScope.RequestedThemeVariant = GeksaThemeVariants.CurrentVariant;

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                Dispatcher.UIThread.Post(() => ShowHideWindow(null));
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        Logging.SaveLog("OnClosing -> " + e.CloseReason.ToString());

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                e.Cancel = true;
                _updateLoopCts.Cancel();
                if (await PrepareEcpExitAsync())
                {
                    _blCloseByUser = true;
                    await AppManager.Instance.AppExitAsync(false);
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                }
                break;
        }

        base.OnClosing(e);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.V:
                    await AddServerViaClipboardAsync();
                    break;

                case Key.S:
                    await ScanScreenTaskAsync();
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuPromotion_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private void MenuSettingsSetUWP_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = await AvaUtils.GetClipboardData(this);
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    public async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        await Task.Delay(200);

        var bytes = QRCodeAvaloniaUtils.CaptureScreen();
        if (bytes != null && ViewModel != null)
        {
            await ViewModel.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private async void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        await ShowGeksaUpdateWindowAsync(checkImmediately: true);
        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView);
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await RequestApplicationExitAsync(confirm: true);
        }
        catch
        {
            // Ignore
        }
    }

    public async Task RequestApplicationExitAsync(bool confirm)
    {
        if (confirm && await UI.ShowYesNo(ResUI.menuExitTips) != ButtonResult.Yes)
        {
            return;
        }
        if (!await PrepareEcpExitAsync())
        {
            return;
        }

        _blCloseByUser = true;
        StorageUI();
        await AppManager.Instance.AppExitAsync(false);
        AppManager.Instance.Shutdown(true);
    }

    private async Task<bool> PrepareEcpExitAsync()
    {
        if (_exitPrepared)
        {
            return true;
        }
        if (_exitPreparationInProgress)
        {
            return false;
        }

        _exitPreparationInProgress = true;
        try
        {
            var failureMessage = ViewModel?.GeksaShellViewModel is { } shell
                ? await shell.TryPrepareApplicationExitAsync()
                : null;
            if (failureMessage.IsNotEmpty())
            {
                _blCloseByUser = false;
                ShowAdvancedVpnSettings(false);
                ShowHideWindow(true);
                await UI.ShowMessage(failureMessage, global::ServiceLib.Geksa.TriskoLocalization.Text("Отключите устройства ЭЦП"));
                return false;
            }

            _exitPrepared = true;
            return true;
        }
        finally
        {
            _exitPreparationInProgress = false;
        }
    }

    private async void Shutdown(bool obj)
    {
        if (!await PrepareEcpExitAsync())
        {
            return;
        }
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }
        _blCloseByUser = true;
        StorageUI();
        _geksaTrayMenuManager?.Dispose();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            HotkeyManager.Instance.Dispose();
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    public void ShowAdvancedVpnSettings(bool showAdvanced)
    {
        if (!showAdvanced) ViewModel?.GeksaShellViewModel.RefreshStartupSettings();
        contentGeksaShell.IsVisible = !showAdvanced;
        advancedVpnPanel.IsVisible = showAdvanced;
    }

    private async Task ShowGeksaUpdateWindowAsync(bool checkImmediately)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (!await _gekUpdateDialogGate.WaitAsync(0))
        {
            return;
        }
        try
        {
            _gekUpdateService ??= new VelopackGeksaApplicationUpdateService(
                ViewModel.GeksaVpnController);
            var viewModel = new GeksaUpdateViewModel(_gekUpdateService);
            var window = new GeksaUpdateWindow(viewModel, checkImmediately);
            await window.ShowDialog(this);
        }
        finally
        {
            _gekUpdateDialogGate.Release();
        }
    }

    private async Task MonitorGeksaUpdatesLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var lastCheckUtc = DateTime.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (ViewModel is not null && _config.UiItem.GeksaAutoCheckUpdatesEnabled)
                {
                    var intervalHours = Math.Clamp(_config.UiItem.GeksaCheckUpdateIntervalHours, 1, 720);
                    var elapsed = DateTime.UtcNow - lastCheckUtc;
                    if (elapsed >= TimeSpan.FromHours(intervalHours))
                    {
                        lastCheckUtc = DateTime.UtcNow;

                        _gekUpdateService ??= new VelopackGeksaApplicationUpdateService(
                            ViewModel.GeksaVpnController);

                        if (_gekUpdateService.IsInstalled)
                        {
                            var checkResult = await _gekUpdateService.CheckAsync(cancellationToken);
                            if (checkResult == GeksaUpdateCheckResult.Available)
                            {
                                await Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    await ShowGeksaUpdateWindowAsync(checkImmediately: false);
                                });
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                Logging.SaveLog(nameof(MonitorGeksaUpdatesLoopAsync), exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async void BtnBackToGeksa_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.GeksaVpnController.RefreshAsync();
        }

        ShowAdvancedVpnSettings(false);
    }

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ??
                    (Utils.IsLinux() || Utils.IsMacOS()
                    ? (!AppManager.Instance.ShowInTaskbar ^ (WindowState == WindowState.Minimized))
                    : !AppManager.Instance.ShowInTaskbar);
        if (bl)
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }
        else
        {
            if (Utils.IsLinux() && _config.UiItem.Hide2TrayWhenClose == false)
            {
                WindowState = WindowState.Minimized;
                return;
            }

            foreach (var ownedWindow in OwnedWindows)
            {
                ownedWindow.Close();
            }
            Hide();
        }

        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
        RestoreUI();
        if (ViewModel?.GeksaShellViewModel != null)
        {
            _geksaTrayMenuManager = new GeksaTrayMenuManager(this, ViewModel, ViewModel.GeksaShellViewModel);
            if (!_subscriptionOnboardingStarted)
            {
                _subscriptionOnboardingStarted = true;
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        if (await ViewModel.GeksaShellViewModel.NeedsSubscriptionOnboardingAsync())
                        {
                            ShowHideWindow(true);
                        }
                        await ViewModel.GeksaShellViewModel.PromptForSubscriptionOnboardingAsync();
                        await ViewModel.GeksaShellViewModel.TryAutoConnectOnStartupAsync();
                    }
                    catch (Exception ex)
                    {
                        Logging.SaveLog(nameof(GeksaVpnSubscriptionWindow), ex);
                    }
                });
            }
        }
        _ = MonitorGeksaUpdatesLoopAsync(_updateLoopCts.Token);
    }

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain1.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain1.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain1.RowDefinitions[0].ActualHeight, gridMain1.RowDefinitions[2].ActualHeight);
        }
    }

    private void UpdateLayout(EGirdOrientation orientation)
    {
        var currentLayoutDisposables = new MultipleDisposable();
        _layoutBindingsDisposable.Create(currentLayoutDisposables);

        gridMain.IsVisible = orientation == EGirdOrientation.Horizontal;
        gridMain1.IsVisible = orientation == EGirdOrientation.Vertical;
        gridMain2.IsVisible = orientation == EGirdOrientation.Tab;

        switch (orientation)
        {
            case EGirdOrientation.Horizontal:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Vertical:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections1.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections1.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain1.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Tab:
            default:
                this.OneWayBind(ViewModel, vm => vm.ProfilesViewModel, v => v.tabProfiles2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.MsgViewModel, v => v.tabMsgView2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashProxiesViewModel, v => v.tabClashProxies2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ClashConnectionsViewModel, v => v.tabClashConnections2.Content).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections2.IsVisible).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain2.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;
        }

        RestoreUI();
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType is not ECoreType.v2fly
                        and not ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url?.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
    }

    private void MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag?.ToString());
        }
    }

    #endregion UI
}
