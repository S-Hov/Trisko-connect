namespace ServiceLib.Geksa;

public partial class GeksaShellViewModel
{
    private IReadOnlyList<GeksaAnyDeskConnection> _anyDeskRoster = [];
    private GeksaAnyDeskConnection? _editingAnyDesk;
    private bool _overviewRefreshing;
    private bool _searchRefreshScheduled;
    private DateTime _lastOverviewRefresh;
    public ReactiveCommand<RxVoid, RxVoid> OpenGlobalSearchCommand { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> CloseGlobalSearchCommand { get; private set; } = null!;
    public ReactiveCommand<GeksaSearchResult, RxVoid> ActivateSearchResultCommand { get; private set; } = null!;
    public ReactiveCommand<GeksaSearchResult, RxVoid> OpenSearchResultSectionCommand { get; private set; } = null!;
    public ReactiveCommand<GeksaAnyDeskConnection, RxVoid> ToggleAnyDeskFavoriteCommand { get; private set; } = null!;
    public ReactiveCommand<GeksaAnyDeskConnection, RxVoid> ToggleAnyDeskPinCommand { get; private set; } = null!;
    public ReactiveCommand<GeksaAnyDeskConnection, RxVoid> RenameAnyDeskCommand { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> SaveAnyDeskNameCommand { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> CloseAnyDeskNameCommand { get; private set; } = null!;
    public ReactiveCommand<GeksaVpnProfileOption, RxVoid> ToggleVpnFavoriteCommand { get; private set; } = null!;
    public ObservableCollection<GeksaSearchGroup> GlobalSearchGroups { get; } = [];
    [Reactive] public partial bool IsGlobalSearchOpen { get; set; }
    [Reactive] public partial string GlobalSearchQuery { get; set; } = "";
    [Reactive] public partial bool HasNoGlobalSearchResults { get; set; }
    [Reactive] public partial bool IsAnyDeskNameOpen { get; set; }
    [Reactive] public partial string AnyDeskNameDraft { get; set; } = "";
    [Reactive] public partial string AnyDeskNameAddress { get; set; } = "";
    [Reactive] public partial string UsabilityStatus { get; set; } = "";
    public string OverviewVirtualHereText => IsVirtualHereRunning ? TriskoLocalization.Text("Запущен") : TriskoLocalization.Text("Остановлен");
    public string OverviewVirtualHereColor => IsVirtualHereRunning ? "#3FD69A" : "#8FA6C6";
    public string OverviewAnyDeskText => AnyDeskNetworkStatusText.Replace("AnyDesk ", "");

    private void InitializeUsability()
    {
        OpenGlobalSearchCommand = ReactiveCommand.Create(() =>
        {
            GlobalSearchQuery = "";
            UsabilityStatus = "";
            IsGlobalSearchOpen = true;
            RefreshGlobalSearch();
        });
        CloseGlobalSearchCommand = ReactiveCommand.Create(() => { IsGlobalSearchOpen = false; });
        ActivateSearchResultCommand = ReactiveCommand.CreateFromTask<GeksaSearchResult>(result => RunSearchActionAsync(result.Activate));
        OpenSearchResultSectionCommand = ReactiveCommand.CreateFromTask<GeksaSearchResult>(result => RunSearchActionAsync(result.OpenSection));
        ToggleAnyDeskFavoriteCommand = ReactiveCommand.CreateFromTask<GeksaAnyDeskConnection>(async item =>
            await SetAnyDeskPreferenceAsync(item.Address, GetAnyDeskPreference(item.Address) with { Favorite = !item.IsFavorite }));
        ToggleAnyDeskPinCommand = ReactiveCommand.CreateFromTask<GeksaAnyDeskConnection>(async item =>
            await SetAnyDeskPreferenceAsync(item.Address, GetAnyDeskPreference(item.Address) with { Pinned = !item.IsPinned }));
        RenameAnyDeskCommand = ReactiveCommand.Create<GeksaAnyDeskConnection>(item =>
        {
            _editingAnyDesk = item;
            AnyDeskNameAddress = item.Address;
            AnyDeskNameDraft = GetAnyDeskPreference(item.Address).Name ?? "";
            UsabilityStatus = "";
            IsAnyDeskNameOpen = true;
        });
        SaveAnyDeskNameCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_editingAnyDesk is null) return;
            if (await SetAnyDeskPreferenceAsync(_editingAnyDesk.Address,
                GetAnyDeskPreference(_editingAnyDesk.Address) with { Name = AnyDeskNameDraft.Trim() }))
                IsAnyDeskNameOpen = false;
        });
        CloseAnyDeskNameCommand = ReactiveCommand.Create(() => { IsAnyDeskNameOpen = false; });
        ToggleVpnFavoriteCommand = ReactiveCommand.CreateFromTask<GeksaVpnProfileOption>(async item =>
        {
            if (_appConfig.UiItem is null) return;
            var ids = _appConfig.UiItem.GeksaFavoriteVpnIds;
            var before = ids.ToList();
            if (!ids.Remove(item.IndexId)) ids.Add(item.IndexId);
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                _appConfig.UiItem.GeksaFavoriteVpnIds = before;
                UsabilityStatus = TriskoLocalization.Text("Не удалось сохранить избранное.");
                return;
            }
            item.IsFavorite = ids.Contains(item.IndexId);
        });
        this.WhenAnyValue(x => x.GlobalSearchQuery).Subscribe(_ => RefreshGlobalSearch());
        this.WhenAnyValue(x => x.IsGlobalSearchOpen, x => x.IsAnyDeskNameOpen).Subscribe(_ =>
        {
            this.RaisePropertyChanged(nameof(IsAnyModalOpen));
            this.RaisePropertyChanged(nameof(IsEmbeddedAnyDeskActive));
        });
        this.WhenAnyValue(x => x.IsVirtualHereRunning).Subscribe(_ =>
        {
            this.RaisePropertyChanged(nameof(OverviewVirtualHereText));
            this.RaisePropertyChanged(nameof(OverviewVirtualHereColor));
        });
        this.WhenAnyValue(x => x.AnyDeskNetworkStatusText).Subscribe(_ => this.RaisePropertyChanged(nameof(OverviewAnyDeskText)));
        Resources.CollectionChanged += (_, _) => ScheduleGlobalSearchRefresh();
        VpnProfiles.CollectionChanged += (_, _) => ScheduleGlobalSearchRefresh();
        AnyDeskConnections.CollectionChanged += (_, _) => ScheduleGlobalSearchRefresh();
        this.WhenAnyValue(x => x.IsAnyDeskIntegrationEnabled).Subscribe(_ => RefreshGlobalSearch());
    }

    private async Task RunSearchActionAsync(Func<Task> action)
    {
        IsGlobalSearchOpen = false;
        try { await action(); }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RunSearchActionAsync), ex);
            FailureMessage = ex.Message;
        }
    }

    private void ScheduleGlobalSearchRefresh()
    {
        if (!IsGlobalSearchOpen || _searchRefreshScheduled) return;
        _searchRefreshScheduled = true;
        _ = RefreshGlobalSearchLaterAsync();
    }

    private async Task RefreshGlobalSearchLaterAsync()
    {
        try
        {
            await Task.Delay(75, _monitorCancellation.Token);
            RefreshGlobalSearch();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logging.SaveLog(nameof(RefreshGlobalSearchLaterAsync), ex); }
        finally { _searchRefreshScheduled = false; }
    }

    private GeksaAnyDeskPreference GetAnyDeskPreference(string address) =>
        _appConfig.UiItem?.GeksaAnyDeskPreferences.GetValueOrDefault(address) ?? new();

    private async Task<bool> SetAnyDeskPreferenceAsync(string address, GeksaAnyDeskPreference preference)
    {
        if (_appConfig.UiItem is null) return false;
        var preferences = _appConfig.UiItem.GeksaAnyDeskPreferences;
        var previous = preferences.GetValueOrDefault(address);
        preferences[address] = preference;
        if (await ConfigHandler.SaveConfig(_appConfig) != 0)
        {
            if (previous is null) preferences.Remove(address); else preferences[address] = previous;
            UsabilityStatus = TriskoLocalization.Text("Не удалось сохранить настройки подключения.");
            return false;
        }
        ApplyPersonalizedAnyDeskConnections();
        return true;
    }

    private void ApplyPersonalizedAnyDeskConnections()
    {
        var connections = GeksaAnyDeskPersonalization.Apply(_anyDeskRoster,
            _appConfig.UiItem?.GeksaAnyDeskPreferences ?? []);
        if (AnyDeskConnections.SequenceEqual(connections)) return;
        AnyDeskConnections.Clear();
        foreach (var item in connections) AnyDeskConnections.Add(item);
        HasAnyDeskConnections = AnyDeskConnections.Count > 0;
        HasNoAnyDeskConnections = !HasAnyDeskConnections;
        this.RaisePropertyChanged(nameof(AnyDeskConnectionCount));
    }

    private async Task<bool> ToggleResourceFavoriteAsync(GeksaResourceProfile resource)
    {
        if (_appConfig.UiItem is null) return false;
        var favorites = _appConfig.UiItem.GeksaFavoriteResourceIds;
        var previous = favorites.ToList();
        if (!favorites.Remove(resource.Id)) favorites.Add(resource.Id);
        if (await ConfigHandler.SaveConfig(_appConfig) == 0) return true;
        _appConfig.UiItem.GeksaFavoriteResourceIds = previous;
        UsabilityStatus = TriskoLocalization.Text("Не удалось сохранить избранное.");
        return false;
    }

    private async Task RefreshOverviewRuntimesAsync()
    {
        if (_overviewRefreshing || DateTime.UtcNow - _lastOverviewRefresh < TimeSpan.FromSeconds(10)) return;
        _overviewRefreshing = true;
        _lastOverviewRefresh = DateTime.UtcNow;
        try
        {
            IsVirtualHereRunning = await _virtualHereService.IsRunningAsync(_monitorCancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Logging.SaveLog(nameof(RefreshOverviewRuntimesAsync), ex); }
        finally { _overviewRefreshing = false; }
    }

    private void RefreshGlobalSearch()
    {
        if (!IsGlobalSearchOpen) return;
        var grouped = new Dictionary<string, List<GeksaSearchResult>>();
        void Add(string group, string title, string detail, string icon, Func<Task> action, Func<Task> section, string iconData, params string[] keywords)
        {
            if (!GeksaSearchCatalog.Matches(GlobalSearchQuery, [title, detail, group, .. keywords])) return;
            if (!grouped.TryGetValue(group, out var items)) grouped[group] = items = [];
            items.Add(new(title, detail, GeksaSearchCatalog.IconPath(icon), action, section, group.StartsWith(TriskoLocalization.Text("Подключения → ")), iconData));
        }
        foreach (var page in GeksaSearchCatalog.Pages)
        {
            if (!IsAnyDeskIntegrationEnabled && page.Target == "anydesk") continue;
            Task Navigate()
            {
                if (page.Target.StartsWith("settings:"))
                {
                    SelectPage(GeksaShellPage.Settings);
                    SelectSettingsSubPage(Enum.Parse<GeksaSettingsSubPage>(page.Target[9..]));
                }
                else if (page.Target == "vpn-servers")
                {
                    SelectPage(GeksaShellPage.Connection);
                    IsVpnServersModalOpen = true;
                }
                else if (page.Target == "anydesk")
                {
                    SetAnyDeskViewMode(GeksaAnyDeskViewMode.Integration);
                    SelectPage(GeksaShellPage.RemoteDesktops);
                }
                else SelectPage(Enum.Parse<GeksaShellPage>(page.Target));
                return Task.CompletedTask;
            }
            Add(page.Group, page.Title, TriskoLocalization.Text("Открыть страницу"), page.Icon, Navigate, Navigate, page.IconData, page.Keywords);
        }
        foreach (var item in Resources)
        {
            Task Section()
            {
                if (item.IsSsh) { SshSearchQuery = ""; SelectPage(GeksaShellPage.SshAccess); }
                else if (item.IsSmb) { FileStorageSearchQuery = ""; SelectPage(GeksaShellPage.FileStorage); }
                else { RdpSearchQuery = ""; SelectPage(GeksaShellPage.RemoteDesktops); }
                return Task.CompletedTask;
            }
            async Task Open()
            {
                await Section();
                var wasConnected = item.IsSmb && item.SmbState == GeksaSmbDriveState.Connected;
                await item.OpenCommand.Execute();
                if (item.IsSmb && !wasConnected && item.SmbState == GeksaSmbDriveState.Connected)
                    await _smbDriveService.OpenAsync(item.Resource);
            }
            Add(TriskoLocalization.Format("Подключения → {0}", item.TypeBadge), item.DisplayName,
                TriskoLocalization.Format("{0} · {1}", (item.IsSmb ? TriskoLocalization.Text("Открыть файлы") : TriskoLocalization.Text("Подключиться")), item.TargetText),
                item.IsSsh ? "ssh" : item.IsSmb ? "folder" : "rdp", Open, Section, "", item.Description);
        }
        if (IsAnyDeskIntegrationEnabled)
        foreach (var item in AnyDeskConnections)
        {
            Task Section()
            {
                SetAnyDeskViewMode(GeksaAnyDeskViewMode.Integration);
                SelectPage(GeksaShellPage.RemoteDesktops);
                return Task.CompletedTask;
            }
            async Task Connect() { await Section(); await ConnectAnyDeskAsync(item); }
            Add(TriskoLocalization.Text("Подключения → AnyDesk"), item.DisplayName, TriskoLocalization.Format("Подключиться · {0}", item.Address), "rdp", Connect, Section,
                "", TriskoLocalization.Text("анидеск"), _anyDeskRoster.FirstOrDefault(original => original.Address == item.Address)?.DisplayName ?? "");
        }
        foreach (var item in VpnProfiles.Where(item => !item.IsAutomatic))
        {
            Task Section() { SelectPage(GeksaShellPage.Connection); IsVpnServersModalOpen = true; return Task.CompletedTask; }
            async Task Connect() { SelectPage(GeksaShellPage.Connection); await ConnectVpnProfileCommand.Execute(item); }
            Add(TriskoLocalization.Text("Подключения → VPN"), item.DisplayName, TriskoLocalization.Format("Подключиться · {0}", item.EndpointText), "connection", Connect, Section, "", item.GroupName, item.ProtocolName);
        }
        GlobalSearchGroups.Clear();
        foreach (var (group, items) in grouped) GlobalSearchGroups.Add(new(group, items));
        HasNoGlobalSearchResults = GlobalSearchGroups.Count == 0;
    }
}
