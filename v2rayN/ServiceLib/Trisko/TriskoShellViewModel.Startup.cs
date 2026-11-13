namespace ServiceLib.Trisko;

public partial class TriskoShellViewModel
{
    private TriskoStartupSettingsService _startupSettings = null!;
    private readonly List<IDisposable> _startupSettingsSubscriptions = [];
    private int _startupConnectionAttempted;

    [Reactive] public partial bool RunAtLogin { get; set; }
    [Reactive] public partial bool AutoConnectVpnOnStartup { get; set; }
    [Reactive] public partial bool IsSavingStartupSettings { get; set; }
    public bool CanConfigureRunAtLogin => Utils.IsWindows();

    private void InitializeStartupSettings()
    {
        _startupSettings = new TriskoStartupSettingsService(_appConfig);
        RunAtLogin = _appConfig.GuiItem.AutoRun;
        AutoConnectVpnOnStartup = _appConfig.UiItem.TriskoAutoConnectVpnOnStartup;
        _startupSettingsSubscriptions.Add(this.WhenAnyValue(vm => vm.RunAtLogin).Skip(1)
            .Subscribe(enabled => { if (!IsSavingStartupSettings) _ = SaveStartupSettingAsync(true, enabled); }));
        _startupSettingsSubscriptions.Add(this.WhenAnyValue(vm => vm.AutoConnectVpnOnStartup).Skip(1)
            .Subscribe(enabled => { if (!IsSavingStartupSettings) _ = SaveStartupSettingAsync(false, enabled); }));
    }

    private async Task SaveStartupSettingAsync(bool runAtLogin, bool enabled)
    {
        IsSavingStartupSettings = true;
        try
        {
            if (runAtLogin) await _startupSettings.SetRunAtLoginAsync(enabled);
            else await _startupSettings.SetAutoConnectAsync(enabled);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(SaveStartupSettingAsync), ex);
            ReportOperationFailure(ex is TriskoConfigurationException configurationException
                ? configurationException.UserMessage : TriskoLocalization.Text("Не удалось изменить настройки запуска."), ex);
        }
        finally
        {
            RunAtLogin = _appConfig.GuiItem.AutoRun;
            AutoConnectVpnOnStartup = _appConfig.UiItem.TriskoAutoConnectVpnOnStartup;
            IsSavingStartupSettings = false;
        }
    }

    public async Task TryAutoConnectOnStartupAsync()
    {
        if (!await _initializationCompleted.Task) return;
        if (Interlocked.Exchange(ref _startupConnectionAttempted, 1) != 0
            || !AutoConnectVpnOnStartup || IsBusy || _controller.Snapshot.IsCoreRunning)
            return;
        if (!CanConnect)
        {
            Notifications.Show(TriskoLocalization.Text("Автоподключение VPN"), TriskoLocalization.Text("Не найден доступный выбранный сервер. Настройте VPN-подписку и подключитесь вручную."), TriskoNotificationKind.Warning);
            return;
        }
        await ConnectAsync();
    }

    public void RefreshStartupSettings()
    {
        if (IsSavingStartupSettings) return;
        IsSavingStartupSettings = true;
        try
        {
            RunAtLogin = _appConfig.GuiItem.AutoRun;
            AutoConnectVpnOnStartup = _appConfig.UiItem.TriskoAutoConnectVpnOnStartup;
        }
        finally { IsSavingStartupSettings = false; }
    }
}
