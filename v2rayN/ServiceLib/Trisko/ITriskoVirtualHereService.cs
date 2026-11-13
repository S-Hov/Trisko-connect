namespace ServiceLib.Trisko;

public interface ITriskoVirtualHereService
{
    Task<IReadOnlyList<TriskoVirtualUsbDevice>> ScanAsync(CancellationToken cancellationToken = default);

    Task UseAsync(string address, CancellationToken cancellationToken = default);

    Task StopUsingAsync(string address, CancellationToken cancellationToken = default);

    Task ToggleAutoUseDeviceAsync(string address, CancellationToken cancellationToken = default);

    Task ToggleAutoUsePortAsync(string address, CancellationToken cancellationToken = default);

    Task AddManualHubAsync(string endpoint, CancellationToken cancellationToken = default);

    Task RemoveManualHubAsync(string endpoint, CancellationToken cancellationToken = default);

    Task ShutdownAsync(CancellationToken cancellationToken = default);

    Task<bool> IsRunningAsync(CancellationToken cancellationToken = default);

    void OpenClientWindow();
}

public enum TriskoVirtualUsbState
{
    Available,
    InUseLocally,
    InUseRemotely,
    Unavailable
}

public sealed record TriskoVirtualUsbDevice(
    string Address,
    string DisplayName,
    string Vendor,
    string Product,
    string ServerName,
    string ServerAddress,
    string SerialNumber,
    TriskoVirtualUsbState State,
    string InUseBy,
    string AutoUseMode,
    bool IsStorageDevice);

public sealed class TriskoVirtualHereHubConfiguration
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 7575;

    [JsonIgnore]
    public string Endpoint => Host.Contains(':')
        ? $"[{Host}]:{Port}"
        : $"{Host}:{Port}";

    public static bool TryCreate(
        string? host,
        string? portText,
        out TriskoVirtualHereHubConfiguration configuration,
        out string error)
    {
        configuration = new TriskoVirtualHereHubConfiguration();
        error = string.Empty;
        var normalizedHost = host?.Trim() ?? string.Empty;
        if (normalizedHost.IsNullOrEmpty())
        {
            error = TriskoLocalization.Text("Укажите IP-адрес или имя сервера VirtualHere.");
            return false;
        }
        if (normalizedHost.StartsWith('[') || normalizedHost.EndsWith(']'))
        {
            if (!normalizedHost.StartsWith('[') || !normalizedHost.EndsWith(']'))
            {
                error = TriskoLocalization.Text("IPv6-адрес указан некорректно.");
                return false;
            }
            normalizedHost = normalizedHost[1..^1];
        }
        if (normalizedHost.IndexOfAny([',', '\r', '\n', ' ', '\t', '/', '\\']) >= 0)
        {
            error = TriskoLocalization.Text("Адрес сервера содержит недопустимые символы.");
            return false;
        }
        if (normalizedHost.Contains(':') &&
            (!System.Net.IPAddress.TryParse(normalizedHost, out var ipAddress) ||
             ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6))
        {
            error = TriskoLocalization.Text("Порт указывается в отдельном поле. Для адреса с двоеточиями допустим только IPv6.");
            return false;
        }
        if (!int.TryParse(portText?.Trim(), out var port) || port is < 1 or > 65535)
        {
            error = TriskoLocalization.Text("Порт VirtualHere должен быть числом от 1 до 65535.");
            return false;
        }

        configuration.Host = normalizedHost;
        configuration.Port = port;
        return true;
    }
}

public sealed class TriskoVirtualHereException : Exception
{
    public TriskoVirtualHereException(string userMessage, Exception? innerException = null)
        : base(userMessage, innerException)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}
