namespace ServiceLib.Trisko;

public enum TriskoVpnRoutingMode
{
    CorporateOnly = 0,
    GlobalExceptLocal = 1,
    Global = 2,
    RussianSites = 3
}

public sealed record TriskoVpnRoutingModeOption(
    TriskoVpnRoutingMode Mode,
    string DisplayName,
    string Description)
{
    public static IReadOnlyList<TriskoVpnRoutingModeOption> All { get; } =
    [
        new(
            TriskoVpnRoutingMode.CorporateOnly,
            TriskoLocalization.Text("Только корпоративные ресурсы"),
            TriskoLocalization.Text("Через VPN идут IP из корпоративного комплекта и добавленные вручную адреса. Остальной трафик не попадает в TUN.")),
        new(
            TriskoVpnRoutingMode.RussianSites,
            TriskoLocalization.Text("Российские сайты и корпоративные ресурсы"),
            TriskoLocalization.Text("Российские доменные зоны и корпоративные ресурсы идут через VPN. Остальной интернет проходит через прямой выход без VPN-сервера.")),
        new(
            TriskoVpnRoutingMode.GlobalExceptLocal,
            TriskoLocalization.Text("Весь интернет через VPN, локальная сеть напрямую"),
            TriskoLocalization.Text("Публичный интернет идёт через VPN. Частные и локальные сети идут напрямую, кроме точных корпоративных адресов.")),
        new(
            TriskoVpnRoutingMode.Global,
            TriskoLocalization.Text("Весь трафик через VPN"),
            TriskoLocalization.Text("Весь IPv4- и IPv6-трафик направляется в TUN и через VPN-сервер."))
    ];
}

public sealed class TriskoVpnRouteItemViewModel
{
    public TriskoVpnRouteItemViewModel(
        string route,
        Func<string, Task> removeAsync)
    {
        Route = route;
        RemoveCommand = ReactiveCommand.CreateFromTask(() => removeAsync(Route));
    }

    public string Route { get; }

    public string KindText => Route.EndsWith("/32", StringComparison.Ordinal)
        ? TriskoLocalization.Text("Один IPv4-адрес")
        : TriskoLocalization.Text("IPv4-сеть");

    public ReactiveCommand<RxVoid, RxVoid> RemoveCommand { get; }
}

internal static class TriskoVpnRouteParser
{
    public const int MaxManualRoutes = 128;

    public static bool TryNormalize(
        string? value,
        out string normalized,
        out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        var candidate = value?.Trim();
        if (candidate.IsNullOrEmpty())
        {
            error = TriskoLocalization.Text("Введите IPv4-адрес, например 192.168.3.222.");
            return false;
        }

        if (IPAddress.TryParse(candidate, out var address))
        {
            if (address.AddressFamily != AddressFamily.InterNetwork)
            {
                error = TriskoLocalization.Text("Сейчас дополнительные маршруты поддерживают только IPv4.");
                return false;
            }

            normalized = $"{address}/32";
            return true;
        }

        if (!IPNetwork2.TryParse(candidate, out var network)
            || network.AddressFamily != AddressFamily.InterNetwork)
        {
            error = TriskoLocalization.Text("Неверный адрес. Используйте IPv4 или сеть CIDR, например 192.168.3.222 или 10.20.0.0/16.");
            return false;
        }

        if (network.Cidr == 0)
        {
            error = TriskoLocalization.Text("Маршрут /0 добавлять нельзя. Для всего трафика выберите глобальный режим.");
            return false;
        }

        normalized = network.ToString();
        return true;
    }

    public static List<string> NormalizeMany(IEnumerable<string>? values)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            if (TryNormalize(value, out var normalized, out _))
            {
                result.Add(normalized);
            }
        }

        return result
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(MaxManualRoutes)
            .ToList();
    }
}
