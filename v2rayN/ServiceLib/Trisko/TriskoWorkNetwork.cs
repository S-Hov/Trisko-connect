using System.Buffers.Binary;
using System.Globalization;

namespace ServiceLib.Trisko;

public sealed record TriskoWorkNetworkOptions(string Host, int Port, string Hub, string Username, string Cidr, bool AutoConnect)
{
    public const string DefaultCidr = "10.77.0.0/16";
    public const int DefaultPort = 5555;

    [JsonIgnore]
    public IPNetwork Subnet => IPNetwork.Parse(Cidr);
    internal bool IsIpAddress => IPAddress.TryParse(Host, out _);
    internal string Server => $"{Host}:{Port}";
    [JsonIgnore]
    public string NetworkKey => $"{Server.ToLowerInvariant()}/{Hub.ToUpperInvariant()}/{Cidr}";

    public static TriskoWorkNetworkOptions Create(string? host, string? port, string? hub, string? user,
        string? cidr, bool autoConnect)
    {
        var address = host?.Trim() ?? "";
        if (Uri.CheckHostName(address) is not (UriHostNameType.Dns or UriHostNameType.IPv4) ||
            address.Length > 253 || address.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-')))
            throw new ArgumentException(TriskoLocalization.Text("Укажите имя сервера SoftEther или его IPv4-адрес."));
        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var portNumber) || portNumber is < 1 or > 65535)
            throw new ArgumentException(TriskoLocalization.Text("Порт должен быть от 1 до 65535."));

        var hubName = hub?.Trim() ?? "";
        var userName = user?.Trim() ?? "";
        if (hubName.Length is < 1 or > 255 || userName.Length is < 1 or > 255 ||
            hubName.Concat(userName).Any(c => char.IsControl(c) || c == '"'))
            throw new ArgumentException(TriskoLocalization.Text("Укажите виртуальный хаб и имя пользователя SoftEther."));
        if (!IPNetwork.TryParse(cidr?.Trim(), out var subnet) || !TriskoWorkNetwork.IsPrivateSubnet(subnet))
            throw new ArgumentException(TriskoLocalization.Text("Укажите частную IPv4-подсеть с префиксом от /16 до /24."));

        var normalizedHost = IPAddress.TryParse(address, out var ip) ? ip.ToString() : address.ToLowerInvariant().TrimEnd('.');
        return new(normalizedHost, portNumber, hubName, userName, subnet.ToString(), autoConnect);
    }

    public static TriskoWorkNetworkOptions? Validated(TriskoWorkNetworkOptions? options)
    {
        if (options is null) return null;
        try { return Create(options.Host, options.Port.ToString(CultureInfo.InvariantCulture), options.Hub,
            options.Username, options.Cidr, options.AutoConnect); }
        catch (ArgumentException) { return null; }
    }

    public bool UsesSameRoute(TriskoWorkNetworkOptions other) =>
        Host == other.Host && Port == other.Port && Cidr == other.Cidr;

    internal bool UsesSameAccount(TriskoWorkNetworkOptions other) =>
        Host == other.Host && Port == other.Port && Hub.Equals(other.Hub, StringComparison.OrdinalIgnoreCase)
        && Username.Equals(other.Username, StringComparison.OrdinalIgnoreCase);
}

public static class TriskoWorkNetwork
{
    private static readonly IPNetwork[] PrivateNetworks =
        [IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("192.168.0.0/16")];

    internal static bool IsPrivateSubnet(IPNetwork subnet) =>
        subnet.BaseAddress.AddressFamily == AddressFamily.InterNetwork &&
        subnet.PrefixLength is >= 16 and <= 24 &&
        PrivateNetworks.Any(network => network.Contains(subnet.BaseAddress));

    public static IPAddress GetBroadcast(IPNetwork subnet) =>
        ToAddress(ToNumber(subnet.BaseAddress) | HostMask(subnet));

    internal static IPAddress GetMask(IPNetwork subnet) => ToAddress(~HostMask(subnet));

    public static IPAddress GetAddress(Guid id, IPNetwork subnet)
    {
        var hash = SHA256.HashData(id.ToByteArray());
        var hostMask = HostMask(subnet);
        // Keep the bottom /24 of a /16 free, as in the original implementation.
        var firstHost = subnet.PrefixLength == 16 ? 256u : 2u;
        var host = BinaryPrimitives.ReadUInt32BigEndian(hash) % (hostMask - firstHost) + firstHost;
        return ToAddress(ToNumber(subnet.BaseAddress) | host);
    }

    public static string GetNumber(Guid id)
    {
        var hash = SHA256.HashData(id.ToByteArray());
        var value = BinaryPrimitives.ReadUInt32BigEndian(hash);
        return (value % 900_000_000 + 100_000_000).ToString(CultureInfo.InvariantCulture);
    }

    private static uint HostMask(IPNetwork subnet) => uint.MaxValue >> subnet.PrefixLength;
    private static uint ToNumber(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static IPAddress ToAddress(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    public static Guid LoadDeviceId(string path)
    {
        if (File.Exists(path)) return Guid.Parse(File.ReadAllText(path));
        var id = Guid.NewGuid();
        var temporary = path + "." + id.ToString("N");
        try
        {
            File.WriteAllText(temporary, id.ToString("D"));
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { return Guid.Parse(File.ReadAllText(path)); }
            return id;
        }
        finally { File.Delete(temporary); }
    }
}
