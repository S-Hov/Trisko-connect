namespace ServiceLib.Geksa;

/// <summary>
/// Selects the underlay interface used to reach the corporate VPN endpoint.
/// Other VPN and virtual adapters are only inspected; they are never changed.
/// </summary>
public sealed class GeksaNetworkBindingSelector
{
    private static readonly TimeSpan ProbeBudget = TimeSpan.FromMilliseconds(700);

    private readonly Func<IReadOnlyList<GeksaNetworkInterfaceCandidate>> _candidateProvider;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> _addressResolver;
    private readonly Func<GeksaNetworkInterfaceCandidate, IReadOnlyList<IPAddress>, int, CancellationToken,
        Task<GeksaNetworkProbeResult?>> _probe;

    public GeksaNetworkBindingSelector()
        : this(GetSystemCandidates, ResolveIPv4AddressesAsync, ProbeAsync)
    {
    }

    public GeksaNetworkBindingSelector(
        Func<IReadOnlyList<GeksaNetworkInterfaceCandidate>> candidateProvider,
        Func<string, CancellationToken, Task<IReadOnlyList<IPAddress>>> addressResolver,
        Func<GeksaNetworkInterfaceCandidate, IReadOnlyList<IPAddress>, int, CancellationToken,
            Task<GeksaNetworkProbeResult?>> probe)
    {
        _candidateProvider = candidateProvider ?? throw new ArgumentNullException(nameof(candidateProvider));
        _addressResolver = addressResolver ?? throw new ArgumentNullException(nameof(addressResolver));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public IReadOnlyList<GeksaNetworkInterfaceOption> GetAvailableOptions()
    {
        var options = _candidateProvider()
            .GroupBy(candidate => candidate.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var addresses = group
                    .Select(candidate => candidate.LocalAddress.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                var first = group.OrderByDescending(candidate => candidate.HasDefaultGateway).First();
                return new GeksaNetworkInterfaceOption(
                    first.InterfaceName,
                    $"{first.InterfaceName}  ·  {string.Join(", ", addresses)}",
                    IsAutomatic: false);
            })
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        options.Insert(0, GeksaNetworkInterfaceOption.Automatic);
        return options;
    }

    public Config CreateSpeedtestConfiguration(Config config)
    {
        var requiredInterface = config.TunModeItem.GeksaAutoSelectInterface
            ? null : config.TunModeItem.GeksaPreferredInterface?.Trim();
        if (!config.TunModeItem.GeksaAutoSelectInterface && string.IsNullOrWhiteSpace(requiredInterface))
            throw new GeksaVpnOperationException("networkInterfaceSelectionInvalid",
                TriskoLocalization.Text("Ручной режим включён, но сетевой адаптер не выбран."), retryable: false);

        // Refresh the underlay independently of server reachability: UDP-only or temporarily
        // unavailable endpoints must not prevent the remaining profiles from being tested.
        var candidate = _candidateProvider()
            .Where(item => IsUsableLocalAddress(item.LocalAddress))
            .Where(item => requiredInterface is not null
                ? item.InterfaceName.Equals(requiredInterface, StringComparison.OrdinalIgnoreCase)
                : item.HasDefaultGateway || IsPointToPointUnderlay(item.InterfaceType))
            .OrderByDescending(item => item.HasDefaultGateway)
            .ThenByDescending(item => GetInterfacePriority(item.InterfaceType))
            .ThenByDescending(item => item.Speed)
            .ThenBy(item => item.InterfaceIndex)
            .ThenBy(item => item.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (candidate is null)
        {
            if (requiredInterface is not null)
                throw new GeksaVpnOperationException("selectedNetworkInterfaceUnavailable",
                    TriskoLocalization.Format("Выбранный сетевой адаптер «{0}» сейчас недоступен. Выберите другой адаптер или включите автоматический режим.", requiredInterface));
            throw NoUnderlayInterface();
        }

        var snapshot = JsonUtils.DeepCopy(config)
            ?? throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось подготовить настройки проверки задержки."));
        snapshot.CoreBasicItem.BindInterface = candidate.InterfaceName;
        snapshot.CoreBasicItem.SendThrough = candidate.LocalAddress.ToString();
        snapshot.TunModeItem.EnableTun = false;
        snapshot.TunModeItem.PreserveOutboundInterfaceBinding = true;
        return snapshot;
    }

    public async Task<GeksaNetworkBinding> SelectAsync(
        string endpointHost,
        int endpointPort,
        string? requiredInterfaceName,
        CancellationToken cancellationToken)
    {
        if (endpointHost.IsNullOrEmpty() || endpointPort is <= 0 or > 65535)
        {
            throw new GeksaVpnOperationException(
                "endpointInvalid",
                TriskoLocalization.Text("В VPN-профиле указан неправильный адрес или порт сервера."),
                retryable: false);
        }

        var remoteAddresses = await ResolveEndpointAsync(endpointHost.Trim(), cancellationToken);
        IReadOnlyList<GeksaNetworkInterfaceCandidate> candidates;
        try
        {
            candidates = _candidateProvider()
                .Where(candidate => candidate.LocalAddress.AddressFamily == AddressFamily.InterNetwork)
                .DistinctBy(candidate => new { candidate.InterfaceName, candidate.LocalAddress })
                .ToArray();
        }
        catch (Exception ex)
        {
            throw new GeksaVpnOperationException(
                "networkInterfaceInspectionFailed",
                TriskoLocalization.Text("Не удалось проверить сетевые подключения Windows."),
                innerException: ex);
        }

        if (!requiredInterfaceName.IsNullOrEmpty())
        {
            candidates = candidates
                .Where(candidate => candidate.InterfaceName.Equals(
                    requiredInterfaceName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (candidates.Count == 0)
            {
                throw new GeksaVpnOperationException(
                    "selectedNetworkInterfaceUnavailable",
                    TriskoLocalization.Format("Выбранный сетевой адаптер «{0}» сейчас недоступен. ", requiredInterfaceName) +
                    TriskoLocalization.Text("Выберите другой адаптер или включите автоматический режим."));
            }
        }

        if (candidates.Count == 0)
        {
            throw NoUnderlayInterface();
        }

        using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeTimeout.CancelAfter(ProbeBudget);

        var successful = (await Task.WhenAll(candidates.Select(async candidate =>
            {
                try
                {
                    return await _probe(candidate, remoteAddresses, endpointPort, probeTimeout.Token);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(GeksaNetworkBindingSelector), ex);
                    return null;
                }
            })))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var selected = successful
            .Where(result =>
                !requiredInterfaceName.IsNullOrEmpty()
                || result.Candidate.HasDefaultGateway
                || IsPointToPointUnderlay(result.Candidate.InterfaceType))
            .OrderByDescending(result => result.Candidate.HasDefaultGateway)
            .ThenByDescending(result => GetInterfacePriority(result.Candidate.InterfaceType))
            .ThenByDescending(result => result.Candidate.Speed)
            .ThenBy(result => result.Candidate.InterfaceIndex)
            .ThenBy(result => result.Candidate.InterfaceName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selected is null)
        {
            if (!requiredInterfaceName.IsNullOrEmpty())
            {
                throw new GeksaVpnOperationException(
                    "selectedNetworkInterfaceUnreachable",
                    TriskoLocalization.Format("Через адаптер «{0}» VPN-сервер не отвечает. ", requiredInterfaceName) +
                    TriskoLocalization.Text("Выберите другой адаптер или включите автоматический режим."));
            }

            throw NoUnderlayInterface();
        }

        return new GeksaNetworkBinding(
            selected.Candidate.InterfaceName,
            selected.Candidate.LocalAddress,
            selected.RemoteAddress,
            selected.Candidate.InterfaceIndex);
    }

    private async Task<IReadOnlyList<IPAddress>> ResolveEndpointAsync(
        string endpointHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await _addressResolver(endpointHost, cancellationToken);
            var ipv4Addresses = addresses
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToArray();
            if (ipv4Addresses.Length > 0)
            {
                return ipv4Addresses;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new GeksaVpnOperationException(
                "endpointResolveFailed",
                TriskoLocalization.Text("Не удалось определить IP-адрес VPN-сервера. Проверьте интернет и DNS."),
                innerException: ex);
        }

        throw new GeksaVpnOperationException(
            "endpointResolveFailed",
            TriskoLocalization.Text("VPN-сервер не имеет доступного IPv4-адреса."));
    }

    private static GeksaVpnOperationException NoUnderlayInterface()
    {
        return new GeksaVpnOperationException(
            "networkInterfaceUnavailable",
            TriskoLocalization.Text("Не найден прямой сетевой адаптер, через который отвечает VPN-сервер. ") +
            TriskoLocalization.Text("Проверьте Ethernet, Wi-Fi или мобильную сеть либо выберите адаптер вручную в настройках."));
    }

    private static bool IsPointToPointUnderlay(NetworkInterfaceType interfaceType)
    {
        return interfaceType is NetworkInterfaceType.Ppp
            or NetworkInterfaceType.Wwanpp
            or NetworkInterfaceType.Wwanpp2;
    }

    private static int GetInterfacePriority(NetworkInterfaceType interfaceType)
    {
        return interfaceType switch
        {
            NetworkInterfaceType.Ethernet => 50,
            NetworkInterfaceType.GigabitEthernet => 50,
            NetworkInterfaceType.FastEthernetFx => 50,
            NetworkInterfaceType.FastEthernetT => 50,
            NetworkInterfaceType.Wireless80211 => 40,
            NetworkInterfaceType.Wwanpp => 30,
            NetworkInterfaceType.Wwanpp2 => 30,
            NetworkInterfaceType.Ppp => 20,
            _ => 10
        };
    }

    private static IReadOnlyList<GeksaNetworkInterfaceCandidate> GetSystemCandidates()
    {
        var candidates = new List<GeksaNetworkInterfaceCandidate>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            try
            {
                var properties = networkInterface.GetIPProperties();
                var hasDefaultGateway = properties.GatewayAddresses.Any(gateway => IsUsableGateway(gateway.Address));
                var interfaceIndex = properties.GetIPv4Properties()?.Index ?? int.MaxValue;

                foreach (var localAddress in properties.UnicastAddresses
                             .Select(unicast => unicast.Address)
                             .Where(IsUsableLocalAddress))
                {
                    candidates.Add(new GeksaNetworkInterfaceCandidate(
                        networkInterface.Name,
                        localAddress,
                        hasDefaultGateway,
                        networkInterface.NetworkInterfaceType,
                        networkInterface.Speed,
                        interfaceIndex));
                }
            }
            catch (NetworkInformationException ex)
            {
                Logging.SaveLog(nameof(GeksaNetworkBindingSelector), ex);
            }
        }

        return candidates;
    }

    private static bool IsUsableGateway(IPAddress address)
    {
        return IsUsableLocalAddress(address);
    }

    private static bool IsUsableLocalAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.None)
            || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] != 169 || bytes[1] != 254;
    }

    private static async Task<IReadOnlyList<IPAddress>> ResolveIPv4AddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsedAddress))
        {
            return [parsedAddress];
        }

        return await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);
    }

    private static async Task<GeksaNetworkProbeResult?> ProbeAsync(
        GeksaNetworkInterfaceCandidate candidate,
        IReadOnlyList<IPAddress> remoteAddresses,
        int remotePort,
        CancellationToken cancellationToken)
    {
        foreach (var remoteAddress in remoteAddresses)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            socket.Bind(new IPEndPoint(candidate.LocalAddress, 0));

            try
            {
                await socket.ConnectAsync(new IPEndPoint(remoteAddress, remotePort), cancellationToken);
                return new GeksaNetworkProbeResult(candidate, remoteAddress);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException)
            {
                // Try the next resolved address within the shared probe budget.
            }
        }

        return null;
    }
}

public sealed record GeksaNetworkInterfaceOption(
    string? InterfaceName,
    string DisplayName,
    bool IsAutomatic)
{
    public static GeksaNetworkInterfaceOption Automatic { get; } =
        new(null, TriskoLocalization.Text("Автоматически (рекомендуется)"), IsAutomatic: true);
}

public sealed record GeksaNetworkInterfaceCandidate(
    string InterfaceName,
    IPAddress LocalAddress,
    bool HasDefaultGateway,
    NetworkInterfaceType InterfaceType,
    long Speed,
    int InterfaceIndex);

public sealed record GeksaNetworkProbeResult(
    GeksaNetworkInterfaceCandidate Candidate,
    IPAddress RemoteAddress);

public sealed record GeksaNetworkBinding(
    string InterfaceName,
    IPAddress LocalAddress,
    IPAddress RemoteAddress,
    int InterfaceIndex);
