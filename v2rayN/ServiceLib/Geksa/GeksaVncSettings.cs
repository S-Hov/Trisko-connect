using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ServiceLib.Geksa;

public sealed record GeksaVncServerSettings(int Port = 5900, bool AutoStart = false,
    bool LoopbackOnly = false, string AllowedAddresses = "LocalSubnet");

public static class GeksaVncSettings
{
    public const int DefaultPort = 5900;

    public static (string Host, int Port) ResolveEndpoint(string input, int port, string? workAddress)
    {
        if (input.Contains(':') && IPEndPoint.TryParse(input, out var endpoint) && endpoint.Port > 0)
            return (endpoint.Address.ToString(), endpoint.Port);
        ValidateEndpoint(input, port);
        return (input, port);
    }

    public static bool AllowsWorkPeer(GeksaVncServerSettings settings, IPAddress peer)
    {
        return !settings.LoopbackOnly && settings.AllowedAddresses.Split(',', StringSplitOptions.TrimEntries)
            .Any(item => item.Equals("LocalSubnet", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(item, out var address) && peer.Equals(address)) ||
                (IPNetwork.TryParse(item, out var network) && network.Contains(peer)));
    }

    public static void ValidateEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 ||
            Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException(TriskoLocalization.Text("Укажите IP-адрес или имя компьютера без порта и протокола."));
        ValidatePort(port);
    }

    public static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentException(TriskoLocalization.Text("Порт должен быть от 1 до 65535."));
    }

    public static string NormalizeAllowedAddresses(string value)
    {
        var items = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0)
            throw new ArgumentException(TriskoLocalization.Text("Укажите разрешённые IP-адреса, подсети или LocalSubnet."));
        foreach (var item in items)
        {
            if (item.Equals("LocalSubnet", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = item.Split('/');
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out var address) ||
                (parts.Length == 2 && (!int.TryParse(parts[1], out var prefix) || prefix < 1 ||
                    prefix > (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128))))
                throw new ArgumentException(TriskoLocalization.Text("Разрешены IP-адреса, подсети вида 10.0.0.0/24 и LocalSubnet, через запятую."));
        }
        return string.Join(',', items);
    }

    public static void ValidatePassword(string password)
    {
        // Classic VNC authentication uses exactly eight bytes; never silently truncate a longer password.
        if (password.Length != 8 || password.Any(c => c < 33 || c > 126))
            throw new ArgumentException(TriskoLocalization.Text("Для совместимости VNC задайте ровно 8 символов: латиница, цифры или знаки без пробелов."));
    }

    public static string EncodePassword(string password)
    {
        ValidatePassword(password);
        byte[] clear = Encoding.ASCII.GetBytes(password);
        try
        {
            // VNC's fixed storage key, with bits reversed for the .NET DES implementation.
            using var des = DES.Create();
            des.Key = [0xE8, 0x4A, 0xD6, 0x60, 0xC4, 0x72, 0x1A, 0xE0];
            var encrypted = des.EncryptEcb(clear, PaddingMode.None);
            try
            {
                // Windows WritePrivateProfileStruct stores a trailing additive checksum.
                return Convert.ToHexString(encrypted) + ((byte)(encrypted.Sum(b => (int)b) & 255)).ToString("X2");
            }
            finally { CryptographicOperations.ZeroMemory(encrypted); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static string BuildServerConfiguration(GeksaVncServerSettings settings, string password)
    {
        ValidatePort(settings.Port);
        NormalizeAllowedAddresses(settings.AllowedAddresses);
        var encoded = EncodePassword(password);
        return $"""
            [admin]
            SocketConnect=1
            HTTPConnect=0
            AutoPortSelect=0
            PortNumber={settings.Port}
            AllowLoopback=1
            LoopbackOnly={(settings.LoopbackOnly ? 1 : 0)}
            AuthRequired=1
            MSLogonRequired=0
            RequireMSLogonIII=0
            NewMSLogon=0
            UseDSMPlugin=0
            QuerySetting=2
            QueryIfNoLogon=0
            FileTransferEnabled=0
            EnableRemoteInputs=1
            InputsEnabled=1
            LocalInputsDisabled=0
            RemoveWallpaper=0
            AllowProperties=0
            AllowEditClients=0
            AllowShutdown=0
            DebugMode=0
            Secure=0
            [ultravnc]
            passwd={encoded}
            """ + "\r\n";
    }
}
