namespace ServiceLib.Trisko;

public sealed record TriskoWorkplace(Guid Id, string Name, IPAddress Address, int Port)
{
    public string Number => TriskoWorkNetwork.GetNumber(Id);
    public string DisplayName => $"{Name} · {Number}";
}

public sealed class TriskoWorkplaceDiscovery : IDisposable
{
    public const string MulticastAddress = "239.255.77.41";
    public const int DiscoveryPort = 47741;
    private const SocketOptionName WindowsUnicastInterface = (SocketOptionName)31; // Winsock IP_UNICAST_IF
    private static readonly IPEndPoint Destination = new(IPAddress.Parse(MulticastAddress), DiscoveryPort);
    private readonly IPNetwork _subnet;
    private readonly IPEndPoint _broadcast;
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly Socket _directSender = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly ConcurrentDictionary<(Guid Id, IPAddress Address), (TriskoWorkplace Peer, long Seen)> _peers = new();
    private readonly Guid _id;
    private readonly IPAddress _address;
    private readonly int _interfaceIndex;
    private readonly byte[] _query;
    private byte[]? _advertisement;
    private long? _lastProbe;

    public TriskoWorkplaceDiscovery(Guid id, IPAddress address, int interfaceIndex, IPNetwork subnet)
    {
        _id = id;
        _address = address;
        _interfaceIndex = interfaceIndex;
        _subnet = subnet;
        _broadcast = new(TriskoWorkNetwork.GetBroadcast(subnet), DiscoveryPort);
        _query = JsonSerializer.SerializeToUtf8Bytes(new Announcement(1, id, null, 0, Query: true));
        try
        {
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.PacketInformation, true);
            _socket.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Destination.Address, address));
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            _directSender.EnableBroadcast = true;
            _directSender.Bind(new IPEndPoint(address, 0));
            if (OperatingSystem.IsWindows())
                _directSender.SetSocketOption(SocketOptionLevel.IP, WindowsUnicastInterface,
                    IPAddress.HostToNetworkOrder(interfaceIndex));
            _ = ReceiveAsync();
        }
        catch
        {
            _socket.Dispose();
            _directSender.Dispose();
            throw;
        }
    }

    public async Task RefreshAsync(string? name, int port, IReadOnlyList<IPAddress> knownAddresses)
    {
        var message = name is null ? null : JsonSerializer.SerializeToUtf8Bytes(new Announcement(1, _id, name, port));
        Volatile.Write(ref _advertisement, message);
        if (message is not null)
            await Task.WhenAll(
                SendAsync(_socket, Destination, message),
                SendAsync(_directSender, _broadcast, message)).ConfigureAwait(false);

        var now = Environment.TickCount64;
        if (!OperatingSystem.IsWindows() || _lastProbe is { } lastProbe && now - lastProbe < 5_000)
            return;
        _lastProbe = now;
        // SoftEther can carry unicast even when group traffic does not reach a peer.
        // Reuse the adapter's neighbor cache without sweeping the whole subnet.
        await ProbeNeighborsAsync(knownAddresses).ConfigureAwait(false);
    }

    private async Task ProbeNeighborsAsync(IReadOnlyList<IPAddress> knownAddresses)
    {
        var neighbors = await ReadNeighborsAsync().ConfigureAwait(false);
        var candidates = knownAddresses.Concat(neighbors)
            .Where(address => _subnet.Contains(address) && !address.Equals(_subnet.BaseAddress) &&
                !address.Equals(_address) && !address.Equals(_broadcast.Address))
            .Distinct().Take(32);
        await Task.WhenAll(candidates.Select(address => SendAsync(_directSender, new IPEndPoint(address, DiscoveryPort), _query)))
            .ConfigureAwait(false);
    }

    private async Task<IEnumerable<IPAddress>> ReadNeighborsAsync()
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "arp.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            start.ArgumentList.Add("-a");
            start.ArgumentList.Add("-N");
            start.ArgumentList.Add(_address.ToString());
            using var process = Process.Start(start);
            if (process is null) return [];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            string output;
            try
            {
                output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill();
                return [];
            }
            if (process.ExitCode != 0) return [];

            return output.Split('\n').Select(line => line.Trim().Split(' ', '\t')[0])
                .Select(value => IPAddress.TryParse(value, out var address) ? address : IPAddress.None);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            Logging.SaveLog(nameof(TriskoWorkplaceDiscovery), ex);
            return [];
        }
    }

    private static async Task SendAsync(Socket socket, IPEndPoint destination, byte[] message)
    {
        try { await socket.SendToAsync(message, SocketFlags.None, destination).ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
        catch (SocketException ex) { Logging.SaveLog(nameof(TriskoWorkplaceDiscovery), ex); }
    }

    public TriskoWorkplace[] GetPeers()
    {
        foreach (var entry in _peers)
        {
            if (Environment.TickCount64 - entry.Value.Seen > 12_000) _peers.TryRemove(entry);
        }
        return _peers.Values.Select(value => value.Peer).OrderBy(peer => peer.Name).ThenBy(peer => peer.Number).ToArray();
    }

    private async Task ReceiveAsync()
    {
        var buffer = new byte[1024];
        EndPoint source = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            while (true)
            {
                SocketReceiveMessageFromResult packet;
                try
                {
                    packet = await _socket.ReceiveMessageFromAsync(buffer.AsMemory(), SocketFlags.None, source, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.MessageSize)
                {
                    continue;
                }
                var destination = packet.PacketInformation.Address;
                var unicast = destination.Equals(_address);
                if ((packet.SocketFlags & SocketFlags.Truncated) != 0 || packet.PacketInformation.Interface != _interfaceIndex ||
                    !(unicast || destination.Equals(Destination.Address) || destination.Equals(_broadcast.Address)) ||
                    packet.RemoteEndPoint is not IPEndPoint sender || !_subnet.Contains(sender.Address) ||
                    sender.Address.Equals(_address) || sender.Address.Equals(_broadcast.Address))
                    continue;
                if (ReadAnnouncement(buffer.AsSpan(0, packet.ReceivedBytes), _id) is not { } announcement)
                    continue;
                if (announcement.Query)
                {
                    if (unicast && Volatile.Read(ref _advertisement) is { } advertisement)
                        await SendAsync(_directSender, new IPEndPoint(sender.Address, DiscoveryPort), advertisement).ConfigureAwait(false);
                    continue;
                }
                var peer = new TriskoWorkplace(announcement.Id, announcement.Name!, sender.Address, announcement.Port);
                if (_peers.Count < 1024 || _peers.ContainsKey((peer.Id, peer.Address)))
                    _peers[(peer.Id, peer.Address)] = (peer, Environment.TickCount64);
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException ex)
        {
            if (!_socket.SafeHandle.IsClosed) Logging.SaveLog(nameof(TriskoWorkplaceDiscovery), ex);
        }
    }

    internal static Announcement? ReadAnnouncement(ReadOnlySpan<byte> packet, Guid localId)
    {
        Announcement? announcement;
        try { announcement = JsonSerializer.Deserialize<Announcement>(packet); }
        catch (JsonException) { return null; }

        if (announcement is not { Version: 1 } || announcement.Id == Guid.Empty || announcement.Id == localId)
            return null;
        if (announcement.Query)
            return announcement;
        return announcement is { Port: > 0 and <= 65535, Name.Length: > 0 and <= 128 } &&
            !announcement.Name.Any(char.IsControl) ? announcement : null;
    }

    public void Dispose()
    {
        _socket.Dispose();
        _directSender.Dispose();
    }

    internal sealed record Announcement(int Version, Guid Id, string? Name, int Port, bool Query = false);
}
