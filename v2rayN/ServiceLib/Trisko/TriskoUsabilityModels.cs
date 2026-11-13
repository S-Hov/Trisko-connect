namespace ServiceLib.Trisko;

public sealed record TriskoAnyDeskPreference(string? Name = null, bool? Favorite = null, bool Pinned = false);

public static class TriskoAnyDeskPersonalization
{
    public static IReadOnlyList<TriskoAnyDeskConnection> Apply(IEnumerable<TriskoAnyDeskConnection> roster,
        IReadOnlyDictionary<string, TriskoAnyDeskPreference> preferences)
    {
        var connections = roster.ToDictionary(item => item.Address, StringComparer.OrdinalIgnoreCase);
        foreach (var (address, preference) in preferences)
        {
            if (!TriskoAnyDeskConfigurationParser.IsSafeAddress(address)) continue;
            if (!connections.ContainsKey(address)) connections[address] = new(address, address, false);
            var item = connections[address];
            connections[address] = item with
            {
                DisplayName = string.IsNullOrWhiteSpace(preference.Name) ? item.DisplayName : preference.Name.Trim(),
                IsFavorite = preference.Favorite ?? item.IsFavorite,
                IsPinned = preference.Pinned
            };
        }
        return connections.Values.OrderByDescending(item => item.IsPinned).ThenBy(item => item.DisplayName,
            StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

public sealed record TriskoSearchPage(string Title, string Group, string Icon, string Target, string Keywords)
{
    public string IconData => Target switch
    {
        "settings:OneCSystem" => "M12 2C7.58 2 4 3.79 4 6v12c0 2.21 3.58 4 8 4s8-1.79 8-4V6c0-2.21-3.58-4-8-4m0 2c3.87 0 6 1.5 6 2s-2.13 2-6 2s-6-1.5-6-2s2.13-2 6-2M6 8.71C7.47 9.5 9.61 10 12 10s4.53-.5 6-1.29V12c0 .5-2.13 2-6 2s-6-1.5-6-2zm0 6C7.47 15.5 9.61 16 12 16s4.53-.5 6-1.29V18c0 .5-2.13 2-6 2s-6-1.5-6-2z",
        "settings:VpnRouting" => "M4 6h5v2H6v3h12V8h-3V6h5v7H4zm7-4h2v6h3l-4 4-4-4h3zm-5 13h12v5H6zm2 2v1h8v-1z",
        "settings:SmbAutomation" => "M11 21h-1l1-7H7.5c-.58 0-.57-.32-.38-.66.19-.34.05-.08.07-.12C8.48 10.94 10.42 7.54 13 3h1l-1 7h3.5c.49 0 .56.33.47.51l-.07.15C12.9 17.55 11 21 11 21z",
        "settings:NetworkInterface" => "M4 6.5v3a.5.5 0 1 1-1 0v-3a.5.5 0 1 1 1 0M6.5 6a.5.5 0 0 0-.5.5v3a.5.5 0 1 0 1 0v-3a.5.5 0 0 0-.5-.5m3 0a.5.5 0 0 0-.5.5v3a.5.5 0 1 0 1 0v-3a.5.5 0 0 0-.5-.5m3 0a.5.5 0 0 0-.5.5v3a.5.5 0 0 0 1 0v-3a.5.5 0 0 0-.5-.5m2.5-.5v5a1.5 1.5 0 0 1-1.5 1.5H13v1.5a.5.5 0 0 1-.5.5h-6a.5.5 0 0 1-.5-.5V12H2v1.5a.5.5 0 0 1-1 0V3H.5a.5.5 0 0 1 0-1H1a1 1 0 0 1 1 1v1h11.5A1.5 1.5 0 0 1 15 5.5M7 13h2v-1H7zm5-1h-2v1h2zm2-6.5a.5.5 0 0 0-.5-.5H2v6h11.5a.5.5 0 0 0 .5-.5z",
        "settings:EcpServers" => "M4 4h16v12H4zm2 2v8h12V6zm3 11h6v2h3v2H6v-2h3z",
        "settings:AnyDesk" => "M4 5h16v11H4zm2 2v7h12V7zm3 10h6v2h3v2H6v-2h3z",
        "settings:Updates" => "M12 21q-1.875 0-3.512-.712t-2.85-1.925t-1.925-2.85T3 12t.713-3.512t1.924-2.85t2.85-1.925T12 3q2.05 0 3.888.875T19 6.35V5q0-.425.288-.712T20 4t.713.288T21 5v4q0 .425-.288.713T20 10h-4q-.425 0-.712-.288T15 9t.288-.712T16 8h1.75q-1.025-1.4-2.525-2.2T12 5Q9.075 5 7.038 7.038T5 12t2.038 4.963T12 19q2.375 0 4.25-1.425t2.475-3.675q.125-.4.45-.6t.725-.15q.425.05.675.362t.15.688q-.725 2.975-3.15 4.888T12 21m1-9.4l2.5 2.5q.275.275.275.7t-.275.7t-.7.275t-.7-.275l-2.8-2.8q-.15-.15-.225-.337T11 11.975V8q0-.425.288-.712T12 7t.713.288T13 8z",
        _ => ""
    };
}

public static class TriskoSearchCatalog
{
    public static IReadOnlyList<TriskoSearchPage> Pages { get; } =
    [
        new(TriskoLocalization.Text("Подключение VPN"), TriskoLocalization.Text("Страницы"), "connection", "Connection", TriskoLocalization.Text("главная интернет сервер")),
        new(TriskoLocalization.Text("Рабочие столы RDP"), TriskoLocalization.Text("Страницы"), "rdp", "RemoteDesktops", TriskoLocalization.Text("удалённый доступ")),
        new(TriskoLocalization.Text("Файлы SMB"), TriskoLocalization.Text("Страницы"), "folder", "FileStorage", TriskoLocalization.Text("диски папки хранилище")),
        new(TriskoLocalization.Text("SSH-доступ"), TriskoLocalization.Text("Страницы"), "ssh", "SshAccess", TriskoLocalization.Text("терминал консоль сервер")),
        new(TriskoLocalization.Text("ЭЦП / VirtualHere"), TriskoLocalization.Text("Страницы"), "ecp", "Ecp", TriskoLocalization.Text("usb токен подпись")),
        new(TriskoLocalization.Text("Настройки"), TriskoLocalization.Text("Настройки"), "settings", "settings:Main", TriskoLocalization.Text("тема оформление")),
        new(TriskoLocalization.Text("Система 1С"), TriskoLocalization.Text("Настройки"), "settings", "settings:OneCSystem", "1c"),
        new(TriskoLocalization.Text("Маршрутизация VPN"), TriskoLocalization.Text("Настройки"), "connection", "settings:VpnRouting", TriskoLocalization.Text("tun режим исключения")),
        new(TriskoLocalization.Text("Автоматизация SMB"), TriskoLocalization.Text("Настройки"), "folder", "settings:SmbAutomation", TriskoLocalization.Text("сетевые диски автозапуск")),
        new(TriskoLocalization.Text("Сетевой интерфейс"), TriskoLocalization.Text("Настройки"), "connection", "settings:NetworkInterface", TriskoLocalization.Text("адаптер сеть")),
        new(TriskoLocalization.Text("Серверы VirtualHere"), TriskoLocalization.Text("Настройки"), "ecp", "settings:EcpServers", TriskoLocalization.Text("usb эцп хабы")),
        new(TriskoLocalization.Text("Обновления"), TriskoLocalization.Text("Настройки"), "settings", "settings:Updates", TriskoLocalization.Text("версия релиз")),
        new(TriskoLocalization.Text("Интеграция AnyDesk"), TriskoLocalization.Text("Настройки"), "rdp", "settings:AnyDesk", TriskoLocalization.Text("анидеск удалённый доступ")),
        new(TriskoLocalization.Text("AnyDesk — подключения"), TriskoLocalization.Text("Страницы"), "rdp", "anydesk", TriskoLocalization.Text("анидеск избранные рабочие столы")),
        new(TriskoLocalization.Text("Доступные VPN-серверы"), TriskoLocalization.Text("Страницы"), "connection", "vpn-servers", TriskoLocalization.Text("выбор сервера подписки"))
    ];

    public static bool Matches(string query, params string[] values) => query.Split(' ',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .All(word => values.Any(value => value.Contains(word, StringComparison.CurrentCultureIgnoreCase)));

    public static string IconPath(string name) => $"avares://Trisko.Connect/Assets/Trisko/Icons/{name}.png";
}

public sealed record TriskoSearchResult(string Title, string Detail, string IconPath, Func<Task> Activate, Func<Task> OpenSection, bool IsConnection, string IconData = "")
{
    public bool HasImageIcon => string.IsNullOrEmpty(IconData);
}
public sealed record TriskoSearchGroup(string Name, IReadOnlyList<TriskoSearchResult> Items);
