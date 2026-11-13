using ServiceLib.Manager;

namespace ServiceLib.Geksa;

public sealed partial class GeksaVpnProfileOption : MyReactiveObject
{
    private static readonly Regex SubscriptionMetadataSuffix = new(
        @"\s*[|｜]\s*(?=(?:📊|⏳|⌛|♾|(?:upload|download|traffic|expire|total)\b|\d+(?:[.,]\d+)?\s*(?:[KMGT]I?B)\b)).*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public const string AutomaticIndexId = "__geksa_automatic__";
    public const string AutomaticAnyDeskIndexId = "__geksa_automatic_anydesk__";

    public GeksaVpnProfileOption(
        string indexId,
        string displayName,
        string protocolName,
        string groupName,
        string address,
        int port,
        bool isAutomatic = false,
        bool isAnyDesk = false)
    {
        IndexId = indexId;
        DisplayName = displayName;
        ProtocolName = protocolName;
        GroupName = groupName;
        Address = address;
        Port = port;
        IsAutomatic = isAutomatic;
        IsAnyDesk = false;
    }

    public string IndexId { get; }
    [Reactive] public partial bool IsFavorite { get; set; }

    public string DisplayName { get; }

    public string ProtocolName { get; }

    public string GroupName { get; }

    public string Address { get; }

    public int Port { get; }

    public bool IsAutomatic { get; }

    public bool IsAnyDesk { get; }

    public string EndpointText => IsAutomatic
        ? TriskoLocalization.Text("Самый быстрый сервер перед подключением")
        : $"{Address}:{Port}";

    [Reactive]
    public partial string DelayText { get; set; } = TriskoLocalization.Text("Не проверено");

    [Reactive]
    public partial string DelayColor { get; set; } = "#8FA6C6";

    [Reactive]
    public partial bool IsTesting { get; set; }

    public string DetailsText => GroupName.IsNotEmpty()
        ? $"{ProtocolName} · {GroupName}"
        : ProtocolName;

    public static GeksaVpnProfileOption From(ProfileItemModel profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var displayName = CleanSubscriptionDisplayName(profile.Remarks);
        if (displayName.IsNullOrEmpty())
        {
            displayName = profile.Address?.Trim();
        }

        return new GeksaVpnProfileOption(
            profile.IndexId,
            displayName.IsNullOrEmpty() ? TriskoLocalization.Text("VPN-профиль без названия") : displayName,
            GetProtocolName(profile.ConfigType),
            profile.SubRemarks?.Trim() ?? string.Empty,
            profile.Address?.Trim() ?? string.Empty,
            profile.Port,
            isAnyDesk: false);
    }

    public static GeksaVpnProfileOption CreateAutomatic(bool isAnyDesk = false) => new(
        isAnyDesk ? AutomaticAnyDeskIndexId : AutomaticIndexId,
        TriskoLocalization.Text("Автоматически"),
        TriskoLocalization.Text("Самый быстрый сервер"),
        string.Empty,
        string.Empty,
        0,
        isAutomatic: true,
        isAnyDesk: isAnyDesk);

    public static GeksaVpnProfileOption From(RunningProfileInfo profile)
    {
        var displayName = CleanSubscriptionDisplayName(profile.Name) ?? profile.Name;
        return new(
            profile.IndexId, displayName, GetProtocolName(profile.Protocol),
            string.Empty, profile.Address, profile.Port,
            isAnyDesk: false);
    }

    public static bool IsAnyDeskProfileName(string? displayName) => false;

    public static string? CleanSubscriptionDisplayName(string? displayName)
    {
        var value = displayName?.Trim();
        if (value.IsNullOrEmpty())
        {
            return value;
        }

        var cleaned = SubscriptionMetadataSuffix.Replace(value!, string.Empty).Trim();
        return cleaned.IsNotEmpty() ? cleaned : value;
    }

    public static IReadOnlyList<GeksaVpnTestCandidate> CandidatesForAutomaticProfile(
        GeksaVpnProfileOption automatic,
        IEnumerable<GeksaVpnTestCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(automatic);
        ArgumentNullException.ThrowIfNull(candidates);
        if (!automatic.IsAutomatic)
        {
            throw new ArgumentException("The profile must be automatic.", nameof(automatic));
        }

        return candidates
            .Where(candidate => candidate.Profile.IsAnyDesk == automatic.IsAnyDesk)
            .OrderBy(candidate => candidate.DelayMilliseconds)
            .ToList();
    }

    public int? LastDelay { get; private set; }

    public void ApplyDelay(int delay)
    {
        LastDelay = delay;
        IsTesting = false;
        DelayText = delay > 0 ? TriskoLocalization.Format("{0} мс", delay) : TriskoLocalization.Text("Нет ответа");
        var isLight = AppManager.Instance.Config?.UiItem?.GeksaThemeMode == GeksaThemeMode.Light;
        var green = isLight ? "#059669" : "#3FD69A";
        var red = isLight ? "#DC2626" : "#F0717E";
        DelayColor = delay > 0 && delay < 250 ? green : red;
    }

    public void RefreshTheme(GeksaThemeMode mode)
    {
        if (IsTesting)
        {
            DelayColor = "#5B8DFF";
            return;
        }

        var isLight = mode == GeksaThemeMode.Light;
        if (LastDelay.HasValue)
        {
            var green = isLight ? "#059669" : "#3FD69A";
            var red = isLight ? "#DC2626" : "#F0717E";
            DelayColor = LastDelay.Value > 0 && LastDelay.Value < 250 ? green : red;
        }
        else
        {
            DelayColor = isLight ? "#64748B" : "#8FA6C6";
        }
    }

    public void MarkTesting()
    {
        IsTesting = true;
        DelayText = TriskoLocalization.Text("Проверка…");
        DelayColor = "#5B8DFF";
    }

    public static string GetProtocolName(EConfigType configType) => configType switch
    {
        EConfigType.VMess => "VMess",
        EConfigType.Custom => TriskoLocalization.Text("Пользовательский"),
        EConfigType.Shadowsocks => "Shadowsocks",
        EConfigType.SOCKS => "SOCKS",
        EConfigType.VLESS => "VLESS",
        EConfigType.Trojan => "Trojan",
        EConfigType.Hysteria2 => "Hysteria2",
        EConfigType.TUIC => "TUIC",
        EConfigType.WireGuard => "WireGuard",
        EConfigType.HTTP => "HTTP",
        EConfigType.Anytls => "AnyTLS",
        EConfigType.Naive => "NaiveProxy",
        EConfigType.Outbound => TriskoLocalization.Text("Исходящее подключение"),
        EConfigType.PolicyGroup => TriskoLocalization.Text("Группа серверов"),
        EConfigType.ProxyChain => TriskoLocalization.Text("Цепочка прокси"),
        _ => configType.ToString()
    };
}
