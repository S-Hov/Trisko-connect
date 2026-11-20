using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoVncPanel : UserControl
{
    private readonly TriskoVncServerService _server = OperatingSystem.IsWindows() ? new() : null;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _busy;
    private bool _initialized;
    private bool _statusVisible;
    private bool _shellActive;
    private bool _refreshCycle;
    private bool _promptingForPassword;
    private bool _refreshing;
    private TriskoVncServerStatus? _serverStatus;
    private string? _lanAddress;
    private static string ClientSettingsPath => Utils.GetConfigPath("trisko-vnc-client.json");

    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<TriskoVncPanel, string>(nameof(StatusText), TriskoLocalization.Text("Доступ по адресу компьютера"));

    public string StatusText => GetValue(StatusTextProperty);

    public TriskoVncPanel()
    {
        InitializeComponent();
        InitializeWorkNetwork();
        if (OperatingSystem.IsWindows())
            HostBox.PlaceholderText = TriskoLocalization.Text("Номер, IP-адрес или имя компьютера");
        _statusTimer.Tick += async (_, _) => await RefreshStatusesAsync(false);
    }

    public void SetStatusMonitoring(bool enabled)
    {
        _statusVisible = enabled;
        UpdateStatusMonitoring();
    }

    public void SetWorkNetworkMonitoring(bool enabled)
    {
        _shellActive = enabled;
        UpdateStatusMonitoring();
    }

    private void UpdateStatusMonitoring()
    {
        if (!_statusVisible && !(_shellActive && _workOptions is not null))
        {
            _statusTimer.Stop();
            StopDiscovery();
            return;
        }

        var initialize = !_initialized;
        if (initialize)
        {
            _initialized = true;
            LoadHistory();
            LoadClientSettings();
        }

        if (!_statusTimer.IsEnabled)
        {
            _statusTimer.Start();
            _ = RefreshStatusesAsync(initialize);
        }
    }

    private async Task RefreshStatusesAsync(bool loadSettings)
    {
        if (_refreshCycle) return;
        _refreshCycle = true;
        try
        {
            if (!_busy) await RefreshAsync(loadSettings);
            await RefreshWorkNetworkAsync();
        }
        finally { _refreshCycle = false; }
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e) => await OpenSettingsAsync();

    private async void SetupIncoming_Click(object? sender, RoutedEventArgs e) => await OpenSettingsAsync(VncServerPanel);

    private async Task OpenSettingsAsync(Expander? section = null)
    {
        if (section is not null)
        {
            VncServerPanel.IsExpanded = section == VncServerPanel;
            WorkNetworkPanel.IsExpanded = section == WorkNetworkPanel;
        }
        LoadWorkNetworkFields();
        try
        {
            await TriskoRemoteSettingsWindow.ShowFormAsync(this, SettingsSlot, TriskoLocalization.Text("Настройки VNC"),
                TriskoLocalization.Text("Рабочая сеть, параметры подключения и доступ к этому компьютеру."), Notifications);
        }
        finally
        {
            ServerPasswordBox.Text = "";
            WorkPasswordBox.Text = "";
        }
    }

    private void LoadClientSettings()
    {
        try
        {
            if (_history.FirstOrDefault() is { } recent)
            {
                HostBox.Text = recent.Number ?? recent.Host;
                ClientPortBox.Text = recent.Port.ToString(CultureInfo.InvariantCulture);
                return;
            }

            // The previous client saved only the last address and port.
            if (!File.Exists(ClientSettingsPath)) return;
            var saved = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(ClientSettingsPath));
            if (saved == null) return;
            TriskoVncSettings.ValidateEndpoint(saved.Host, saved.Port);
            HostBox.Text = saved.Host; ClientPortBox.Text = saved.Port.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { ClientStatus.Text = TriskoLocalization.Text("Не удалось прочитать последнее подключение: ") + ex.Message; }
    }

    private TriskoNotificationCenter? Notifications => (DataContext as TriskoShellViewModel)?.Notifications;

    private void ReportError(string summary, Exception error)
    {
        ClientStatus.Text = $"{summary}: {error.Message}";
        Notifications?.Show(summary, error.Message, TriskoNotificationKind.Error, error.ToString());
    }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var input = HostBox.Text?.Trim() ?? "";
            var number = input.Replace(" ", "");
            if (OperatingSystem.IsWindows() && number.Length > 0 && number.All(char.IsAsciiDigit))
                await ConnectToAsync(ResolveWorkplace(number));
            else
            {
                var (host, port) = TriskoVncSettings.ResolveEndpoint(input, ParsePort(ClientPortBox.Text), null);
                await ConnectToAsync(new Destination(host, port));
            }
        }
        catch (Exception ex) { ReportError(TriskoLocalization.Text("Не удалось подключиться по VNC"), ex); }
    }

    private async Task ConnectToAsync(Destination target)
    {
        if (_promptingForPassword || TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            if (target is { Number: { } number, Id: { } id })
            {
                if (target.NetworkKey != _workOptions?.NetworkKey)
                    throw new InvalidOperationException(TriskoLocalization.Text("Подключите нужную рабочую сеть SoftEther."));
                target = ResolveWorkplace(number, id);
            }
            CheckWorkplaceRoute(target);
            await OpenSessionAsync(owner, target);
        }
        catch (Exception ex) { ReportError(TriskoLocalization.Text("Не удалось подключиться по VNC"), ex); }
    }

    private async Task OpenSessionAsync(Window owner, Destination target)
    {
        ClientStatus.Text = "";
        string? password = null;
        if (OperatingSystem.IsWindows())
        {
            try { password = WindowsTriskoResourceLauncher.ReadVncPassword(target.Key); }
            catch (System.ComponentModel.Win32Exception ex) { Logging.SaveLog("VNC credentials", ex); }
        }

        var usedSavedPassword = password is not null;
        var savePassword = false;
        if (password is null)
        {
            _promptingForPassword = true;
            try
            {
                var answer = await AskPasswordAsync(owner, target.Caption);
                if (answer is null) return;
                (password, savePassword) = answer.Value;
            }
            finally { _promptingForPassword = false; }
        }

        if (target.Number is { } number)
        {
            var live = ResolveWorkplace(number, target.Id);
            if (target.Key != live.Key || target.Host != live.Host || target.Port != live.Port)
                throw new InvalidOperationException(TriskoLocalization.Text("Адрес рабочего места изменился. Подключитесь повторно."));
            CheckWorkplaceRoute(target);
        }

        HostBox.Text = target.Number ?? target.Host;
        ClientPortBox.Text = target.Port.ToString(CultureInfo.InvariantCulture);
        var window = new TriskoVncSessionWindow(target.Host, target.Port, password, Notifications);
        window.Authenticated += secret =>
        {
            try
            {
                var remembered = _history.FirstOrDefault(target.Matches) is { } saved ? target.WithPresentation(saved) : target;
                SaveHistory(_history.Where(item => !target.Matches(item)).Prepend(remembered).ToList());
                if (savePassword && OperatingSystem.IsWindows())
                    WindowsTriskoResourceLauncher.SetVncPassword(target.Key, secret);
            }
            catch (Exception ex) { ReportError(TriskoLocalization.Text("Не удалось сохранить подключение"), ex); }
        };
        if (usedSavedPassword)
        {
            window.PasswordRejected += secret =>
            {
                if (!OperatingSystem.IsWindows()) return;
                try
                {
                    if (WindowsTriskoResourceLauncher.ReadVncPassword(target.Key) == secret)
                        WindowsTriskoResourceLauncher.SetVncPassword(target.Key, null);
                }
                catch (Exception ex) { ReportError(TriskoLocalization.Text("Не удалось удалить отклонённый пароль VNC"), ex); }
            };
        }
        window.Show(owner);
    }

    private async void EnableServer_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || _busy || _server == null) return;
        SetBusy(true);
        try
        {
            var settings = new TriskoVncServerSettings(ParsePort(ServerPortBox.Text),
                AutoStartBox.IsChecked == true, LoopbackBox.IsChecked == true, AllowedAddressesBox.Text ?? "");
            ServerStatus.Text = TriskoLocalization.Text("Настройка и запуск сервера…");
            await _server.EnableAsync(settings, ServerPasswordBox.Text ?? "", _workOptions?.Subnet);
            await RefreshAsync(true);
        }
        catch (Exception ex) { ServerStatus.Text = TriskoLocalization.Text("Не удалось включить доступ: ") + ex.Message; }
        finally { ServerPasswordBox.Text = ""; SetBusy(false); }
    }

    private async void DisableServer_Click(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || _busy || _server == null) return;
        SetBusy(true);
        try { await _server.DisableAsync(); await RefreshAsync(true); }
        catch (Exception ex) { ServerStatus.Text = TriskoLocalization.Text("Не удалось отключить доступ: ") + ex.Message; }
        finally { SetBusy(false); }
    }

    private async void RefreshServer_Click(object? sender, RoutedEventArgs e) => await RefreshAsync(true);

    private async Task RefreshAsync(bool loadSettings)
    {
        if (_refreshing) return;
        if (!OperatingSystem.IsWindows() || _server == null)
        {
            EnableServerButton.IsEnabled = DisableServerButton.IsEnabled = RefreshServerButton.IsEnabled = false;
            ServerStatus.Text = TriskoLocalization.Text("Настройка сервера доступна только на Windows. Клиент может подключаться к совместимым VNC-серверам.");
            return;
        }
        _refreshing = true;
        try
        {
            var status = await _server.GetStatusAsync();
            _serverStatus = status;
            if (loadSettings)
            {
                ServerPortBox.Text = status.Settings.Port.ToString(CultureInfo.InvariantCulture);
                AutoStartBox.IsChecked = status.Settings.AutoStart;
                LoopbackBox.IsChecked = status.Settings.LoopbackOnly;
                AllowedAddressesBox.Text = status.Settings.AllowedAddresses;
            }
            ServerStatus.Text = status.Running
                ? TriskoLocalization.Format("Доступ включён · порт {0} · ", status.Settings.Port) + (status.Settings.AutoStart ? TriskoLocalization.Text("автозапуск включён") : TriskoLocalization.Text("автозапуск выключен"))
                : status.Installed ? TriskoLocalization.Text("Доступ отключён. Служба установлена.") : TriskoLocalization.Text("Сервер не установлен. Он установится при включении доступа.");
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(adapter => (Type: adapter.NetworkInterfaceType, Properties: adapter.GetIPProperties()))
                .ToArray();
            var localAddresses = interfaces.SelectMany(adapter => GetIPv4Addresses(adapter.Properties)).Distinct();
            _lanAddress = interfaces
                .Where(adapter => adapter.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                    or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.FastEthernetT)
                .OrderByDescending(adapter => adapter.Properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(gateway.Address)))
                .SelectMany(adapter => GetIPv4Addresses(adapter.Properties))
                .FirstOrDefault(address => !address.StartsWith("169.254.", StringComparison.Ordinal) &&
                    _workOptions?.Subnet.Contains(IPAddress.Parse(address)) != true);
            LocalAddresses.Text = status.Settings.LoopbackOnly ? TriskoLocalization.Text("Адрес для SSH-туннеля: 127.0.0.1")
                : TriskoLocalization.Format("Имя компьютера: {0} · IP: {1}. Выберите адрес, доступный из вашей сети.", Dns.GetHostName(), string.Join(", ", localAddresses));
        }
        catch (Exception ex)
        {
            _serverStatus = null;
            ServerStatus.Text = TriskoLocalization.Text("Не удалось проверить сервер: ") + ex.Message;
        }
        finally
        {
            _refreshing = false;
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var accessText = _serverStatus switch
        {
            null => TriskoLocalization.Text("доступ · статус неизвестен"),
            { Running: false } => TriskoLocalization.Text("доступ выключен"),
            { Settings.LoopbackOnly: true } => TriskoLocalization.Text("доступ только с этого ПК"),
            _ => TriskoLocalization.Text("доступ включён")
        };
        SetCurrentValue(StatusTextProperty, accessText);

        string? identity = null;
        var label = TriskoLocalization.Text("Адрес этого ПК");
        if (_serverStatus is { Running: true, Settings.LoopbackOnly: false } server)
        {
            if (_workStatus is { Connected: true } && _deviceId != Guid.Empty)
            {
                identity = TriskoWorkNetwork.GetNumber(_deviceId);
                label = TriskoLocalization.Text("Номер этого компьютера");
            }
            else if (_lanAddress is { } lanAddress)
            {
                identity = server.Settings.Port == TriskoVncSettings.DefaultPort ? lanAddress : $"{lanAddress}:{server.Settings.Port}";
            }
        }
        WorkstationLabel.Text = label;
        WorkstationValue.Text = identity;
        WorkstationIdentity.IsVisible = identity is not null;
        SetupIncomingButton.IsVisible = identity is null;
    }

    private static IEnumerable<string> GetIPv4Addresses(IPInterfaceProperties properties) =>
        properties.UnicastAddresses.Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString());

    private void SetBusy(bool busy)
    {
        _busy = busy;
        EnableServerButton.IsEnabled = DisableServerButton.IsEnabled = RefreshServerButton.IsEnabled = !busy;
    }

    private static int ParsePort(string? text)
    {
        if (!int.TryParse(text, out var port)) throw new ArgumentException(TriskoLocalization.Text("Укажите порт числом от 1 до 65535."));
        TriskoVncSettings.ValidatePort(port); return port;
    }

    private sealed record ClientSettings(string Host, int Port);
}
