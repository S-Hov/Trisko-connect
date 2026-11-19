using System.Net;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoVncPanel
{
    private readonly TriskoSoftEtherService? _workNetwork = OperatingSystem.IsWindows() ? new() : null;
    private readonly SemaphoreSlim _workGate = new(1, 1);
    private TriskoWorkNetworkOptions? _workOptions;
    private TriskoSoftEtherStatus? _workStatus;
    private TriskoWorkplaceDiscovery? _discovery;
    private (IPAddress Address, int Index, string Network)? _discoveryBinding;
    private string? _discoveryFirewallCidr;
    private string? _lastWorkError;
    private (long Version, int Port)? _workTransport;
    private (long Version, int Port)? _autoAttempt;
    private Guid _deviceId;

    private void InitializeWorkNetwork()
    {
        if (_workNetwork is null) return;
        _workOptions = TriskoWorkNetworkOptions.Validated(AppManager.Instance.Config?.UiItem?.TriskoWorkNetwork);
        WorkNetworkPanel.IsVisible = QuickWorkButton.IsVisible = true;
        UpdateWorkControls();
    }

    private void LoadWorkNetworkFields()
    {
        WorkHostBox.Text = _workOptions?.Host;
        WorkPortBox.Text = (_workOptions?.Port ?? TriskoWorkNetworkOptions.DefaultPort).ToString(CultureInfo.InvariantCulture);
        WorkHubBox.Text = _workOptions?.Hub;
        WorkUsernameBox.Text = _workOptions?.Username;
        WorkSubnetBox.Text = _workOptions?.Cidr ?? TriskoWorkNetworkOptions.DefaultCidr;
        AutoWorkBox.IsChecked = _workOptions?.AutoConnect ?? false;
    }

    private (long Version, int Port)? CurrentTransport()
    {
        var core = CoreManager.Instance;
        return core.IsCoreRunning && core.ActiveLocalProxyPort is { } port &&
            core.ActiveWorkNetwork is { } active && _workOptions is { } options && active.UsesSameRoute(options)
            ? (core.ActiveWorkNetworkTransportVersion, port) : null;
    }

    private async Task SaveWorkNetworkAsync()
    {
        var options = TriskoWorkNetworkOptions.Create(WorkHostBox.Text, WorkPortBox.Text, WorkHubBox.Text,
            WorkUsernameBox.Text, WorkSubnetBox.Text, AutoWorkBox.IsChecked == true);
        if (options == _workOptions) return;
        if (_workStatus is { Connected: true } && _workOptions is { } activeOptions &&
            (options with { AutoConnect = activeOptions.AutoConnect }) != activeOptions)
            throw new InvalidOperationException(TriskoLocalization.Text("Сначала отключите рабочую сеть SoftEther."));

        var config = AppManager.Instance.Config;
        var previous = config.UiItem.TriskoWorkNetwork;
        config.UiItem.TriskoWorkNetwork = options;
        if (await ConfigHandler.SaveConfig(config) != 0)
        {
            config.UiItem.TriskoWorkNetwork = previous;
            throw new IOException(TriskoLocalization.Text("Не удалось сохранить настройки рабочей сети."));
        }

        _workOptions = options;
        _autoAttempt = null;
        UpdateStatusMonitoring();
        WorkNetworkFeedback.Text = CoreManager.Instance.IsCoreRunning && CurrentTransport() is null
            ? TriskoLocalization.Text("Переподключите VPN, чтобы применить настройки рабочей сети.")
            : TriskoLocalization.Text("Настройки рабочей сети сохранены.");
    }

    private async void SaveWork_Click(object? sender, RoutedEventArgs e) => await RunWorkActionAsync(
        TriskoLocalization.Text("Не удалось сохранить настройки рабочей сети."), SaveWorkNetworkAsync);

    private async void ConnectWork_Click(object? sender, RoutedEventArgs e) => await ConnectWorkManuallyAsync(fromSettings: true);

    private async void DisconnectWork_Click(object? sender, RoutedEventArgs e) => await DisconnectWorkManuallyAsync();

    private async void QuickWork_Click(object? sender, RoutedEventArgs e)
    {
        if (_workStatus is { Connected: true })
            await DisconnectWorkManuallyAsync();
        else if (_workStatus is { Configured: true })
            await ConnectWorkManuallyAsync(fromSettings: false);
        else
            await OpenSettingsAsync(WorkNetworkPanel);
    }

    private void UseWorkSubnet_Click(object? sender, RoutedEventArgs e)
    {
        if (_workOptions is { } options) AllowedAddressesBox.Text = options.Cidr;
    }

    private Task ConnectWorkManuallyAsync(bool fromSettings) => RunWorkActionAsync(
        TriskoLocalization.Text("Не удалось подключить рабочую сеть"), async () =>
        {
            if (!OperatingSystem.IsWindows() || _workNetwork is null) return;
            if (_workStatus is not { Installed: true })
            {
                WorkNetworkStatus.Text = TriskoLocalization.Text("Установка SoftEther VPN Client…");
                await _workNetwork.InstallAsync();
                return;
            }

            var password = fromSettings ? WorkPasswordBox.Text ?? "" : "";
            WorkPasswordBox.Text = "";
            if (fromSettings) await SaveWorkNetworkAsync();
            if (!CoreManager.Instance.IsCoreRunning && DataContext is TriskoShellViewModel shell)
                await shell.StartWorkNetworkVpnAsync();
            if (CurrentTransport() is not { } transport)
                throw new InvalidOperationException(CoreManager.Instance.IsCoreRunning
                    ? TriskoLocalization.Text("Переподключите VPN, чтобы применить настройки рабочей сети.")
                    : TriskoLocalization.Text("Сначала подключите VPN."));

            await ConnectWorkAsync(transport, password);
        });

    private Task DisconnectWorkManuallyAsync() => RunWorkActionAsync(
        TriskoLocalization.Text("Не удалось отключить рабочую сеть"), async () =>
        {
            if (!OperatingSystem.IsWindows() || _workNetwork is null) return;
            _autoAttempt = CurrentTransport();
            await DisconnectWorkAsync();
        });

    private async Task RunWorkActionAsync(string operation, Func<Task> action)
    {
        if (!OperatingSystem.IsWindows() || _workNetwork is null) return;
        // Queue behind an in-progress status read, but never behind another user action.
        if (!WorkNetworkPanel.IsEnabled) return;
        WorkNetworkPanel.IsEnabled = QuickWorkButton.IsEnabled = false;
        await _workGate.WaitAsync();
        try
        {
            WorkNetworkFeedback.Text = "";
            await action();
        }
        catch (Exception ex)
        {
            WorkNetworkFeedback.Text = ex.Message;
            ReportError(operation, ex);
        }
        finally
        {
            try { _workStatus = await _workNetwork.GetStatusAsync(_workOptions); }
            catch (Exception ex) { _workStatus = null; LogWorkPollError(ex); }
            _workGate.Release();
            WorkNetworkPanel.IsEnabled = QuickWorkButton.IsEnabled = true;
            UpdateWorkControls();
        }

        await RefreshWorkNetworkAsync();
    }

    [SupportedOSPlatform("windows")]
    private async Task ConnectWorkAsync((long Version, int Port) transport, string password)
    {
        var options = _workOptions ?? throw new InvalidOperationException(
            TriskoLocalization.Text("Сначала настройте рабочую сеть SoftEther."));
        _autoAttempt = transport;
        WorkNetworkStatus.Text = TriskoLocalization.Text("Подключение рабочей сети…");
        UseDeviceId();
        await _workNetwork!.ConnectAsync(options, _deviceId, transport.Port, password);
        if (CurrentTransport() != transport)
        {
            await DisconnectWorkAsync();
            return;
        }

        _workTransport = transport;
        _workStatus = await _workNetwork.GetStatusAsync(options);
        WorkNetworkFeedback.Text = "";
    }

    [SupportedOSPlatform("windows")]
    private async Task DisconnectWorkAsync()
    {
        StopDiscovery();
        await _workNetwork!.DisconnectAsync();
        _workTransport = null;
        _workStatus = await _workNetwork.GetStatusAsync(_workOptions);
    }

    private async Task RefreshWorkNetworkAsync()
    {
        if (!OperatingSystem.IsWindows() || _workNetwork is null || !_statusTimer.IsEnabled ||
            !await _workGate.WaitAsync(0)) return;
        try
        {
            _workStatus = null;
            _workStatus = await _workNetwork.GetStatusAsync(_workOptions);
            var transport = CurrentTransport();
            if (_workStatus.Connected && (_workTransport is null || _workTransport != transport))
                await DisconnectWorkAsync();

            if (transport is null) _autoAttempt = null;
            else if (_workOptions is { AutoConnect: true } && _workStatus is { Configured: true, Connected: false } &&
                _autoAttempt != transport)
            {
                try { await ConnectWorkAsync(transport.Value, ""); }
                catch (Exception ex)
                {
                    WorkNetworkFeedback.Text = ex.Message;
                    ReportError(TriskoLocalization.Text("Не удалось подключить рабочую сеть"), ex);
                }
            }

            await RefreshDiscoveryAsync();
            _lastWorkError = null;
        }
        catch (Exception ex)
        {
            StopDiscovery();
            LogWorkPollError(ex);
        }
        finally
        {
            _workGate.Release();
            UpdateWorkControls();
        }
    }

    private void LogWorkPollError(Exception error)
    {
        if (_lastWorkError == error.Message) return;
        _lastWorkError = error.Message;
        Logging.SaveLog(nameof(RefreshWorkNetworkAsync), error);
    }

    private void UseDeviceId()
    {
        if (_deviceId == Guid.Empty)
            _deviceId = TriskoWorkNetwork.LoadDeviceId(Utils.GetConfigPath("trisko-workplace-id.txt"));
    }

    [SupportedOSPlatform("windows")]
    private async Task RefreshDiscoveryAsync()
    {
        if (!_statusTimer.IsEnabled || _workOptions is not { } options ||
            _workStatus is not { Connected: true, Address: { } address } ||
            TriskoSoftEtherService.FindAdapter()?.GetIPProperties().GetIPv4Properties() is not { } adapter)
        {
            StopDiscovery();
            return;
        }

        UseDeviceId();
        var binding = (Address: IPAddress.Parse(address), Index: adapter.Index, Network: options.NetworkKey);
        if (!_discoveryBinding.Equals(binding))
        {
            StopDiscovery();
            if (_discoveryFirewallCidr != options.Cidr)
            {
                await TriskoVncServerService.AllowDiscoveryAsync(options.Subnet);
                _discoveryFirewallCidr = options.Cidr;
            }

            if (!_statusTimer.IsEnabled || CurrentTransport() != _workTransport) return;
            _discovery = new TriskoWorkplaceDiscovery(_deviceId, binding.Address, binding.Index, options.Subnet);
            _discoveryBinding = binding;
        }

        if (_discovery is not { } discovery) return;
        var known = _history.Where(peer => peer.NetworkKey == options.NetworkKey)
            .Select(peer => IPAddress.TryParse(peer.Host, out var ip) ? ip : IPAddress.None).Distinct().Take(32).ToArray();
        if (_serverStatus is { Running: true, Settings.LoopbackOnly: false } server)
            await discovery.RefreshAsync(Dns.GetHostName(), server.Settings.Port, known);
        else
            await discovery.RefreshAsync(null, 0, known);
        if (_discovery == discovery) ShowWorkplaces(discovery.GetPeers());
    }

    private void StopDiscovery()
    {
        _discovery?.Dispose();
        _discovery = null;
        _discoveryBinding = null;
        ShowWorkplaces([]);
    }

    private void UpdateWorkControls()
    {
        var connected = _workStatus is { Connected: true };
        var installed = _workStatus is { Installed: true };
        var reportedError = _lastWorkError;
        WorkNetworkStatus.Text = reportedError ?? (_workStatus switch
        {
            { Connected: true, Address: { } address } => TriskoLocalization.Format("Рабочая сеть подключена · IP {0}", address),
            { Installed: true } => TriskoLocalization.Text("Рабочая сеть отключена."),
            _ => TriskoLocalization.Text("Для рабочей сети нужен SoftEther VPN Client.")
        });
        QuickWorkButton.Content = connected ? TriskoLocalization.Text("Отключить рабочую сеть")
            : TriskoLocalization.Text("Подключить рабочую сеть");
        ConnectWorkButton.Content = installed ? TriskoLocalization.Text("Подключить рабочую сеть")
            : TriskoLocalization.Text("Установить SoftEther");
        ConnectWorkButton.IsEnabled = !connected;
        DisconnectWorkButton.IsEnabled = connected;
        WorkHostBox.IsEnabled = WorkPortBox.IsEnabled = WorkHubBox.IsEnabled = WorkUsernameBox.IsEnabled =
            WorkSubnetBox.IsEnabled = WorkPasswordBox.IsEnabled = !connected;
        UseWorkSubnetButton.IsVisible = _workOptions is not null;
        DiscoveryPanel.IsVisible = connected;
        UpdateSummary();
    }
}
