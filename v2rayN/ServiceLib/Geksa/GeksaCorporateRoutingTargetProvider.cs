namespace ServiceLib.Geksa;

internal sealed record GeksaCorporateRoutingTargets(
    bool IsEnabled,
    GeksaVpnRoutingMode Mode,
    IReadOnlyList<string> IpCidrs,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> DomainSuffixes,
    IReadOnlyList<string> SystemRouteCidrs)
{
    public static GeksaCorporateRoutingTargets Empty { get; } = new(
        false,
        GeksaVpnRoutingMode.CorporateOnly,
        [],
        [],
        [],
        []);
}

internal static class GeksaCorporateRoutingTargetProvider
{
    internal static IReadOnlyList<string> RussianDomainSuffixes { get; } =
    [
        "ru",
        "su",
        "xn--p1ai",     // .рф
        "xn--p1acf",    // .рус
        "xn--80adxhks", // .москва
        "moscow",
        "xn--d1acj3b",  // .дети
        "tatar",
        "xn--80asehdb", // .онлайн
        "xn--80aswg"    // .сайт
    ];

    private static readonly string[] LocalNetworkCidrs =
    [
        "0.0.0.0/8",
        "10.0.0.0/8",
        "100.64.0.0/10",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "224.0.0.0/4",
        "240.0.0.0/4",
        "::1/128",
        "fc00::/7",
        "fe80::/10",
        "ff00::/8"
    ];

    private static readonly JsonSerializerOptions StoreOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static GeksaCorporateRoutingTargets Load(CoreConfigContext context)
    {
        var activeConfigurationPath = Utils.GetConfigPath(
            GeksaCorporateConfigurationService.ActiveConfigurationFileName);
        var managed = ReadResources(activeConfigurationPath);
        var user = ReadUserResources(Utils.GetConfigPath(GeksaUserResourceService.StoreFileName));
        var resources = GeksaUserResourceService.Merge(
            managed,
            user.Resources,
            user.DeletedManagedResourceIds);
        var manualRoutes = context.AppConfig.TunModeItem.GeksaManualVpnRoutes;
        var mode = context.AppConfig.TunModeItem.GeksaRoutingMode;
        var oneCServerAddress = context.AppConfig.TunModeItem.GeksaOneCEnabled
            ? context.AppConfig.TunModeItem.GeksaOneCServerAddress
            : null;
        var virtualHereHubs = context.AppConfig.UiItem.GeksaVirtualHereHubs ?? [];
        var includeVirtualHereHubs = ShouldIncludeVirtualHereHubs(
            context.AppConfig.UiItem,
            mode);
        var hasVirtualHereConfiguration =
            virtualHereHubs.Any(hub => hub?.Host.IsNotEmpty() == true);

        // Do not replace ordinary v2rayN TUN routes until at least one Trisko routing
        // source exists. Subscription profiles should use corporate routing after a
        // resource package is installed, but plain v2rayN profiles must keep their
        // original routing on a fresh installation.
        if (!File.Exists(activeConfigurationPath) &&
            resources.Count == 0 &&
            (manualRoutes?.Count ?? 0) == 0 &&
            oneCServerAddress.IsNullOrEmpty() &&
            !hasVirtualHereConfiguration &&
            mode == GeksaVpnRoutingMode.CorporateOnly)
        {
            return GeksaCorporateRoutingTargets.Empty;
        }

        return FromResources(
            resources,
            manualRoutes,
            mode,
            oneCServerAddress,
            virtualHereHubs,
            includeVirtualHereHubs);
    }

    internal static bool ShouldIncludeVirtualHereHubs(UIItem uiItem, GeksaVpnRoutingMode mode)
    {
        if (!uiItem.GeksaRouteVirtualHereThroughVpn)
        {
            return false;
        }

        return mode switch
        {
            GeksaVpnRoutingMode.CorporateOnly => uiItem.GeksaRouteVirtualHereInCorporateOnly,
            GeksaVpnRoutingMode.RussianSites => uiItem.GeksaRouteVirtualHereInRussianSites,
            GeksaVpnRoutingMode.GlobalExceptLocal => uiItem.GeksaRouteVirtualHereInGlobalExceptLocal,
            GeksaVpnRoutingMode.Global => uiItem.GeksaRouteVirtualHereInGlobal,
            _ => false
        };
    }

    internal static GeksaCorporateRoutingTargets FromResources(
        IEnumerable<GeksaResourceProfile> resources,
        IEnumerable<string>? manualRoutes = null,
        GeksaVpnRoutingMode mode = GeksaVpnRoutingMode.CorporateOnly,
        string? oneCServerAddress = null,
        IEnumerable<GeksaVirtualHereHubConfiguration>? virtualHereHubs = null,
        bool includeVirtualHereHubs = true)
    {
        var ipCidrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources.Where(resource => resource.RequiresVpn))
        {
            AddHostTarget(resource.Host, ipCidrs, domains);
        }

        if (includeVirtualHereHubs)
        {
            foreach (var hub in virtualHereHubs ?? [])
            {
                AddHostTarget(hub?.Host, ipCidrs, domains);
            }
        }

        ipCidrs.UnionWith(GeksaVpnRouteParser.NormalizeMany(manualRoutes));
        if (GeksaVpnRouteParser.TryNormalize(oneCServerAddress, out var oneCRoute, out _))
        {
            ipCidrs.Add(oneCRoute);
        }

        var orderedIpCidrs = ipCidrs
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            true,
            mode,
            orderedIpCidrs,
            domains.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            mode == GeksaVpnRoutingMode.RussianSites ? RussianDomainSuffixes : [],
            BuildSystemRouteCidrs(mode, orderedIpCidrs));
    }

    private static void AddHostTarget(
        string? value,
        ISet<string> ipCidrs,
        ISet<string> domains)
    {
        var host = value?.Trim();
        if (host.IsNullOrEmpty())
        {
            return;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            var prefixLength = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            ipCidrs.Add($"{address}/{prefixLength}");
        }
        else if (Uri.CheckHostName(host) == UriHostNameType.Dns)
        {
            domains.Add(host.ToLowerInvariant());
        }
    }

    internal static IReadOnlyList<string> ApplyExclusions(
        IEnumerable<string> routes,
        IEnumerable<string>? exclusions)
    {
        var networks = routes
            .Select(IPNetwork2.Parse)
            .Where(network => network is not null)
            .ToList();

        foreach (var exclusion in (exclusions ?? [])
                     .Select(IPNetwork2.Parse)
                     .Where(network => network is not null))
        {
            var next = new List<IPNetwork2>();
            foreach (var network in networks)
            {
                next.AddRange(network.AddressFamily == exclusion.AddressFamily
                    ? network.Subtract(exclusion)
                    : [network]);
            }
            networks = next;
        }

        if (networks.Count == 0)
        {
            return [];
        }

        return networks
            .GroupBy(network => network.AddressFamily)
            .SelectMany(group => IPNetwork2.Supernet(group.ToArray()))
            .Select(network => network.ToString())
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildSystemRouteCidrs(
        GeksaVpnRoutingMode mode,
        IReadOnlyList<string> corporateIpCidrs)
    {
        return mode switch
        {
            GeksaVpnRoutingMode.CorporateOnly => corporateIpCidrs,
            GeksaVpnRoutingMode.GlobalExceptLocal => ApplyExclusions(
                    ["0.0.0.0/0", "::/0"],
                    LocalNetworkCidrs)
                .Concat(corporateIpCidrs)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            GeksaVpnRoutingMode.RussianSites => ["0.0.0.0/0", "::/0"],
            GeksaVpnRoutingMode.Global => ["0.0.0.0/0", "::/0"],
            _ => corporateIpCidrs
        };
    }

    private static IReadOnlyList<GeksaResourceProfile> ReadResources(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > GeksaCorporateConfigurationCodec.MaxConfigurationBytes)
            {
                throw new InvalidDataException("Invalid corporate resource store size.");
            }

            var document = JsonSerializer.Deserialize<ResourceDocument>(
                File.ReadAllBytes(path),
                StoreOptions);
            return document?.Resources?.ToArray() ?? [];
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaCorporateRoutingTargetProvider), ex);
            return [];
        }
    }

    private static UserResourceDocument ReadUserResources(string path)
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            var file = new FileInfo(path);
            if (file.Length is <= 0 or > GeksaCorporateConfigurationCodec.MaxConfigurationBytes)
            {
                throw new InvalidDataException("Invalid user resource store size.");
            }

            var document = JsonSerializer.Deserialize<UserResourceDocument>(
                File.ReadAllBytes(path),
                StoreOptions);
            return document ?? new();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaCorporateRoutingTargetProvider), ex);
            return new();
        }
    }

    private sealed record ResourceDocument(List<GeksaResourceProfile>? Resources);

    private sealed class UserResourceDocument
    {
        public List<GeksaResourceProfile> Resources { get; init; } = [];

        public List<string> DeletedManagedResourceIds { get; init; } = [];
    }
}
