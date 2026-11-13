using System.IO.Pipes;
using System.Xml.Linq;

namespace ServiceLib.Geksa;

public sealed class WindowsGeksaVirtualHereService : IGeksaVirtualHereService, IDisposable
{
    private const string PipeName = "vhclient";
    private readonly SemaphoreSlim _ipcLock = new(1, 1);
    private readonly string _clientPath;
    private readonly string _configurationPath;
    private int? _clientProcessId;

    public WindowsGeksaVirtualHereService(string? clientPath = null)
    {
        _clientPath = clientPath ?? Path.Combine(
            AppContext.BaseDirectory,
            "Tools",
            "VirtualHere",
            "vhui64.exe");
        _configurationPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Trisko connect",
            "VirtualHere",
            "vhui.ini");
    }

    public async Task<IReadOnlyList<GeksaVirtualUsbDevice>> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureWindowsAndClientPresent();
        await EnsureClientRunningAsync(cancellationToken);
        var xml = await SendCommandAsync("GET CLIENT STATE", cancellationToken);
        return ParseClientState(xml);
    }

    public Task UseAsync(string address, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync($"USE,{ValidateAddress(address)}", TriskoLocalization.Text("подключить USB-устройство"), cancellationToken);

    public Task StopUsingAsync(string address, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync($"STOP USING,{ValidateAddress(address)}", TriskoLocalization.Text("отключить USB-устройство"), cancellationToken);

    public Task ToggleAutoUseDeviceAsync(string address, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync($"AUTO USE DEVICE,{ValidateAddress(address)}", TriskoLocalization.Text("изменить автоиспользование устройства"), cancellationToken);

    public Task ToggleAutoUsePortAsync(string address, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync($"AUTO USE PORT,{ValidateAddress(address)}", TriskoLocalization.Text("изменить автоиспользование USB-порта"), cancellationToken);

    public Task AddManualHubAsync(string endpoint, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            $"MANUAL HUB ADD,{ValidateHubEndpoint(endpoint)}",
            TriskoLocalization.Text("добавить сервер VirtualHere"),
            cancellationToken);

    public Task RemoveManualHubAsync(string endpoint, CancellationToken cancellationToken = default) =>
        ExecuteActionAsync(
            $"MANUAL HUB REMOVE,{ValidateHubEndpoint(endpoint)}",
            TriskoLocalization.Text("удалить сервер VirtualHere"),
            cancellationToken);

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var response = (await SendCommandAsync(
                "EXIT",
                cancellationToken,
                connectTimeoutMilliseconds: 500)).Trim();
            if (response.IsNotEmpty() && !response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                throw new GeksaVirtualHereException(
                    TriskoLocalization.Format("Не удалось завершить VirtualHere. Ответ клиента: {0}", NormalizeResponse(response)));
            }
            _clientProcessId = null;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            // Нет named pipe — клиент уже закрыт.
            _clientProcessId = null;
        }
    }

    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            await SendCommandAsync("GET CLIENT STATE", cancellationToken, connectTimeoutMilliseconds: 250);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return false;
        }
    }

    public void OpenClientWindow()
    {
        EnsureWindowsAndClientPresent();
        if (_clientProcessId is { } processId && SetClientWindowVisibility(processId, visible: true))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _clientPath,
            UseShellExecute = true
        });
    }

    public void Dispose() => _ipcLock.Dispose();

    internal static IReadOnlyList<GeksaVirtualUsbDevice> ParseClientState(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml, LoadOptions.None);
            var devices = new List<GeksaVirtualUsbDevice>();

            foreach (var server in document.Root?.Elements("server") ?? [])
            {
                var connection = server.Element("connection");
                if (connection is null)
                {
                    continue;
                }

                var hostname = Attribute(connection, "hostname");
                var port = Attribute(connection, "port");
                var serverName = Attribute(connection, "serverName");
                var serverAddress = port.IsNotEmpty() ? $"{hostname}:{port}" : hostname;
                var localConnectionUuid = Attribute(connection, "uuid");

                foreach (var element in server.Elements("device"))
                {
                    var deviceAddress = Attribute(element, "address");
                    if (hostname.IsNullOrEmpty() || deviceAddress.IsNullOrEmpty())
                    {
                        continue;
                    }

                    var stateValue = int.TryParse(Attribute(element, "state"), out var parsedState)
                        ? parsedState
                        : 0;
                    var isBound = (stateValue & 2) != 0;
                    var boundConnectionUuid = Attribute(element, "boundConnectionUUID");
                    var boundHostname = Attribute(element, "boundClientHostname");
                    var isLocal = isBound && (
                        boundConnectionUuid.IsNotEmpty() &&
                        string.Equals(boundConnectionUuid, localConnectionUuid, StringComparison.Ordinal) ||
                        boundHostname.IsNotEmpty() &&
                        string.Equals(boundHostname, Environment.MachineName, StringComparison.OrdinalIgnoreCase));

                    var state = (stateValue & 1) == 0
                        ? GeksaVirtualUsbState.Unavailable
                        : !isBound
                            ? GeksaVirtualUsbState.Available
                            : isLocal
                                ? GeksaVirtualUsbState.InUseLocally
                                : GeksaVirtualUsbState.InUseRemotely;

                    var vendor = Attribute(element, "vendor");
                    var product = Attribute(element, "product");
                    var nickname = Attribute(element, "nickname");
                    var displayName = nickname.IsNotEmpty()
                        ? nickname
                        : string.Join(' ', new[] { vendor, product }.Where(value => value.IsNotEmpty()));
                    if (displayName.IsNullOrEmpty())
                    {
                        displayName = TriskoLocalization.Format("USB-устройство {0}", deviceAddress);
                    }

                    var owner = Attribute(element, "clientId");
                    if (owner.IsNullOrEmpty())
                    {
                        owner = boundHostname;
                    }

                    devices.Add(new GeksaVirtualUsbDevice(
                        $"{hostname}.{deviceAddress}",
                        displayName,
                        vendor,
                        product,
                        serverName.IsNotEmpty() ? serverName : hostname,
                        serverAddress,
                        Attribute(element, "deviceSerial"),
                        state,
                        owner,
                        Attribute(element, "autoUse"),
                        string.Equals(Attribute(element, "firstInterfaceClass"), "8", StringComparison.Ordinal)));
                }
            }

            return devices
                .OrderByDescending(device => device.IsStorageDevice)
                .ThenBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is not GeksaVirtualHereException)
        {
            throw new GeksaVirtualHereException(
                TriskoLocalization.Text("VirtualHere вернул некорректный список USB-устройств."),
                ex);
        }
    }

    private async Task ExecuteActionAsync(
        string command,
        string actionDescription,
        CancellationToken cancellationToken)
    {
        EnsureWindowsAndClientPresent();
        await EnsureClientRunningAsync(cancellationToken);
        var response = (await SendCommandAsync(command, cancellationToken)).Trim();
        if (!response.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new GeksaVirtualHereException(
                TriskoLocalization.Format("Не удалось {0}. Ответ VirtualHere: {1}", actionDescription, NormalizeResponse(response)));
        }
    }

    private async Task EnsureClientRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SendCommandAsync("GET CLIENT STATE", cancellationToken, connectTimeoutMilliseconds: 250);
            return;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            Logging.SaveLog(nameof(WindowsGeksaVirtualHereService), ex);
        }

        EnsureBackgroundConfiguration();
        var startInfo = new ProcessStartInfo
        {
            FileName = _clientPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(_clientPath)!
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(_configurationPath);
        var clientProcess = Process.Start(startInfo);
        _clientProcessId = clientProcess?.Id;
        if (_clientProcessId is { } processId)
        {
            SetClientWindowVisibility(processId, visible: false);
        }
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(250, cancellationToken);
            try
            {
                await SendCommandAsync("GET CLIENT STATE", cancellationToken, connectTimeoutMilliseconds: 250);
                if (_clientProcessId is { } runningProcessId)
                {
                    SetClientWindowVisibility(runningProcessId, visible: false);
                }
                return;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                Logging.SaveLog(nameof(WindowsGeksaVirtualHereService), ex);
            }
        }

        throw new GeksaVirtualHereException(
            TriskoLocalization.Text("Клиент VirtualHere запущен, но не отвечает. Проверьте значок VirtualHere в области уведомлений Windows."));
    }

    private async Task<string> SendCommandAsync(
        string command,
        CancellationToken cancellationToken,
        int connectTimeoutMilliseconds = 2_000)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("VirtualHere named-pipe IPC is supported only on Windows.");
        }

        await _ipcLock.WaitAsync(cancellationToken);
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(connectTimeoutMilliseconds, cancellationToken);
            pipe.ReadMode = PipeTransmissionMode.Message;

            var request = Encoding.UTF8.GetBytes(command);
            await pipe.WriteAsync(request, cancellationToken);
            await pipe.FlushAsync(cancellationToken);

            using var response = new MemoryStream();
            var buffer = new byte[8 * 1024];
            do
            {
                var read = await pipe.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }
                await response.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            while (!pipe.IsMessageComplete);

            return Encoding.UTF8.GetString(response.ToArray()).TrimEnd('\0', '\r', '\n');
        }
        finally
        {
            _ipcLock.Release();
        }
    }

    private void EnsureWindowsAndClientPresent()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new GeksaVirtualHereException(TriskoLocalization.Text("Раздел ЭЦП с VirtualHere пока поддерживается только в Windows."));
        }
        if (!File.Exists(_clientPath))
        {
            throw new GeksaVirtualHereException(
                TriskoLocalization.Format("Встроенный клиент VirtualHere не найден: {0}", _clientPath));
        }
    }

    private void EnsureBackgroundConfiguration()
    {
        var directory = Path.GetDirectoryName(_configurationPath)!;
        Directory.CreateDirectory(directory);

        var lines = File.Exists(_configurationPath)
            ? File.ReadAllLines(_configurationPath).ToList()
            : [];
        SetGeneralSetting(lines, "AutoMinimize", "1");
        SetGeneralSetting(lines, "AutoFind", "1");
        SetGeneralSetting(lines, "Language", "RU-RU");
        File.WriteAllLines(_configurationPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static void SetGeneralSetting(List<string> lines, string key, string value)
    {
        var generalStart = lines.FindIndex(line =>
            string.Equals(line.Trim(), "[General]", StringComparison.OrdinalIgnoreCase));
        if (generalStart < 0)
        {
            if (lines.Count > 0 && lines[^1].IsNotEmpty())
            {
                lines.Add(string.Empty);
            }
            lines.Add("[General]");
            lines.Add($"{key}={value}");
            return;
        }

        var sectionEnd = lines.FindIndex(generalStart + 1, line =>
            line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        if (sectionEnd < 0)
        {
            sectionEnd = lines.Count;
        }

        for (var index = generalStart + 1; index < sectionEnd; index++)
        {
            var separatorIndex = lines[index].IndexOf('=');
            if (separatorIndex < 0 || !string.Equals(
                    lines[index][..separatorIndex].Trim(),
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines[index] = $"{key}={value}";
            return;
        }

        lines.Insert(sectionEnd, $"{key}={value}");
    }

    private static bool SetClientWindowVisibility(int processId, bool visible)
    {
        var foundWindow = false;
        NativeMethods.EnumWindows((windowHandle, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(windowHandle, out var windowProcessId);
            if (windowProcessId != processId)
            {
                return true;
            }
            if (visible && NativeMethods.GetWindowTextLength(windowHandle) == 0)
            {
                return true;
            }

            foundWindow = true;
            NativeMethods.ShowWindowAsync(
                windowHandle,
                visible ? NativeMethods.SwRestore : NativeMethods.SwHide);
            if (visible)
            {
                NativeMethods.SetForegroundWindow(windowHandle);
            }
            return true;
        }, IntPtr.Zero);
        return foundWindow;
    }

    private static class NativeMethods
    {
        internal const int SwHide = 0;
        internal const int SwRestore = 9;

        internal delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out int processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr windowHandle);
    }

    private static string ValidateAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.IndexOfAny([',', '\r', '\n']) >= 0)
        {
            throw new GeksaVirtualHereException(TriskoLocalization.Text("Получен некорректный адрес USB-устройства."));
        }
        return address;
    }

    private static string ValidateHubEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.IndexOfAny([',', '\r', '\n']) >= 0)
        {
            throw new GeksaVirtualHereException(TriskoLocalization.Text("Получен некорректный адрес сервера VirtualHere."));
        }
        return endpoint.Trim();
    }

    private static string Attribute(XElement element, string name) =>
        element.Attribute(name)?.Value?.Trim() ?? string.Empty;

    private static string NormalizeResponse(string response) =>
        response.IsNotEmpty() ? response.ReplaceLineEndings(" ") : TriskoLocalization.Text("нет ответа");
}
