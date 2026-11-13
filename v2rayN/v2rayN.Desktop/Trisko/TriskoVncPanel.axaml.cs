using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace v2rayN.Desktop.Trisko;

public partial class TriskoVncPanel : UserControl
{
    private readonly TriskoVncServerService _server = OperatingSystem.IsWindows() ? new() : null;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _busy;
    private bool _initialized;
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
        _statusTimer.Tick += async (_, _) => await RefreshStatusesAsync(false);
    }

    public void SetStatusMonitoring(bool enabled)
    {
        if (!enabled)
        {
            _statusTimer.Stop();
            return;
        }
        if (_statusTimer.IsEnabled) return;
        var initialize = !_initialized;
        _initialized = true;
        if (initialize) LoadClientSettings();
        _statusTimer.Start();
        _ = RefreshStatusesAsync(initialize);
    }

    private async Task RefreshStatusesAsync(bool loadSettings)
    {
        if (!_busy) await RefreshAsync(loadSettings);
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e) => await OpenSettingsAsync();

    private async void SetupIncoming_Click(object? sender, RoutedEventArgs e) => await OpenSettingsAsync(VncServerPanel);

    private async Task OpenSettingsAsync(Expander? section = null)
    {
        if (section is not null)
        {
            VncServerPanel.IsExpanded = section == VncServerPanel;
        }
        try
        {
            await TriskoRemoteSettingsWindow.ShowFormAsync(this, SettingsSlot, TriskoLocalization.Text("Настройки VNC"),
                TriskoLocalization.Text("Рабочая сеть, параметры подключения и доступ к этому компьютеру."));
        }
        finally
        {
            ServerPasswordBox.Text = "";
        }
    }

    private void LoadClientSettings()
    {
        try
        {
            if (!File.Exists(ClientSettingsPath)) return;
            var saved = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(ClientSettingsPath));
            if (saved == null) return;
            TriskoVncSettings.ValidateEndpoint(saved.Host, saved.Port);
            HostBox.Text = saved.Host; ClientPortBox.Text = saved.Port.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) { ClientStatus.Text = TriskoLocalization.Text("Не удалось прочитать последнее подключение: ") + ex.Message; }
    }

    private void Connect_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var input = HostBox.Text?.Trim() ?? "";
            var (host, port) = TriskoVncSettings.ResolveEndpoint(input, ParsePort(ClientPortBox.Text), null);
            var password = ClientPasswordBox.Text ?? "";
            if (string.IsNullOrEmpty(password)) throw new ArgumentException(TriskoLocalization.Text("Введите пароль удалённого сервера VNC."));
            if (password.Length > 8 || password.Any(c => c > 127))
                throw new ArgumentException(TriskoLocalization.Text("Эта версия поддерживает стандартный пароль VNC: до 8 символов ASCII."));
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var window = new TriskoVncSessionWindow(host, port, password);
            window.Show(owner);
            ClientPasswordBox.Text = "";
            ClientStatus.Text = "";
            try
            {
                File.WriteAllText(ClientSettingsPath, JsonSerializer.Serialize(new ClientSettings(host, port)));
            }
            catch (Exception ex) { ClientStatus.Text += TriskoLocalization.Text(" Не удалось запомнить адрес: ") + ex.Message; }
        }
        catch (Exception ex) { ClientStatus.Text = ex.Message; }
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
            await _server.EnableAsync(settings, ServerPasswordBox.Text ?? "");
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
                .FirstOrDefault(address => !address.StartsWith("169.254.", StringComparison.Ordinal));
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
            if (_lanAddress is { } lanAddress)
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
