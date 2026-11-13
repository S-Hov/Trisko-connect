namespace ServiceLib.Geksa;

public partial class GeksaShellViewModel
{
    private GeksaStartupSettingsService _startupSettings = null!;
    private readonly List<IDisposable> _startupSettingsSubscriptions = [];
    private int _startupConnectionAttempted;

    [Reactive] public partial bool RunAtLogin { get; set; }
    [Reactive] public partial bool AutoConnectVpnOnStartup { get; set; }
    [Reactive] public partial bool IsSavingStartupSettings { get; set; }
    public bool CanConfigureRunAtLogin => Utils.IsWindows();

    private void InitializeStartupSettings()
    {
        _startupSettings = new GeksaStartupSettingsService(_appConfig);
        RunAtLogin = _appConfig.GuiItem.AutoRun;
        AutoConnectVpnOnStartup = _appConfig.UiItem.GeksaAutoConnectVpnOnStartup;
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
            ReportOperationFailure(ex is GeksaConfigurationException configurationException
                ? configurationException.UserMessage : TriskoLocalization.Text("Не удалось изменить настройки запуска."), ex);
        }
        finally
        {
            RunAtLogin = _appConfig.GuiItem.AutoRun;
            AutoConnectVpnOnStartup = _appConfig.UiItem.GeksaAutoConnectVpnOnStartup;
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
            Notifications.Show(TriskoLocalization.Text("Автоподключение VPN"), TriskoLocalization.Text("Не найден доступный выбранный сервер. Настройте VPN-подписку и подключитесь вручную."), GeksaNotificationKind.Warning);
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
            AutoConnectVpnOnStartup = _appConfig.UiItem.GeksaAutoConnectVpnOnStartup;
        }
        finally { IsSavingStartupSettings = false; }
    }
}
