namespace ServiceLib.Geksa;

public enum GeksaThemeMode
{
    Brand,
    Dark,
    Light
}

public partial class GeksaShellViewModel : MyReactiveObject, IDisposable
{
    private readonly GeksaVpnController _controller;
    private readonly GeksaCorporateConfigurationService _configurationService;
    private readonly GeksaUserResourceService _userResourceService;
    private readonly IGeksaResourceLauncher _resourceLauncher;
    private readonly IGeksaSmbDriveService _smbDriveService;
    private readonly GeksaNetworkBindingSelector _networkBindingSelector;
    private readonly IGeksaOneCSystemService _oneCSystemService;
    private readonly IGeksaVirtualHereService _virtualHereService;
    private readonly IGeksaAnyDeskService _anyDeskService = new WindowsGeksaAnyDeskService();
    private bool _anyDeskEmbeddedLaunchRequested;
    private bool _isAnyDeskEmbeddedWindowAttached;
    private readonly Config _appConfig;
    private readonly IDisposable _snapshotSubscription;
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly SemaphoreSlim _ecpRefreshLock = new(1, 1);
    private readonly SemaphoreSlim _vpnProfileTestLock = new(1, 1);
    private readonly SemaphoreSlim _vpnProfileSelectionLock = new(1, 1);
    private readonly TaskCompletionSource<bool> _initializationCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _previousVpnConnected;
    private bool _scanEcpWhenVpnStops;
    private Task? _monitorTask;
    private Task? _ecpAutoScanTask;
    private Task? _connectionDurationTask;
    private Task? _anyDeskMonitorTask;
    private DateTimeOffset? _connectionStartedAtUtc;
    private string? _localFailureMessage;
    private bool _desiredConnected;
    private int _reconnectAttempt;
    private readonly List<GeksaResourceItemViewModel> _allRdpResources = [];
    private readonly List<GeksaResourceItemViewModel> _allFileStorageResources = [];
    private readonly List<GeksaResourceItemViewModel> _allSshResources = [];
    private readonly List<GeksaVpnTestCandidate> _lastAutoConnectCandidates = [];
    private bool _isSynchronizingVpnProfileSelection;
    private bool _subscriptionPromptShown;
    private bool _isVpnConnectionBusy;
    private bool _isVpnSelectionBusy;
    private string? _automaticProfileId;
    private RunningProfileInfo? _displayedRunningProfile;
    private GeksaVpnProfileOption? _runningVpnProfileOption;
    private int _vpnProfilesRefreshVersion;
    private SpeedtestService? _vpnSpeedtestService;
    private GeksaVpnState? _lastLoggedVpnState;
    private DateTimeOffset _nextReconnectAtUtc;
    private string? _lastAnyDeskEnvironmentWarning;

    private static readonly TimeSpan[] ReconnectBackoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(60)
    ];

    public GeksaShellViewModel(
        GeksaVpnController controller,
        GeksaCorporateConfigurationService configurationService,
        GeksaUserResourceService userResourceService,
        IGeksaResourceLauncher resourceLauncher,
        IGeksaSmbDriveService smbDriveService,
        GeksaNetworkBindingSelector networkBindingSelector,
        IGeksaOneCSystemService oneCSystemService,
        IGeksaVirtualHereService virtualHereService)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _configurationService = configurationService
            ?? throw new ArgumentNullException(nameof(configurationService));
        _userResourceService = userResourceService
            ?? throw new ArgumentNullException(nameof(userResourceService));
        _resourceLauncher = resourceLauncher
            ?? throw new ArgumentNullException(nameof(resourceLauncher));
        _smbDriveService = smbDriveService
            ?? throw new ArgumentNullException(nameof(smbDriveService));
        _networkBindingSelector = networkBindingSelector
            ?? throw new ArgumentNullException(nameof(networkBindingSelector));
        _oneCSystemService = oneCSystemService
            ?? throw new ArgumentNullException(nameof(oneCSystemService));
        _virtualHereService = virtualHereService
            ?? throw new ArgumentNullException(nameof(virtualHereService));
        _appConfig = AppManager.Instance.Config;
        AnyDeskStrategyOptions.Add(new(GeksaAnyDeskStrategy.Direct, TriskoLocalization.Text("Напрямую"),
            TriskoLocalization.Text("Прямое подключение через AnyDesk")));
        RefreshVpnRoutingSettings();
        OneCServerName = _appConfig.TunModeItem.GeksaOneCServerName;
        OneCServerAddress = _appConfig.TunModeItem.GeksaOneCServerAddress;
        IsOneCConfigured = _appConfig.TunModeItem.GeksaOneCEnabled;
        OneCHostsFilePath = _oneCSystemService.HostsFilePath;
        UpdateOneCRouteText();
        if (_appConfig.UiItem != null)
        {
            IsSidebarCollapsed = _appConfig.UiItem.GeksaIsSidebarCollapsed;
            ShowConnectionNav = _appConfig.UiItem.GeksaShowConnectionNav;
            ShowRdpNav = _appConfig.UiItem.GeksaShowRdpNav;
            ShowSmbNav = _appConfig.UiItem.GeksaShowSmbNav;
            ShowSshNav = _appConfig.UiItem.GeksaShowSshNav;
            ShowEcpNav = _appConfig.UiItem.GeksaShowEcpNav;
            ShowSettingsNav = _appConfig.UiItem.GeksaShowSettingsNav;
            InitializeConnectionLayout();
            EnsureCurrentPageIsVisible();
            ThemeMode = _appConfig.UiItem.GeksaThemeMode;
            AutoConnectSmbOnVpn = _appConfig.UiItem.GeksaAutoConnectSmbOnVpn;
            AutoDisconnectSmbOnVpn = _appConfig.UiItem.GeksaAutoDisconnectSmbOnVpn;
            AutoLaunchResourcesOnVpn = _appConfig.UiItem.GeksaAutoLaunchResourcesOnVpn;
            IsAnyDeskIntegrationEnabled = _appConfig.UiItem.GeksaAnyDeskIntegrationEnabled;
            AutoStartAnyDeskOnLaunch = _appConfig.UiItem.GeksaAutoStartAnyDeskOnLaunch;
            SetAnyDeskViewMode(_appConfig.UiItem.GeksaAnyDeskViewMode);
            AnyDeskExecutablePath = _appConfig.UiItem.GeksaAnyDeskExecutablePath.IsNullOrEmpty()
                ? GeksaAnyDeskClientLocator.BundledExecutablePath
                : _appConfig.UiItem.GeksaAnyDeskExecutablePath!;
            SelectedAnyDeskStrategy = AnyDeskStrategyOptions.FirstOrDefault(option =>
                option.Value == _appConfig.UiItem.GeksaAnyDeskStrategy)
                ?? AnyDeskStrategyOptions[0];
            if (IsAnyDeskIntegrationEnabled)
            {
                RefreshAnyDeskClientStatus();
            }
            else
            {
                ClearAnyDeskEnvironment();
            }
            RouteEcpServersThroughVpn = _appConfig.UiItem.GeksaRouteVirtualHereThroughVpn;
            RouteEcpInCorporateOnly = _appConfig.UiItem.GeksaRouteVirtualHereInCorporateOnly;
            RouteEcpInRussianSites = _appConfig.UiItem.GeksaRouteVirtualHereInRussianSites;
            RouteEcpInGlobalExceptLocal = _appConfig.UiItem.GeksaRouteVirtualHereInGlobalExceptLocal;
            RouteEcpInGlobal = _appConfig.UiItem.GeksaRouteVirtualHereInGlobal;
            UpdateEcpRoutingSettingStatus(reconnectRequired: false);
            AutoStartVirtualHereOnLaunch = _appConfig.UiItem.GeksaAutoStartVirtualHereOnLaunch;
            AutoStartVirtualHereSettingStatus = AutoStartVirtualHereOnLaunch
                ? TriskoLocalization.Text("VirtualHere автоматически запускается при старте приложения.")
                : TriskoLocalization.Text("VirtualHere запускается вручную кнопкой в разделе «ЭЦП».");
            AutoCheckUpdatesEnabled = _appConfig.UiItem.GeksaAutoCheckUpdatesEnabled;
            CheckUpdateIntervalHours = Math.Clamp(_appConfig.UiItem.GeksaCheckUpdateIntervalHours, 1, 720);
            CheckUpdateIntervalHoursText = CheckUpdateIntervalHours.ToString();
            AutoCheckUpdatesStatus = AutoCheckUpdatesEnabled
                ? TriskoLocalization.Format("Автоматическая проверка обновлений включена: каждые {0} ч.", CheckUpdateIntervalHours)
                : TriskoLocalization.Text("Автоматическая проверка обновлений выключена.");
            IsEcpAutoScanEnabled = _appConfig.UiItem.GeksaEcpAutoScanEnabled;
            EcpAutoScanIntervalSeconds = Math.Clamp(
                _appConfig.UiItem.GeksaEcpAutoScanIntervalSeconds,
                1,
                3600);
            EcpAutoScanIntervalText = EcpAutoScanIntervalSeconds.ToString();
            _appConfig.UiItem.GeksaVirtualHereHubs ??= [];
            foreach (var hub in _appConfig.UiItem.GeksaVirtualHereHubs
                         .Where(hub => hub.Host.IsNotEmpty() && hub.Port is > 0 and <= 65535)
                         .GroupBy(hub => hub.Endpoint, StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
            {
                EcpServers.Add(new GeksaEcpServerItemViewModel(hub, RemoveEcpServerAsync));
            }
            HasEcpServers = EcpServers.Count > 0;
            HasNoEcpServers = !HasEcpServers;
        }


        this.WhenAnyValue(x => x.AutoConnectSmbOnVpn)
            .Skip(1)
            .Subscribe(val =>
            {
                if (_appConfig.UiItem != null)
                {
                    _appConfig.UiItem.GeksaAutoConnectSmbOnVpn = val;
                    _ = ConfigHandler.SaveConfig(_appConfig);
                }
            });

        this.WhenAnyValue(x => x.AutoDisconnectSmbOnVpn)
            .Skip(1)
            .Subscribe(val =>
            {
                if (_appConfig.UiItem != null)
                {
                    _appConfig.UiItem.GeksaAutoDisconnectSmbOnVpn = val;
                    _ = ConfigHandler.SaveConfig(_appConfig);
                }
            });

        this.WhenAnyValue(x => x.AutoLaunchResourcesOnVpn)
            .Skip(1)
            .Subscribe(val =>
            {
                if (_appConfig.UiItem != null)
                {
                    _appConfig.UiItem.GeksaAutoLaunchResourcesOnVpn = val;
                    _ = ConfigHandler.SaveConfig(_appConfig);
                }
            });

        this.WhenAnyValue(x => x.RouteEcpServersThroughVpn)
            .Skip(1)
            .Subscribe(val =>
            {
                if (_appConfig.UiItem is null)
                {
                    return;
                }

                _appConfig.UiItem.GeksaRouteVirtualHereThroughVpn = val;
                _ = ConfigHandler.SaveConfig(_appConfig);
                UpdateEcpRoutingSettingStatus(reconnectRequired: true);
                this.RaisePropertyChanged(nameof(EcpServerSettingsSummaryText));
            });

        this.WhenAnyValue(x => x.RouteEcpInCorporateOnly)
            .Skip(1)
            .Subscribe(val => SaveEcpRoutingModeSetting(
                () => _appConfig.UiItem.GeksaRouteVirtualHereInCorporateOnly = val));

        this.WhenAnyValue(x => x.RouteEcpInRussianSites)
            .Skip(1)
            .Subscribe(val => SaveEcpRoutingModeSetting(
                () => _appConfig.UiItem.GeksaRouteVirtualHereInRussianSites = val));

        this.WhenAnyValue(x => x.RouteEcpInGlobalExceptLocal)
            .Skip(1)
            .Subscribe(val => SaveEcpRoutingModeSetting(
                () => _appConfig.UiItem.GeksaRouteVirtualHereInGlobalExceptLocal = val));

        this.WhenAnyValue(x => x.RouteEcpInGlobal)
            .Skip(1)
            .Subscribe(val => SaveEcpRoutingModeSetting(
                () => _appConfig.UiItem.GeksaRouteVirtualHereInGlobal = val));

        this.WhenAnyValue(x => x.AutoStartVirtualHereOnLaunch)
            .Skip(1)
            .Subscribe(val =>
            {
                if (_appConfig.UiItem is null)
                {
                    return;
                }

                _appConfig.UiItem.GeksaAutoStartVirtualHereOnLaunch = val;
                _ = ConfigHandler.SaveConfig(_appConfig);
                AutoStartVirtualHereSettingStatus = val
                    ? TriskoLocalization.Text("VirtualHere автоматически запускается при старте приложения.")
                    : TriskoLocalization.Text("VirtualHere запускается вручную кнопкой в разделе «ЭЦП».");
                this.RaisePropertyChanged(nameof(EcpServerSettingsSummaryText));
            });

        this.WhenAnyValue(x => x.AutoCheckUpdatesEnabled)
            .Skip(1)
            .Subscribe(val =>
            {
                if (_appConfig.UiItem is null)
                {
                    return;
                }

                _appConfig.UiItem.GeksaAutoCheckUpdatesEnabled = val;
                _ = ConfigHandler.SaveConfig(_appConfig);
                AutoCheckUpdatesStatus = val
                    ? TriskoLocalization.Format("Автоматическая проверка обновлений включена: каждые {0} ч.", CheckUpdateIntervalHours)
                    : TriskoLocalization.Text("Автоматическая проверка обновлений выключена.");
                this.RaisePropertyChanged(nameof(UpdatesSummaryText));
            });

        this.WhenAnyValue(x => x.IsSidebarCollapsed)
            .Skip(1)
            .Subscribe(val =>
            {
                if (_appConfig.UiItem != null)
                {
                    _appConfig.UiItem.GeksaIsSidebarCollapsed = val;
                    _ = ConfigHandler.SaveConfig(_appConfig);
                }
            });

        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync);
        DisconnectCommand = ReactiveCommand.CreateFromTask(DisconnectAsync);
        RunPrimaryActionCommand = ReactiveCommand.CreateFromTask(RunPrimaryActionAsync);
        InitializeFeedback();
        InitializeStartupSettings();
        ImportCorporateConfigurationCommand = ReactiveCommand.CreateFromTask(ImportConfigurationAsync);
        ExportCorporateConfigurationCommand = ReactiveCommand.CreateFromTask(ExportConfigurationAsync);
        ShowFormatHelpCommand = ReactiveCommand.CreateFromTask(ShowFormatHelpAsync);
        ShowAdvancedVpnSettingsCommand = ReactiveCommand.CreateFromTask(ShowAdvancedVpnSettingsAsync);
        ShowUpdateManagerCommand = ReactiveCommand.CreateFromTask(ShowUpdateManagerAsync);
        SaveAutoCheckUpdatesSettingsCommand = ReactiveCommand.CreateFromTask(SaveAutoCheckUpdatesSettingsAsync);
        SaveNetworkInterfaceCommand = ReactiveCommand.CreateFromTask(SaveNetworkInterfaceAsync);
        RefreshNetworkInterfacesCommand = ReactiveCommand.Create(RefreshNetworkInterfaceOptions);
        SaveVpnRoutingModeCommand = ReactiveCommand.CreateFromTask(SaveVpnRoutingModeAsync);
        AddManualVpnRouteCommand = ReactiveCommand.CreateFromTask(AddManualVpnRouteAsync);
        ConfigureOneCSystemCommand = ReactiveCommand.CreateFromTask(ConfigureOneCSystemAsync);
        RefreshOneCSystemCommand = ReactiveCommand.CreateFromTask(RefreshOneCSystemStatusAsync);
        RemoveOneCSystemCommand = ReactiveCommand.CreateFromTask(RemoveOneCSystemAsync);
        TestLatencyCommand = ReactiveCommand.CreateFromTask(TestLatencyAsync);
        AddRdpResourceCommand = ReactiveCommand.CreateFromTask(
            () => AddResourceAsync(GeksaResourceType.Rdp));
        AddSshResourceCommand = ReactiveCommand.CreateFromTask(
            () => AddResourceAsync(GeksaResourceType.Ssh));
        AddSmbResourceCommand = ReactiveCommand.CreateFromTask(
            () => AddResourceAsync(GeksaResourceType.Smb));
        AddManualVpnCommand = ReactiveCommand.CreateFromTask(AddManualVpnAsync);
        ConfigureVpnSubscriptionCommand = ReactiveCommand.CreateFromTask(
            () => ConfigureVpnSubscriptionAsync(onboarding: false));
        UpdateVpnSubscriptionCommand = ReactiveCommand.CreateFromTask(UpdateVpnSubscriptionsAsync);
        TestAllVpnProfilesCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                if (!IsBusy)
                {
                    _localFailureMessage = null;
                    await TestAllVpnProfilesAsync();
                }
            });
        SelectVpnProfileCommand = ReactiveCommand.CreateFromTask<GeksaVpnProfileOption>(
            SelectVpnProfileFromMenuAsync);
        ConnectVpnProfileCommand = ReactiveCommand.CreateFromTask<GeksaVpnProfileOption>(
            ConnectVpnProfileAsync);
        TestVpnProfileCommand = ReactiveCommand.CreateFromTask<GeksaVpnProfileOption>(
            TestVpnProfileLatencyAsync);
        OpenVpnServersModalCommand = ReactiveCommand.Create(() =>
        {
            IsRegularVpnServerSection = true;
            IsVpnServersModalOpen = true;
        });
        CloseVpnServersModalCommand = ReactiveCommand.Create(() => { IsVpnServersModalOpen = false; });
        ShowRegularVpnServerSectionCommand = ReactiveCommand.Create(() => SelectVpnServerSection(false));
        ToggleRegularVpnServersExpandedCommand = ReactiveCommand.Create(() =>
        {
            IsRegularVpnServersExpanded = !IsRegularVpnServersExpanded;
        });
        ToggleAnyDeskVpnServersExpandedCommand = ReactiveCommand.Create(() =>
        {
            IsAnyDeskVpnServersExpanded = !IsAnyDeskVpnServersExpanded;
        });
        ShowConnectionCommand = ReactiveCommand.Create(() => SelectPage(GeksaShellPage.Connection));
        ShowRemoteDesktopsCommand = ReactiveCommand.Create(() => SelectPage(GeksaShellPage.RemoteDesktops));
        ShowFileStorageCommand = ReactiveCommand.Create(() => SelectPage(GeksaShellPage.FileStorage));
        ShowSshAccessCommand = ReactiveCommand.Create(() => SelectPage(GeksaShellPage.SshAccess));
        ShowEcpCommand = ReactiveCommand.Create(() => SelectPage(GeksaShellPage.Ecp));
        ShowSettingsCommand = ReactiveCommand.Create(() => SelectPage(GeksaShellPage.Settings));
        StartVirtualHereCommand = ReactiveCommand.CreateFromTask(StartVirtualHereAsync);
        RefreshEcpDevicesCommand = ReactiveCommand.CreateFromTask(RefreshEcpEnvironmentAsync);
        ScanEcpDevicesCommand = ReactiveCommand.CreateFromTask(RefreshEcpDevicesAsync);
        OpenVirtualHereCommand = ReactiveCommand.Create(OpenVirtualHere);
        AddEcpServerCommand = ReactiveCommand.CreateFromTask(AddEcpServerAsync);
        SaveEcpAutoScanSettingsCommand = ReactiveCommand.CreateFromTask(SaveEcpAutoScanSettingsAsync);
        BrowseAnyDeskExecutableCommand = ReactiveCommand.CreateFromTask(BrowseAnyDeskExecutableAsync);
        UseBundledAnyDeskExecutableCommand = ReactiveCommand.Create(UseBundledAnyDeskExecutable);
        SaveAnyDeskSettingsCommand = ReactiveCommand.CreateFromTask(SaveAnyDeskSettingsAsync);
        RefreshAnyDeskCommand = ReactiveCommand.CreateFromTask(RefreshAnyDeskEnvironmentAsync);
        ShowAnyDeskIntegrationViewCommand = ReactiveCommand.CreateFromTask(ShowAnyDeskIntegrationViewAsync);
        ShowEmbeddedAnyDeskViewCommand = ReactiveCommand.CreateFromTask(ShowEmbeddedAnyDeskViewAsync);
        ConnectAnyDeskCommand = ReactiveCommand.CreateFromTask<GeksaAnyDeskConnection>(ConnectAnyDeskAsync);
        ConnectManualAnyDeskCommand = ReactiveCommand.CreateFromTask(ConnectManualAnyDeskAsync);
        NavigateToSettingsMainCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.Main));
        NavigateToOneCSettingsCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.OneCSystem));
        NavigateToVpnRoutingSettingsCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.VpnRouting));
        NavigateToSmbAutomationSettingsCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.SmbAutomation));
        NavigateToNetworkInterfaceSettingsCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.NetworkInterface));
        NavigateToEcpServerSettingsCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.EcpServers));
        NavigateToUpdatesSettingsCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.Updates));
        NavigateToAnyDeskSettingsCommand = ReactiveCommand.Create(() => SelectSettingsSubPage(GeksaSettingsSubPage.AnyDesk));
        ToggleSidebarCommand = ReactiveCommand.Create(ToggleSidebar);
        ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
        SelectBrandThemeCommand = ReactiveCommand.Create(() => SetTheme(GeksaThemeMode.Brand));
        SelectDarkThemeCommand = ReactiveCommand.Create(() => SetTheme(GeksaThemeMode.Dark));
        SelectLightThemeCommand = ReactiveCommand.Create(() => SetTheme(GeksaThemeMode.Light));
        ClearRdpSearchCommand = ReactiveCommand.Create(() => { RdpSearchQuery = string.Empty; });
        ClearFileStorageSearchCommand = ReactiveCommand.Create(() => { FileStorageSearchQuery = string.Empty; });
        ClearSshSearchCommand = ReactiveCommand.Create(() => { SshSearchQuery = string.Empty; });
        ToggleShowConnectionNavCommand = ReactiveCommand.Create(ToggleShowConnectionNav);
        ToggleShowRdpNavCommand = ReactiveCommand.Create(ToggleShowRdpNav);
        ToggleShowSmbNavCommand = ReactiveCommand.Create(ToggleShowSmbNav);
        ToggleShowSshNavCommand = ReactiveCommand.Create(ToggleShowSshNav);
        ToggleShowEcpNavCommand = ReactiveCommand.Create(ToggleShowEcpNav);
        ToggleShowSettingsNavCommand = ReactiveCommand.Create(ToggleShowSettingsNav);
        ShowAllNavSectionsCommand = ReactiveCommand.Create(ShowAllNavSections);

        this.WhenAnyValue(x => x.RdpSearchQuery)
            .Subscribe(_ => FilterRdpResources());
        this.WhenAnyValue(x => x.FileStorageSearchQuery)
            .Subscribe(_ => FilterFileStorageResources());
        this.WhenAnyValue(x => x.SshSearchQuery)
            .Subscribe(_ => FilterSshResources());
        this.WhenAnyValue(x => x.OneCServerName, x => x.OneCServerAddress)
            .Skip(1)
            .Subscribe(_ => MarkOneCSettingsChanged());
        this.WhenAnyValue(x => x.IsOneCConfigured, x => x.OneCServerName, x => x.OneCServerAddress)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(OneCSettingsSummaryText)));
        this.WhenAnyValue(x => x.SelectedVpnRoutingModeOption)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(VpnRoutingSummaryText)));
        this.WhenAnyValue(x => x.AutoLaunchResourcesOnVpn, x => x.AutoConnectSmbOnVpn, x => x.AutoDisconnectSmbOnVpn)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SmbAutomationSummaryText)));
        this.WhenAnyValue(x => x.SelectedNetworkInterfaceOption)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(NetworkInterfaceSummaryText)));
        this.WhenAnyValue(
                x => x.IsVpnServersModalOpen,
                x => x.IsGlobalSearchOpen)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(IsAnyModalOpen));
                this.RaisePropertyChanged(nameof(IsEmbeddedAnyDeskActive));
            });
        this.WhenAnyValue(x => x.AnyDeskExecutablePath)
            .Skip(1)
            .Subscribe(_ => RefreshAnyDeskClientStatus());
        this.WhenAnyValue(x => x.IsAnyDeskIntegrationEnabled)
            .Skip(1)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(AnyDeskIntegrationSummaryText));
                this.RaisePropertyChanged(nameof(AnyDeskIntegrationStateText));
            });
        this.WhenAnyValue(x => x.AutoStartAnyDeskOnLaunch)
            .Skip(1)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(AnyDeskIntegrationSummaryText)));
        this.WhenAnyValue(x => x.ThemeMode)
            .Subscribe(mode =>
            {
                this.RaisePropertyChanged(nameof(IsBrandTheme));
                this.RaisePropertyChanged(nameof(IsDarkTheme));
                this.RaisePropertyChanged(nameof(IsLightTheme));
                this.RaisePropertyChanged(nameof(ThemeTooltipText));
                this.RaisePropertyChanged(nameof(ThemeButtonText));
                this.RaisePropertyChanged(nameof(ThemeSummaryText));
                foreach (var profile in VpnProfiles)
                {
                    profile.RefreshTheme(mode);
                }
                ApplySnapshot(_controller.Snapshot);
            });

        _snapshotSubscription = _controller.SnapshotChanged.AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(ApplySnapshot);

        InitializeUsability();
        ApplySnapshot(_controller.Snapshot);
    }

    public Interaction<RxVoid, RxVoid> ShowAdvancedVpnSettingsInteraction { get; } = new();

    public Interaction<RxVoid, string?> BrowseCorporateConfigurationInteraction { get; } = new();

    public Interaction<string, string?> SaveCorporateConfigurationInteraction { get; } = new();

    public Interaction<GeksaConfigurationImportPreview, GeksaConfigurationImportDecision>
        ConfirmCorporateConfigurationImportInteraction { get; } = new();

    public Interaction<RxVoid, RxVoid> ShowFormatHelpInteraction { get; } = new();

    public Interaction<RxVoid, RxVoid> ShowUpdateManagerInteraction { get; } = new();

    public Interaction<GeksaResourceEditRequest, GeksaResourceEditResult?> EditResourceInteraction { get; } = new();

    public Interaction<RxVoid, GeksaManualVpnProfile?> AddManualVpnInteraction { get; } = new();

    public Interaction<GeksaVpnSubscriptionEditContext?, GeksaVpnSubscriptionRequest?> ConfigureVpnSubscriptionInteraction { get; } = new();

    public Interaction<string, bool> ConfirmOneCSystemChangeInteraction { get; } = new();

    public Interaction<RxVoid, string?> BrowseAnyDeskExecutableInteraction { get; } = new();

    public Interaction<string, RxVoid> ShowAnyDeskWarningInteraction { get; } = new();

    public Interaction<string, RxVoid> ShowVpnProfileSwitchBlockedInteraction { get; } = new();

    public ReactiveCommand<RxVoid, RxVoid> ConnectCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> DisconnectCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RunPrimaryActionCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ImportCorporateConfigurationCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ExportCorporateConfigurationCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowFormatHelpCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowAdvancedVpnSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowUpdateManagerCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SaveAutoCheckUpdatesSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SaveNetworkInterfaceCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RefreshNetworkInterfacesCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SaveVpnRoutingModeCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddManualVpnRouteCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ConfigureOneCSystemCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RefreshOneCSystemCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveOneCSystemCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> TestLatencyCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddRdpResourceCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddSshResourceCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddSmbResourceCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddManualVpnCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ConfigureVpnSubscriptionCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> UpdateVpnSubscriptionCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> TestAllVpnProfilesCommand { get; }

    public ReactiveCommand<GeksaVpnProfileOption, RxVoid> SelectVpnProfileCommand { get; }

    public ReactiveCommand<GeksaVpnProfileOption, RxVoid> ConnectVpnProfileCommand { get; }

    public ReactiveCommand<GeksaVpnProfileOption, RxVoid> TestVpnProfileCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> OpenVpnServersModalCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> CloseVpnServersModalCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowRegularVpnServerSectionCommand { get; }



    public ReactiveCommand<RxVoid, RxVoid> ToggleRegularVpnServersExpandedCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ToggleAnyDeskVpnServersExpandedCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowConnectionCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowRemoteDesktopsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowFileStorageCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowSshAccessCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowEcpCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> StartVirtualHereCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RefreshEcpDevicesCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ScanEcpDevicesCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> OpenVirtualHereCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> AddEcpServerCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SaveEcpAutoScanSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> BrowseAnyDeskExecutableCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> UseBundledAnyDeskExecutableCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SaveAnyDeskSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RefreshAnyDeskCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowAnyDeskIntegrationViewCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ShowEmbeddedAnyDeskViewCommand { get; }

    public ReactiveCommand<GeksaAnyDeskConnection, RxVoid> ConnectAnyDeskCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ConnectManualAnyDeskCommand { get; }





    public ReactiveCommand<RxVoid, RxVoid> NavigateToSettingsMainCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NavigateToOneCSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NavigateToVpnRoutingSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NavigateToSmbAutomationSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NavigateToNetworkInterfaceSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NavigateToEcpServerSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NavigateToUpdatesSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> NavigateToAnyDeskSettingsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ToggleSidebarCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ToggleThemeCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SelectBrandThemeCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SelectDarkThemeCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> SelectLightThemeCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ClearRdpSearchCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ClearFileStorageSearchCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> ClearSshSearchCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleShowConnectionNavCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleShowRdpNavCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleShowSmbNavCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleShowSshNavCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleShowEcpNavCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleShowSettingsNavCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ShowAllNavSectionsCommand { get; }

    [Reactive]
    public partial bool ShowConnectionNav { get; set; } = true;

    [Reactive]
    public partial bool ShowRdpNav { get; set; } = true;

    [Reactive]
    public partial bool ShowSmbNav { get; set; } = true;

    [Reactive]
    public partial bool ShowSshNav { get; set; } = false;

    [Reactive]
    public partial bool ShowEcpNav { get; set; } = false;

    [Reactive]
    public partial bool ShowSettingsNav { get; set; } = true;

    [Reactive]
    public partial string RdpSearchQuery { get; set; } = string.Empty;

    [Reactive]
    public partial string FileStorageSearchQuery { get; set; } = string.Empty;

    [Reactive]
    public partial string SshSearchQuery { get; set; } = string.Empty;

    [Reactive]
    public partial string RdpEmptyTitle { get; set; } = TriskoLocalization.Text("RDP-подключения не загружены");

    [Reactive]
    public partial string RdpEmptySubtitle { get; set; } = TriskoLocalization.Text("Нажмите «Добавить RDP» или импортируйте корпоративный комплект .gac3.");

    [Reactive]
    public partial string FileStorageEmptyTitle { get; set; } = TriskoLocalization.Text("Файловые ресурсы не загружены");

    [Reactive]
    public partial string FileStorageEmptySubtitle { get; set; } = TriskoLocalization.Text("Нажмите «Добавить SMB» или импортируйте корпоративный комплект .gac3.");

    [Reactive]
    public partial string SshEmptyTitle { get; set; } = TriskoLocalization.Text("SSH-подключения не загружены");

    [Reactive]
    public partial string SshEmptySubtitle { get; set; } = TriskoLocalization.Text("Нажмите «Добавить SSH» или импортируйте корпоративный комплект .gac3.");

    [Reactive]
    public partial bool IsSidebarCollapsed { get; set; }

    [Reactive]
    public partial GeksaThemeMode ThemeMode { get; set; } = GeksaThemeMode.Brand;

    public bool IsBrandTheme => ThemeMode == GeksaThemeMode.Brand;
    public bool IsDarkTheme => ThemeMode == GeksaThemeMode.Dark;
    public bool IsLightTheme => ThemeMode == GeksaThemeMode.Light;

    public string ThemeTooltipText => ThemeMode switch
    {
        GeksaThemeMode.Brand => TriskoLocalization.Text("Тема: Trisko (нажмите для смены)"),
        GeksaThemeMode.Dark => TriskoLocalization.Text("Тема: Тёмная (нажмите для смены)"),
        GeksaThemeMode.Light => TriskoLocalization.Text("Тема: Светлая (нажмите для смены)"),
        _ => TriskoLocalization.Text("Сменить тему")
    };

    public string ThemeButtonText => ThemeMode switch
    {
        GeksaThemeMode.Brand => TriskoLocalization.Text("Тема Trisko"),
        GeksaThemeMode.Dark => TriskoLocalization.Text("Тёмная тема"),
        GeksaThemeMode.Light => TriskoLocalization.Text("Светлая тема"),
        _ => TriskoLocalization.Text("Сменить тему")
    };

    public string ThemeSummaryText => ThemeMode switch
    {
        GeksaThemeMode.Brand => TriskoLocalization.Text("Активна: Trisko"),
        GeksaThemeMode.Dark => TriskoLocalization.Text("Активна: Тёмная (для комфортной работы ночью)"),
        GeksaThemeMode.Light => TriskoLocalization.Text("Активна: Светлая (высококонтрастный дневной режим)"),
        _ => TriskoLocalization.Text("Выбор темы оформления")
    };

    [Reactive]
    public partial bool AutoConnectSmbOnVpn { get; set; }

    [Reactive]
    public partial bool AutoDisconnectSmbOnVpn { get; set; }

    public ObservableCollection<GeksaResourceItemViewModel> Resources { get; } = [];

    public ObservableCollection<GeksaResourceItemViewModel> RemoteDesktopResources { get; } = [];

    public ObservableCollection<GeksaResourceItemViewModel> FileStorageResources { get; } = [];

    public ObservableCollection<GeksaResourceItemViewModel> SshResources { get; } = [];

    public ObservableCollection<GeksaEcpDeviceViewModel> EcpDevices { get; } = [];

    public ObservableCollection<GeksaEcpServerItemViewModel> EcpServers { get; } = [];

    public ObservableCollection<GeksaResourceItemViewModel> PinnedRemoteDesktopResources { get; } = [];
    public ObservableCollection<GeksaResourceItemViewModel> UnpinnedRemoteDesktopResources { get; } = [];

    public ObservableCollection<GeksaResourceItemViewModel> PinnedFileStorageResources { get; } = [];
    public ObservableCollection<GeksaResourceItemViewModel> UnpinnedFileStorageResources { get; } = [];

    public ObservableCollection<GeksaResourceItemViewModel> PinnedSshResources { get; } = [];
    public ObservableCollection<GeksaResourceItemViewModel> UnpinnedSshResources { get; } = [];

    public ObservableCollection<GeksaNetworkInterfaceOption> NetworkInterfaceOptions { get; } = [];

    public ObservableCollection<GeksaVpnRoutingModeOption> VpnRoutingModeOptions { get; } = [];

    public ObservableCollection<GeksaAnyDeskStrategyOption> AnyDeskStrategyOptions { get; } = [];

    public ObservableCollection<GeksaAnyDeskConnection> AnyDeskConnections { get; } = [];


    public ObservableCollection<GeksaVpnRouteItemViewModel> ManualVpnRoutes { get; } = [];

    public ObservableCollection<GeksaVpnProfileOption> VpnProfiles { get; } = [];

    public ObservableCollection<GeksaVpnProfileOption> RegularVpnProfiles { get; } = [];

    public ObservableCollection<GeksaVpnProfileOption> AnyDeskVpnProfiles { get; } = [];

    public ObservableCollection<GeksaConnectionLogEntry> ConnectionLogEntries { get; } = [];

    public string ProductVersionText =>
        TriskoLocalization.Format("Trisko connect · {0}", Utils.GetVersionInfo());

    public string CurrentVersionText => $"Trisko connect {Utils.GetVersionInfo()}";

    [Reactive]
    public partial string StateTitle { get; set; } = TriskoLocalization.Text("VPN отключён");

    [Reactive]
    public partial string StateDescription { get; set; } = TriskoLocalization.Text("Нажмите «Подключиться», чтобы открыть корпоративные ресурсы.");

    [Reactive]
    public partial string ProfileName { get; set; } = TriskoLocalization.Text("VPN-профиль не выбран");

    [Reactive]
    public partial GeksaVpnProfileOption? SelectedVpnProfile { get; set; }

    [Reactive]
    public partial GeksaVpnProfileOption? EffectiveVpnProfile { get; set; }

    [Reactive]
    public partial string SelectedVpnDisplayName { get; set; } = TriskoLocalization.Text("Не выбран");

    [Reactive]
    public partial bool HasVpnProfiles { get; set; }

    [Reactive]
    public partial bool HasRegularVpnProfiles { get; set; }

    [Reactive]
    public partial bool HasAnyDeskVpnProfiles { get; set; }

    [Reactive]
    public partial int RegularVpnProfileCount { get; set; }

    [Reactive]
    public partial int AnyDeskVpnProfileCount { get; set; }

    [Reactive]
    public partial bool CanSelectVpnProfile { get; set; }

    [Reactive]
    public partial string VpnProfileSelectionHint { get; set; } =
        TriskoLocalization.Text("Выбранный профиль будет использован при следующем подключении.");

    [Reactive]
    public partial bool IsVpnProfileListExpanded { get; set; }

    [Reactive]
    public partial bool IsVpnServersModalOpen { get; set; }

    [Reactive]
    public partial bool IsRegularVpnServerSection { get; set; } = true;

    [Reactive]
    public partial bool HasRegularVpnSubscription { get; set; }

    [Reactive]
    public partial GeksaSubscriptionUsagePresentation RegularSubscriptionUsage { get; set; } =
        GeksaSubscriptionUsagePresentation.Empty;


    [Reactive]
    public partial bool IsRegularVpnServersExpanded { get; set; } = true;

    [Reactive]
    public partial bool IsAnyDeskVpnServersExpanded { get; set; } = true;

    [Reactive]
    public partial bool IsVpnProfileTestBusy { get; set; }

    [Reactive]
    public partial bool IsVpnSubscriptionBusy { get; set; }

    [Reactive]
    public partial string VpnSubscriptionStatus { get; set; } =
        TriskoLocalization.Text("VPN-подписка ещё не проверена");

    [Reactive]
    public partial bool HasConnectionLogEntries { get; set; }

    public Task InitializationCompleted => _initializationCompleted.Task;

    [Reactive]
    public partial string? FailureMessage { get; set; }

    [Reactive]
    public partial bool IsConnected { get; set; }

    [Reactive]
    public partial bool IsConnecting { get; set; }

    [Reactive]
    public partial bool IsConnectionIdle { get; set; } = true;

    [Reactive]
    public partial string ConnectionDurationText { get; set; } = "00:00:00";

    [Reactive]
    public partial bool IsBusy { get; set; }

    [Reactive]
    public partial bool CanConnect { get; set; } = true;

    [Reactive]
    public partial bool CanDisconnect { get; set; }

    [Reactive]
    public partial bool IsConfigurationBusy { get; set; }

    [Reactive]
    public partial bool HasResources { get; set; }

    [Reactive]
    public partial bool HasNoResources { get; set; } = true;

    [Reactive]
    public partial string ConfigurationStatus { get; set; } = TriskoLocalization.Text("Корпоративный комплект ещё не импортирован");

    [Reactive]
    public partial bool IsConnectionPage { get; set; } = true;

    [Reactive]
    public partial bool IsRemoteDesktopsPage { get; set; }

    [Reactive]
    public partial bool IsFileStoragePage { get; set; }

    [Reactive]
    public partial bool IsSshAccessPage { get; set; }

    [Reactive]
    public partial bool IsEcpPage { get; set; }

    [Reactive]
    public partial bool IsSettingsPage { get; set; }

    [Reactive]
    public partial bool IsEcpBusy { get; set; }

    [Reactive]
    public partial bool IsVirtualHereRunning { get; set; }

    [Reactive]
    public partial bool HasEcpDevices { get; set; }

    [Reactive]
    public partial bool HasNoEcpDevices { get; set; } = true;

    [Reactive]
    public partial string EcpStatusText { get; set; } = TriskoLocalization.Text("Поиск USB-устройств ещё не выполнялся");

    [Reactive]
    public partial string? EcpFailureMessage { get; set; }

    [Reactive]
    public partial GeksaSettingsSubPage CurrentSettingsSubPage { get; set; } = GeksaSettingsSubPage.Main;

    [Reactive]
    public partial bool IsSettingsMainSubPage { get; set; } = true;

    [Reactive]
    public partial bool IsSettingsOneCSubPage { get; set; }

    [Reactive]
    public partial bool IsSettingsRoutingSubPage { get; set; }

    [Reactive]
    public partial bool IsSettingsSmbSubPage { get; set; }

    [Reactive]
    public partial bool IsSettingsNetworkSubPage { get; set; }

    [Reactive]
    public partial bool IsSettingsEcpServersSubPage { get; set; }

    [Reactive]
    public partial bool IsSettingsUpdatesSubPage { get; set; }

    [Reactive]
    public partial bool IsSettingsAnyDeskSubPage { get; set; }

    [Reactive]
    public partial bool IsAnyDeskIntegrationEnabled { get; set; } = true;

    [Reactive]
    public partial bool AutoStartAnyDeskOnLaunch { get; set; }

    [Reactive]
    public partial bool IsAnyDeskIntegrationView { get; set; } = true;

    [Reactive]
    public partial bool IsEmbeddedAnyDeskView { get; set; }

    [Reactive]
    public partial bool IsAnyDeskFullscreenOpen { get; set; }

    public bool IsAnyDeskEmbeddedWindowAttached
    {
        get => _isAnyDeskEmbeddedWindowAttached;
        set
        {
            this.RaiseAndSetIfChanged(ref _isAnyDeskEmbeddedWindowAttached, value);
            if (value)
            {
                _anyDeskEmbeddedLaunchRequested = false;
            }
        }
    }

    private bool _isAnyDeskSectionOpen;

    public bool IsEmbeddedAnyDeskActive =>
        _isAnyDeskSectionOpen && IsRemoteDesktopsPage && IsAnyDeskIntegrationEnabled && IsEmbeddedAnyDeskView && !IsAnyModalOpen;

    public void SetAnyDeskSectionOpen(bool isOpen)
    {
        if (_isAnyDeskSectionOpen == isOpen) return;
        _isAnyDeskSectionOpen = isOpen;
        this.RaisePropertyChanged(nameof(IsEmbeddedAnyDeskActive));
        if (IsEmbeddedAnyDeskActive) _ = ShowEmbeddedAnyDeskViewAsync();
    }

    [Reactive]
    public partial GeksaAnyDeskStrategyOption? SelectedAnyDeskStrategy { get; set; }

    [Reactive]
    public partial string AnyDeskExecutablePath { get; set; } = GeksaAnyDeskClientLocator.BundledExecutablePath;

    [Reactive]
    public partial string AnyDeskClientStatusText { get; set; } = TriskoLocalization.Text("Проверка portable-клиента ещё не выполнена");

    [Reactive]
    public partial string AnyDeskSettingsStatus { get; set; } =
        TriskoLocalization.Text("Интерфейс подготовлен. Запуск AnyDesk и сетевой стратегии будут добавлены следующим этапом.");

    [Reactive]
    public partial bool IsAnyDeskClientReady { get; set; }

    [Reactive]
    public partial bool IsAnyDeskBusy { get; set; }

    [Reactive]
    public partial bool HasAnyDeskConnections { get; set; }

    [Reactive]
    public partial bool HasNoAnyDeskConnections { get; set; } = true;

    [Reactive]
    public partial string AnyDeskManualAddress { get; set; } = string.Empty;

    [Reactive]
    public partial string AnyDeskNetworkStatusText { get; set; } = TriskoLocalization.Text("AnyDesk ещё не запущен");

    [Reactive]
    public partial string AnyDeskNetworkStatusColor { get; set; } = "#8FA6C6";

    [Reactive]
    public partial string AnyDeskWorkstationAddressText { get; set; } = TriskoLocalization.Text("ID ещё не получен");

    [Reactive]
    public partial string? AnyDeskActionStatus { get; set; }

    [Reactive]
    public partial string? AnyDeskStrategyWarningText { get; set; }

    [Reactive]
    public partial string? AnyDeskEnvironmentWarningText { get; set; }

    [Reactive]
    public partial string AnyDeskStrategyWarningColor { get; set; } = "#F3B35A";

    public bool IsAnyModalOpen => IsVpnServersModalOpen || IsGlobalSearchOpen || IsAnyDeskNameOpen;

    public bool HasAnyDeskStrategyWarning => AnyDeskStrategyWarningText.IsNotEmpty();

    public bool HasAnyDeskEnvironmentWarning => AnyDeskEnvironmentWarningText.IsNotEmpty();

    public int AnyDeskConnectionCount => AnyDeskConnections.Count;


    public string AnyDeskIntegrationSummaryText =>
        !IsAnyDeskIntegrationEnabled
            ? TriskoLocalization.Text("Интеграция выключена")
            : TriskoLocalization.Format("{0} · ", SelectedAnyDeskStrategy?.DisplayName ?? TriskoLocalization.Text("Напрямую")) +
              (IsAnyDeskClientReady ? TriskoLocalization.Text("AnyDesk 6.1.0 готов") : TriskoLocalization.Text("требуется проверка клиента")) +
              (AutoStartAnyDeskOnLaunch ? TriskoLocalization.Text(" · автозапуск") : string.Empty);

    public string AnyDeskIntegrationStateText =>
        IsAnyDeskIntegrationEnabled ? TriskoLocalization.Text("ВКЛЮЧЕНА") : TriskoLocalization.Text("ВЫКЛЮЧЕНА");

    [Reactive]
    public partial string NewEcpServerHost { get; set; } = string.Empty;

    [Reactive]
    public partial string NewEcpServerPort { get; set; } = "7575";

    [Reactive]
    public partial string EcpServerSettingsStatus { get; set; } =
        TriskoLocalization.Text("Добавленные серверы применяются, когда VirtualHere запущен.");

    [Reactive]
    public partial bool IsEcpServerSettingsBusy { get; set; }

    [Reactive]
    public partial bool HasEcpServers { get; set; }

    [Reactive]
    public partial bool HasNoEcpServers { get; set; } = true;

    [Reactive]
    public partial bool RouteEcpServersThroughVpn { get; set; } = true;

    [Reactive]
    public partial bool RouteEcpInCorporateOnly { get; set; } = true;

    [Reactive]
    public partial bool RouteEcpInRussianSites { get; set; } = true;

    [Reactive]
    public partial bool RouteEcpInGlobalExceptLocal { get; set; } = true;

    [Reactive]
    public partial bool RouteEcpInGlobal { get; set; } = true;

    [Reactive]
    public partial string EcpRoutingSettingStatus { get; set; } =
        TriskoLocalization.Text("По умолчанию серверы VirtualHere считаются корпоративными ресурсами VPN.");

    [Reactive]
    public partial bool AutoStartVirtualHereOnLaunch { get; set; }

    [Reactive]
    public partial string AutoStartVirtualHereSettingStatus { get; set; } =
        TriskoLocalization.Text("VirtualHere запускается вручную из раздела «ЭЦП».");

    [Reactive]
    public partial bool IsEcpAutoScanEnabled { get; set; } = true;

    [Reactive]
    public partial string EcpAutoScanIntervalText { get; set; } = "5";

    [Reactive]
    public partial string EcpAutoScanSettingsStatus { get; set; } =
        TriskoLocalization.Text("Автосканирование работает в фоне и обновляет статусы устройств без перерегистрации серверов.");

    public int EcpAutoScanIntervalSeconds { get; private set; } = 5;

    public string EcpServerSettingsSummaryText
    {
        get
        {
            var servers = EcpServers.Count switch
            {
                0 => TriskoLocalization.Text("Серверы не указаны"),
                1 => TriskoLocalization.Text("Настроен 1 сервер"),
                _ => TriskoLocalization.Format("Настроено серверов: {0}", EcpServers.Count)
            };
            var autoStart = AutoStartVirtualHereOnLaunch
                ? TriskoLocalization.Text("автозапуск включён")
                : TriskoLocalization.Text("автозапуск выключен");
            var autoScan = IsEcpAutoScanEnabled
                ? TriskoLocalization.Format("автосканирование каждые {0} сек.", EcpAutoScanIntervalSeconds)
                : TriskoLocalization.Text("автосканирование выключено");
            var routing = RouteEcpServersThroughVpn
                ? TriskoLocalization.Format("через VPN в режимах: {0}/4", EnabledEcpRoutingModeCount)
                : TriskoLocalization.Text("не считается корпоративным ресурсом");
            return $"{servers} · {autoStart} · {autoScan} · {routing}";
        }
    }

    private int EnabledEcpRoutingModeCount =>
        (RouteEcpInCorporateOnly ? 1 : 0) +
        (RouteEcpInRussianSites ? 1 : 0) +
        (RouteEcpInGlobalExceptLocal ? 1 : 0) +
        (RouteEcpInGlobal ? 1 : 0);

    private void SaveEcpRoutingModeSetting(Action updateConfig)
    {
        if (_appConfig.UiItem is null)
        {
            return;
        }

        updateConfig();
        _ = ConfigHandler.SaveConfig(_appConfig);
        UpdateEcpRoutingSettingStatus(reconnectRequired: true);
        this.RaisePropertyChanged(nameof(EcpServerSettingsSummaryText));
    }

    private void UpdateEcpRoutingSettingStatus(bool reconnectRequired)
    {
        var suffix = reconnectRequired
            ? TriskoLocalization.Text(" Изменение применится после переподключения VPN.")
            : string.Empty;

        EcpRoutingSettingStatus = RouteEcpServersThroughVpn
            ? TriskoLocalization.Format("VirtualHere включён для режимов: {0} из 4.{1}", EnabledEcpRoutingModeCount, suffix)
            : TriskoLocalization.Format("VirtualHere не считается корпоративным ресурсом ни в одном режиме.{0}", suffix);
    }

    public string OneCSettingsSummaryText => IsOneCConfigured
        ? TriskoLocalization.Format("Настроено: {0} ({1})", OneCServerName, OneCServerAddress)
        : TriskoLocalization.Text("Имя и маршрут сервера 1С не настроены");

    public string VpnRoutingSummaryText => SelectedVpnRoutingModeOption?.DisplayName ?? TriskoLocalization.Text("Только корпоративные ресурсы");

    [Reactive]
    public partial bool AutoLaunchResourcesOnVpn { get; set; } = true;

    public string SmbAutomationSummaryText
    {
        get
        {
            var autoLaunchCount = Resources?.Count(r => r.AutoLaunchOnVpn) ?? 0;
            var launchPart = AutoLaunchResourcesOnVpn
                ? (autoLaunchCount > 0 ? TriskoLocalization.Format("Автозапуск: {0} подкл.", autoLaunchCount) : TriskoLocalization.Text("Автозапуск включен"))
                : TriskoLocalization.Text("Автозапуск выключен");

            var smbPart = (AutoConnectSmbOnVpn, AutoDisconnectSmbOnVpn) switch
            {
                (true, true) => TriskoLocalization.Text("диски: подключение и отключение"),
                (true, false) => TriskoLocalization.Text("диски: автоподключение"),
                (false, true) => TriskoLocalization.Text("диски: автоотключение"),
                _ => TriskoLocalization.Text("диски: без автоподключения")
            };

            return $"{launchPart}  ·  {smbPart}";
        }
    }

    public string NetworkInterfaceSummaryText => SelectedNetworkInterfaceOption?.DisplayName ?? TriskoLocalization.Text("Автоматически (рекомендуется)");

    [Reactive]
    public partial bool AutoCheckUpdatesEnabled { get; set; } = true;

    [Reactive]
    public partial int CheckUpdateIntervalHours { get; set; } = 2;

    [Reactive]
    public partial string CheckUpdateIntervalHoursText { get; set; } = "2";

    [Reactive]
    public partial string AutoCheckUpdatesStatus { get; set; } =
        TriskoLocalization.Text("Автоматическая проверка обновлений выполняется каждые 2 ч.");

    public string UpdatesSummaryText =>
        AutoCheckUpdatesEnabled
            ? TriskoLocalization.Format("{0} · Автопроверка каждые {1} ч.", CurrentVersionText, CheckUpdateIntervalHours)
            : TriskoLocalization.Format("{0} · Фоновая проверка выключена", CurrentVersionText);

    [Reactive]
    public partial bool HasRemoteDesktopResources { get; set; }

    [Reactive]
    public partial bool HasNoRemoteDesktopResources { get; set; } = true;

    [Reactive]
    public partial bool HasFileStorageResources { get; set; }

    [Reactive]
    public partial bool HasNoFileStorageResources { get; set; } = true;

    [Reactive]
    public partial bool HasSshResources { get; set; }

    [Reactive]
    public partial bool HasNoSshResources { get; set; } = true;

    [Reactive]
    public partial bool HasPinnedRemoteDesktopResources { get; set; }

    [Reactive]
    public partial bool HasUnpinnedRemoteDesktopResources { get; set; }

    [Reactive]
    public partial bool HasPinnedFileStorageResources { get; set; }

    [Reactive]
    public partial bool HasUnpinnedFileStorageResources { get; set; }

    [Reactive]
    public partial bool HasPinnedSshResources { get; set; }

    [Reactive]
    public partial bool HasUnpinnedSshResources { get; set; }

    [Reactive]
    public partial string ResourceSummaryText { get; set; } = TriskoLocalization.Text("Ресурсы ещё не загружены");

    [Reactive]
    public partial string ConnectionButtonText { get; set; } = TriskoLocalization.Text("Подключиться");

    [Reactive]
    public partial string ConnectionStatusBadge { get; set; } = TriskoLocalization.Text("ОТКЛЮЧЕНО");

    [Reactive]
    public partial string StatusColor { get; set; } = "#75849A";

    [Reactive]
    public partial bool CanRunPrimaryAction { get; set; }

    [Reactive]
    public partial bool HasConnectionError { get; set; }

    [Reactive]
    public partial GeksaNetworkInterfaceOption? SelectedNetworkInterfaceOption { get; set; }

    [Reactive]
    public partial string EffectiveNetworkInterfaceText { get; set; } =
        TriskoLocalization.Text("Фактический адаптер будет определён при подключении");

    [Reactive]
    public partial string NetworkInterfaceSettingStatus { get; set; } =
        TriskoLocalization.Text("Автоматический выбор активен");

    [Reactive]
    public partial bool CanEditNetworkInterface { get; set; } = true;

    [Reactive]
    public partial GeksaVpnRoutingModeOption? SelectedVpnRoutingModeOption { get; set; }

    [Reactive]
    public partial string NewManualVpnRoute { get; set; } = string.Empty;

    [Reactive]
    public partial string VpnRoutingSettingStatus { get; set; } =
        TriskoLocalization.Text("По умолчанию через VPN идут только корпоративные IP.");

    [Reactive]
    public partial bool CanEditVpnRouting { get; set; } = true;

    [Reactive]
    public partial bool HasNoManualVpnRoutes { get; set; } = true;

    [Reactive]
    public partial string OneCServerName { get; set; } = "geksadata";

    [Reactive]
    public partial string OneCServerAddress { get; set; } = "192.168.3.222";

    [Reactive]
    public partial string OneCSystemStatusTitle { get; set; } = TriskoLocalization.Text("Не настроено");

    [Reactive]
    public partial string OneCSystemStatusMessage { get; set; } =
        TriskoLocalization.Text("Нажмите «Настроить систему и программу», чтобы подготовить Windows для 1С.");

    [Reactive]
    public partial string OneCSystemStatusColor { get; set; } = "#8FA6C6";

    [Reactive]
    public partial string OneCHostsEntriesText { get; set; } = string.Empty;

    [Reactive]
    public partial bool HasOneCHostsEntries { get; set; }

    [Reactive]
    public partial bool IsOneCSystemBusy { get; set; }

    [Reactive]
    public partial bool IsOneCConfigured { get; set; }

    [Reactive]
    public partial string OneCRouteText { get; set; } = TriskoLocalization.Text("Маршрут 1С ещё не включён");

    public string OneCHostsFilePath { get; }

    [Reactive]
    public partial string StartupDiagnosticsTitle { get; set; } = TriskoLocalization.Text("Проверка запуска выполняется…");

    [Reactive]
    public partial string StartupDiagnosticsMessage { get; set; } =
        TriskoLocalization.Text("Проверяются права, локальные порты, процессы и службы.");

    [Reactive]
    public partial string StartupDiagnosticsColor { get; set; } = "#6F9CFF";

    [Reactive]
    public partial bool IsLatencyTestBusy { get; set; }

    [Reactive]
    public partial bool CanTestLatency { get; set; }

    [Reactive]
    public partial string LatencyStatusText { get; set; } =
        TriskoLocalization.Text("Проверка задержки ещё не выполнялась");

    [Reactive]
    public partial string LatencyStatusColor { get; set; } = "#8FA6C6";

    [Reactive]
    public partial string LocalConfigurationStatus { get; set; } =
        TriskoLocalization.Text("Подключения можно добавлять и изменять вручную");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var initialized = false;
        try
        {
            ApplyStartupDiagnostics(GeksaStartupDiagnosticsService.Instance.LastReport);
            RefreshNetworkInterfaceOptions();
            await LoadConfigurationAsync(cancellationToken);
            foreach (var subscription in await AppManager.Instance.SubItems() ?? [])
            {
                var interval = GeksaSubscriptionPolicy.MigrateInterval(subscription.AutoUpdateInterval);
                if (interval != subscription.AutoUpdateInterval)
                {
                    subscription.AutoUpdateInterval = interval;
                    if (await ConfigHandler.AddSubItem(_appConfig, subscription) != 0)
                    {
                        throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось сохранить интервал обновления подписки."));
                    }
                }
            }
            await RefreshVpnProfilesAsync(cancellationToken);
            await RefreshVpnSubscriptionStatusAsync();
            await _controller.RefreshAsync(cancellationToken);
            await RefreshOneCSystemStatusAsync();
            await RefreshSmbStatusesSafelyAsync();
            if (IsAnyDeskIntegrationEnabled)
            {
                if (AutoStartAnyDeskOnLaunch)
                {
                    try
                    {
                        await _anyDeskService.StartAsync(AnyDeskExecutablePath, cancellationToken);
                        AnyDeskActionStatus = TriskoLocalization.Text("AnyDesk автоматически запущен вместе с программой.");
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException)
                    {
                        Logging.SaveLog(nameof(InitializeAsync), ex);
                        AnyDeskActionStatus = ex.Message;
                    }
                }
                await RefreshAnyDeskEnvironmentAsync();
            }
            if (AutoStartVirtualHereOnLaunch)
            {
                await ApplyConfiguredEcpServersAsync(cancellationToken);
                await RefreshEcpDevicesAsync();
                IsVirtualHereRunning = true;
                _ecpAutoScanTask ??= MonitorEcpAutoScanAsync(_monitorCancellation.Token);
            }
            AddConnectionLog(TriskoLocalization.Text("Приложение готово. Выберите подключение и нажмите «Подключиться»."), "#8FA6C6");
            _monitorTask ??= MonitorVpnAsync(_monitorCancellation.Token);
            _connectionDurationTask ??= MonitorConnectionDurationAsync(_monitorCancellation.Token);
            if (IsAnyDeskIntegrationEnabled)
            {
                _anyDeskMonitorTask ??= MonitorAnyDeskAsync(_monitorCancellation.Token);
            }
            initialized = true;
        }
        finally
        {
            _initializationCompleted.TrySetResult(initialized);
        }
    }

    public async Task PromptForSubscriptionOnboardingAsync()
    {
        await InitializationCompleted;
        if (_subscriptionPromptShown)
        {
            return;
        }
        _subscriptionPromptShown = true;

        if (!await NeedsSubscriptionOnboardingAsync())
        {
            return;
        }

    }

    public async Task<bool> NeedsSubscriptionOnboardingAsync()
    {
        await InitializationCompleted;
        return false;
    }

    public async Task ShowAdvancedVpnSettingsAsync()
    {
        await ShowAdvancedVpnSettingsInteraction.Handle(RxVoid.Default);
        await RefreshVpnProfilesAsync(CancellationToken.None);
        await _controller.RefreshAsync();
    }

    public async Task ShowUpdateManagerAsync()
    {
        await ShowUpdateManagerInteraction.Handle(RxVoid.Default);
    }

    public async Task<string?> TryPrepareApplicationExitAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await _virtualHereService.IsRunningAsync(cancellationToken))
            {
                var devices = await _virtualHereService.ScanAsync(cancellationToken);
                var connectedDevices = devices
                    .Where(device => device.State == GeksaVirtualUsbState.InUseLocally)
                    .ToList();
                if (connectedDevices.Count > 0)
                {
                    SelectPage(GeksaShellPage.Ecp);
                    var names = string.Join(", ", connectedDevices.Select(device => device.DisplayName));
                    EcpFailureMessage =
                        TriskoLocalization.Format("Перед выходом отключите подключённые к этому компьютеру USB-устройства: {0}.", names);
                    return
                        TriskoLocalization.Text("Нельзя закрыть Trisko connect, пока используются устройства ЭЦП.\n\n") +
                        TriskoLocalization.Format("Сначала отключите: {0}.", names);
                }

                await _virtualHereService.ShutdownAsync(cancellationToken);
            }
        }
        catch (GeksaVirtualHereException ex)
        {
            SelectPage(GeksaShellPage.Ecp);
            EcpFailureMessage = ex.UserMessage;
            return
                TriskoLocalization.Text("Не удалось проверить состояние устройств ЭЦП или завершить VirtualHere. ") +
                TriskoLocalization.Text("Повторите попытку после проверки раздела «ЭЦП».\n\n") + ex.UserMessage;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            SelectPage(GeksaShellPage.Ecp);
            EcpFailureMessage = TriskoLocalization.Text("Не удалось проверить состояние устройств перед выходом.");
            return
                TriskoLocalization.Text("Не удалось проверить состояние устройств ЭЦП. ") +
                TriskoLocalization.Text("Программа останется открытой, чтобы не оставить USB-устройства подключёнными.");
        }

        try
        {
            await _anyDeskService.StopOwnedAsync(cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(TryPrepareApplicationExitAsync), ex);
            return
                TriskoLocalization.Text("Не удалось завершить запущенный программой AnyDesk. ") +
                TriskoLocalization.Text("Остановите их вручную и повторите выход.");
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _startupSettingsSubscriptions) subscription.Dispose();
        _failureNotificationSubscription?.Dispose();
        Notifications.Dispose();
        _monitorCancellation.Cancel();
        _vpnSpeedtestService?.ExitLoop();
        _snapshotSubscription.Dispose();
        if (_virtualHereService is IDisposable disposableVirtualHereService)
        {
            disposableVirtualHereService.Dispose();
        }
        // In-flight operations still release these gates while cancellation drains the test worker.
        _monitorCancellation.Dispose();
    }

    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
    }

    private void ToggleShowConnectionNav()
    {
        if (ShowConnectionNav && CountVisibleNavSections() <= 1)
        {
            return;
        }
        ShowConnectionNav = !ShowConnectionNav;
        OnNavVisibilityChanged();
    }

    private void ToggleShowRdpNav()
    {
        if (ShowRdpNav && CountVisibleNavSections() <= 1)
        {
            return;
        }
        ShowRdpNav = !ShowRdpNav;
        OnNavVisibilityChanged();
    }

    private void ToggleShowSmbNav()
    {
        if (ShowSmbNav && CountVisibleNavSections() <= 1)
        {
            return;
        }
        ShowSmbNav = !ShowSmbNav;
        OnNavVisibilityChanged();
    }

    private void ToggleShowSshNav()
    {
        if (ShowSshNav && CountVisibleNavSections() <= 1)
        {
            return;
        }
        ShowSshNav = !ShowSshNav;
        OnNavVisibilityChanged();
    }

    private void ToggleShowEcpNav()
    {
        if (ShowEcpNav && CountVisibleNavSections() <= 1)
        {
            return;
        }
        ShowEcpNav = !ShowEcpNav;
        OnNavVisibilityChanged();
    }

    private void ToggleShowSettingsNav()
    {
        if (ShowSettingsNav && CountVisibleNavSections() <= 1)
        {
            return;
        }
        ShowSettingsNav = !ShowSettingsNav;
        OnNavVisibilityChanged();
    }

    private void ShowAllNavSections()
    {
        ShowConnectionNav = true;
        ShowRdpNav = true;
        ShowSmbNav = true;
        ShowSshNav = true;
        ShowEcpNav = true;
        ShowSettingsNav = true;
        OnNavVisibilityChanged();
    }

    private int CountVisibleNavSections()
    {
        var count = 0;
        if (ShowConnectionNav) count++;
        if (ShowRdpNav) count++;
        if (ShowSmbNav) count++;
        if (ShowSshNav) count++;
        if (ShowEcpNav) count++;
        if (ShowSettingsNav) count++;
        return count;
    }

    private void OnNavVisibilityChanged()
    {
        if (_appConfig.UiItem != null)
        {
            _appConfig.UiItem.GeksaShowConnectionNav = ShowConnectionNav;
            _appConfig.UiItem.GeksaShowRdpNav = ShowRdpNav;
            _appConfig.UiItem.GeksaShowSmbNav = ShowSmbNav;
            _appConfig.UiItem.GeksaShowSshNav = ShowSshNav;
            _appConfig.UiItem.GeksaShowEcpNav = ShowEcpNav;
            _appConfig.UiItem.GeksaShowSettingsNav = ShowSettingsNav;
            _ = ConfigHandler.SaveConfig(_appConfig);
        }

        EnsureCurrentPageIsVisible();
    }

    private void EnsureCurrentPageIsVisible()
    {
        var isCurrentVisible =
            (IsConnectionPage && ShowConnectionNav) ||
            (IsRemoteDesktopsPage && ShowRdpNav) ||
            (IsFileStoragePage && ShowSmbNav) ||
            (IsSshAccessPage && ShowSshNav) ||
            (IsEcpPage && ShowEcpNav) ||
            (IsSettingsPage && ShowSettingsNav);

        if (!isCurrentVisible)
        {
            if (ShowConnectionNav) SelectPage(GeksaShellPage.Connection);
            else if (ShowRdpNav) SelectPage(GeksaShellPage.RemoteDesktops);
            else if (ShowSmbNav) SelectPage(GeksaShellPage.FileStorage);
            else if (ShowSshNav) SelectPage(GeksaShellPage.SshAccess);
            else if (ShowEcpNav) SelectPage(GeksaShellPage.Ecp);
            else if (ShowSettingsNav) SelectPage(GeksaShellPage.Settings);
        }
    }

    public void SetTheme(GeksaThemeMode mode)
    {
        ThemeMode = mode;

        if (_appConfig.UiItem != null)
        {
            _appConfig.UiItem.GeksaThemeMode = ThemeMode;
            _ = Task.Run(async () => await ConfigHandler.SaveConfig(_appConfig));
        }
    }

    private void ToggleTheme()
    {
        var next = ThemeMode switch
        {
            GeksaThemeMode.Brand => GeksaThemeMode.Dark,
            GeksaThemeMode.Dark => GeksaThemeMode.Light,
            _ => GeksaThemeMode.Brand
        };
        SetTheme(next);
    }

    private async Task ConnectAsync()
    {
        if (IsBusy || _controller.Snapshot.IsCoreRunning)
        {
            return;
        }
        _isVpnConnectionBusy = true;
        ApplySnapshot(_controller.Snapshot);
        try
        {
            await ConnectSelectedProfileAsync();
        }
        catch (Exception ex)
        {
            _desiredConnected = false;
            Logging.SaveLog(nameof(ConnectAsync), ex);
            ReportOperationFailure(TriskoLocalization.Text("Не удалось подключиться к VPN."), ex);
        }
        finally
        {
            _isVpnConnectionBusy = false;
            ApplySnapshot(_controller.Snapshot);
            if (_desiredConnected && _controller.Snapshot.State == GeksaVpnState.Failed
                && _controller.Snapshot.Failure?.Retryable == true)
                ScheduleReconnect();
            UpdateReconnectCountdown();
        }
    }

    private async Task ConnectSelectedProfileAsync()
    {
        _desiredConnected = false;
        _localFailureMessage = null;
        FailureMessage = null;
        if (_appConfig.UiItem.GeksaAutoSelectVpnProfile)
        {
            _automaticProfileId = null;
            SynchronizeSelectedVpnProfile(_appConfig.IndexId);
            AddConnectionLog(
                _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile
                    ? TriskoLocalization.Text("Автоматический выбор самого быстрого VPN-сервера.")
                    : TriskoLocalization.Text("Режим «Авто»: ищу самый быстрый сервер."),
                "#72E5BD");
            if (await TestAllVpnProfilesAsync() is null)
            {
                _desiredConnected = false;
                return;
            }

            try
            {
                await ConnectUsingAutomaticFallbackAsync();
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(ConnectUsingAutomaticFallbackAsync), ex);
                _desiredConnected = false;
                try
                {
                    await _controller.DisconnectAsync();
                }
                catch (Exception cleanupException)
                {
                    Logging.SaveLog(nameof(ConnectUsingAutomaticFallbackAsync), cleanupException);
                }
                _localFailureMessage = _controller.Snapshot.IsCoreRunning
                    ? TriskoLocalization.Text("Автоматическое подключение прервано. Не удалось остановить VPN-ядро.")
                    : TriskoLocalization.Text("Автоматическое подключение завершилось с ошибкой. VPN отключён.");
                FailureMessage = _localFailureMessage;
                AddConnectionLog(_localFailureMessage, "#F0717E");
            }
            return;
        }

        if (await AppManager.Instance.GetProfileItem(_appConfig.IndexId) is null)
        {
            _localFailureMessage = TriskoLocalization.Text("Выбранный сервер больше не существует. Обновите список и выберите сервер.");
            FailureMessage = _localFailureMessage;
            return;
        }
        AddConnectionLog(TriskoLocalization.Text("Запускаю VPN-ядро и проверяю локальный SOCKS-порт."), "#72E5BD");
        _desiredConnected = true;
        _reconnectAttempt = 0;
        _nextReconnectAtUtc = DateTimeOffset.MinValue;
        var snapshot = await _controller.ConnectAsync();
        ApplyStartupDiagnostics(GeksaStartupDiagnosticsService.Instance.LastReport);
        UpdateEffectiveNetworkInterfaceText();
        if (snapshot.State == GeksaVpnState.Failed && snapshot.Failure?.Retryable != true)
        {
            _desiredConnected = false;
        }
    }

    private async Task RunPrimaryActionAsync()
    {
        if (CanDisconnect)
        {
            await DisconnectAsync();
            return;
        }

        if (CanConnect)
        {
            await ConnectAsync();
        }
    }

    private async Task DisconnectAsync()
    {
        _localFailureMessage = null;
        FailureMessage = null;
        _desiredConnected = false;
        _reconnectAttempt = 0;
        _nextReconnectAtUtc = DateTimeOffset.MinValue;
        UpdateReconnectCountdown();
        try { await _controller.DisconnectAsync(); }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(DisconnectAsync), ex);
            ReportOperationFailure(TriskoLocalization.Text("Не удалось отключить VPN."), ex);
        }
    }

    private async Task ImportConfigurationAsync()
    {
        var path = await BrowseCorporateConfigurationInteraction.Handle(RxVoid.Default);
        if (path.IsNullOrEmpty())
        {
            return;
        }

        IsConfigurationBusy = true;
        _localFailureMessage = null;
        FailureMessage = null;
        try
        {
            var userState = await _userResourceService.LoadStateAsync(CancellationToken.None);
            var preview = await _configurationService.PrepareImportFileAsync(
                path,
                userState,
                CancellationToken.None);
            var decision = await ConfirmCorporateConfigurationImportInteraction.Handle(preview);
            if (!decision.Confirmed)
            {
                ConfigurationStatus = TriskoLocalization.Text("Импорт отменён. Действующие конфигурации не изменены.");
                return;
            }

            var result = await _configurationService.ImportAsync(
                preview.Candidate,
                CancellationToken.None);
            string? postImportWarning = null;
            if (!decision.KeepLocalChanges && preview.HasLocalChanges)
            {
                try
                {
                    await _userResourceService.ClearManagedChangesAsync(
                        preview.ManagedResourceIds,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(GeksaShellViewModel), ex);
                    postImportWarning =
                        TriskoLocalization.Text("Комплект установлен, но локальные изменения не удалось сбросить. Они пока продолжают действовать.");
                }
            }
            await LoadConfigurationAsync(CancellationToken.None);
            await RefreshVpnProfilesAsync(CancellationToken.None);
            await _controller.RefreshAsync();
            ConfigurationStatus = result.Changed
                ? TriskoLocalization.Format("Установлена конфигурация {0}, ревизия {1}. ", result.Configuration.ConfigurationVersion, result.Configuration.Revision) +
                  (decision.KeepLocalChanges && preview.HasLocalChanges
                      ? TriskoLocalization.Format("Сохранены локальные изменения: {0}; скрытые ресурсы: {1}.", preview.LocalOverrideCount, preview.LocallyHiddenCount)
                      : TriskoLocalization.Text("Локальные данные обработаны согласно выбранному режиму."))
                : TriskoLocalization.Format("Конфигурация {0} уже установлена. ", result.Configuration.ConfigurationVersion) +
                  (!decision.KeepLocalChanges && preview.HasLocalChanges
                      ? TriskoLocalization.Text("Локальные изменения корпоративных ресурсов сброшены.")
                      : TriskoLocalization.Text("Изменений не требуется."));
            if (postImportWarning is not null)
            {
                ConfigurationStatus =
                    TriskoLocalization.Format("Конфигурация {0}, ревизия {1} установлена. ", result.Configuration.ConfigurationVersion, result.Configuration.Revision) +
                    postImportWarning;
                FailureMessage = postImportWarning;
            }
            Notifications.Show(TriskoLocalization.Text("Конфигурация"), TriskoLocalization.Text("Корпоративная конфигурация установлена."), GeksaNotificationKind.Success);
        }
        catch (GeksaConfigurationException ex)
        {
            ReportOperationFailure(ex.UserMessage, ex);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            ReportOperationFailure(TriskoLocalization.Text("Не удалось импортировать корпоративную конфигурацию."), ex);
        }
        finally
        {
            IsConfigurationBusy = false;
        }
    }

    private async Task ExportConfigurationAsync()
    {
        if (IsConfigurationBusy)
        {
            return;
        }

        IsConfigurationBusy = true;
        _localFailureMessage = null;
        FailureMessage = null;
        try
        {
            var active = await _configurationService.LoadAsync(CancellationToken.None);
            var userState = await _userResourceService.LoadStateAsync(CancellationToken.None);
            var allResources = GeksaUserResourceService.Merge(
                active?.Resources ?? [],
                userState.Resources,
                userState.DeletedManagedResourceIds).ToList();

            var nextRevision = Math.Max(
                active?.Revision ?? 0,
                _appConfig.TunModeItem.GeksaLastExportRevision) + 1;

            var configToExport = new GeksaCorporateConfiguration
            {
                SchemaVersion = GeksaCorporateConfigurationCodec.CurrentSchemaVersion,
                ConfigurationVersion = active?.ConfigurationVersion.IsNotEmpty() == true
                    ? active.ConfigurationVersion
                    : $"export-{DateTime.UtcNow:yyyy.MM.dd}",
                Revision = nextRevision,
                Resources = allResources
            };

            var content = GeksaCorporateConfigurationCodec.SerializeConfiguration(configToExport);
            var defaultName = GeksaConfigurationFileIO.SanitizeSuggestedFileName(
                $"geksa-{configToExport.ConfigurationVersion}-r{configToExport.Revision}",
                $"geksa-export-r{configToExport.Revision}") + ".gac3";
            var targetPath = await SaveCorporateConfigurationInteraction.Handle(defaultName);
            if (targetPath.IsNullOrEmpty())
            {
                return;
            }
            if (!targetPath.EndsWith(".gac3", StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.ChangeExtension(targetPath, ".gac3");
            }

            var previousExportRevision = _appConfig.TunModeItem.GeksaLastExportRevision;
            _appConfig.TunModeItem.GeksaLastExportRevision = nextRevision;
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                _appConfig.TunModeItem.GeksaLastExportRevision = previousExportRevision;
                throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Не удалось зарезервировать уникальную ревизию экспортируемого комплекта."));
            }

            await GeksaConfigurationFileIO.WriteTextAtomicallyAsync(
                targetPath,
                content,
                CancellationToken.None);

            ConfigurationStatus =
                TriskoLocalization.Format("Комплект ресурсов ревизии {0} экспортирован в «{1}». ", nextRevision, Path.GetFileName(targetPath)) +
                TriskoLocalization.Text("VPN-профили и подписки в файл не включены.");
            Notifications.Show(TriskoLocalization.Text("Конфигурация"), TriskoLocalization.Text("Комплект корпоративных ресурсов экспортирован."), GeksaNotificationKind.Success);
        }
        catch (GeksaConfigurationException ex)
        {
            ReportOperationFailure(ex.UserMessage, ex);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            ReportOperationFailure(TriskoLocalization.Text("Ошибка при экспорте корпоративной конфигурации."), ex);
        }
        finally
        {
            IsConfigurationBusy = false;
        }
    }

    private async Task ShowFormatHelpAsync()
    {
        await ShowFormatHelpInteraction.Handle(RxVoid.Default);
    }

    private async Task LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        var active = await _configurationService.LoadAsync(cancellationToken);
        var userState = await _userResourceService.LoadStateAsync(cancellationToken);
        var resources = GeksaUserResourceService.Merge(
            active?.Resources ?? [],
            userState.Resources,
            userState.DeletedManagedResourceIds);
        ApplyResources(resources);
        _ = RefreshSmbStatusesSafelyAsync();

        if (active is null)
        {
            ConfigurationStatus = TriskoLocalization.Text("Корпоративный комплект ещё не импортирован");
            return;
        }

        ConfigurationStatus =
            TriskoLocalization.Format("Конфигурация {0}, ревизия {1}", active.ConfigurationVersion, active.Revision);
    }

    private static bool MatchesSearch(GeksaResourceItemViewModel item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var q = query.Trim();
        return item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (item.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.TargetText?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.Resource.Host?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.Resource.UserName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.Resource.Path?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void FilterRdpResources()
    {
        RemoteDesktopResources.Clear();
        PinnedRemoteDesktopResources.Clear();
        UnpinnedRemoteDesktopResources.Clear();

        foreach (var item in _allRdpResources.OrderByDescending(r => r.IsPinned))
        {
            if (MatchesSearch(item, RdpSearchQuery))
            {
                RemoteDesktopResources.Add(item);
                if (item.IsPinned)
                {
                    PinnedRemoteDesktopResources.Add(item);
                }
                else
                {
                    UnpinnedRemoteDesktopResources.Add(item);
                }
            }
        }

        HasPinnedRemoteDesktopResources = PinnedRemoteDesktopResources.Count > 0;
        HasUnpinnedRemoteDesktopResources = UnpinnedRemoteDesktopResources.Count > 0;
        HasRemoteDesktopResources = RemoteDesktopResources.Count > 0;
        HasNoRemoteDesktopResources = !HasRemoteDesktopResources;

        if (_allRdpResources.Count > 0 && RemoteDesktopResources.Count == 0)
        {
            RdpEmptyTitle = TriskoLocalization.Format("Ничего не найдено по запросу «{0}»", RdpSearchQuery);
            RdpEmptySubtitle = TriskoLocalization.Text("Попробуйте изменить поисковый запрос или очистить поле поиска.");
        }
        else
        {
            RdpEmptyTitle = TriskoLocalization.Text("RDP-подключения не загружены");
            RdpEmptySubtitle = TriskoLocalization.Text("Нажмите «Добавить RDP» или импортируйте корпоративный комплект .gac3.");
        }
    }

    private void FilterFileStorageResources()
    {
        FileStorageResources.Clear();
        PinnedFileStorageResources.Clear();
        UnpinnedFileStorageResources.Clear();

        foreach (var item in _allFileStorageResources.OrderByDescending(r => r.IsPinned))
        {
            if (MatchesSearch(item, FileStorageSearchQuery))
            {
                FileStorageResources.Add(item);
                if (item.IsPinned)
                {
                    PinnedFileStorageResources.Add(item);
                }
                else
                {
                    UnpinnedFileStorageResources.Add(item);
                }
            }
        }

        HasPinnedFileStorageResources = PinnedFileStorageResources.Count > 0;
        HasUnpinnedFileStorageResources = UnpinnedFileStorageResources.Count > 0;
        HasFileStorageResources = FileStorageResources.Count > 0;
        HasNoFileStorageResources = !HasFileStorageResources;

        if (_allFileStorageResources.Count > 0 && FileStorageResources.Count == 0)
        {
            FileStorageEmptyTitle = TriskoLocalization.Format("Ничего не найдено по запросу «{0}»", FileStorageSearchQuery);
            FileStorageEmptySubtitle = TriskoLocalization.Text("Попробуйте изменить поисковый запрос или очистить поле поиска.");
        }
        else
        {
            FileStorageEmptyTitle = TriskoLocalization.Text("Файловые ресурсы не загружены");
            FileStorageEmptySubtitle = TriskoLocalization.Text("Нажмите «Добавить SMB» или импортируйте корпоративный комплект .gac3.");
        }
    }

    private void FilterSshResources()
    {
        SshResources.Clear();
        PinnedSshResources.Clear();
        UnpinnedSshResources.Clear();

        foreach (var item in _allSshResources.OrderByDescending(r => r.IsPinned))
        {
            if (MatchesSearch(item, SshSearchQuery))
            {
                SshResources.Add(item);
                if (item.IsPinned)
                {
                    PinnedSshResources.Add(item);
                }
                else
                {
                    UnpinnedSshResources.Add(item);
                }
            }
        }

        HasPinnedSshResources = PinnedSshResources.Count > 0;
        HasUnpinnedSshResources = UnpinnedSshResources.Count > 0;
        HasSshResources = SshResources.Count > 0;
        HasNoSshResources = !HasSshResources;

        if (_allSshResources.Count > 0 && SshResources.Count == 0)
        {
            SshEmptyTitle = TriskoLocalization.Format("Ничего не найдено по запросу «{0}»", SshSearchQuery);
            SshEmptySubtitle = TriskoLocalization.Text("Попробуйте изменить поисковый запрос или очистить поле поиска.");
        }
        else
        {
            SshEmptyTitle = TriskoLocalization.Text("SSH-подключения не загружены");
            SshEmptySubtitle = TriskoLocalization.Text("Нажмите «Добавить SSH» или импортируйте корпоративный комплект .gac3.");
        }
    }

    private void ApplyResources(IEnumerable<GeksaResourceProfile> resources)
    {
        var existingSmbStatuses = _allFileStorageResources
            .Where(item => !string.IsNullOrEmpty(item.Resource.Id) && item.LastSmbStatus != null)
            .ToDictionary(item => item.Resource.Id, item => item.LastSmbStatus!);

        ClearResources();
        foreach (var resource in resources)
        {
            var item = new GeksaResourceItemViewModel(
                resource,
                LaunchResourceAsync,
                EditResourceAsync,
                TogglePinResourceAsync,
                ToggleAutoLaunchResourceAsync,
                DeleteResourceAsync,
                ToggleResourceFavoriteAsync,
                _appConfig.UiItem?.GeksaFavoriteResourceIds.Contains(resource.Id) == true);
            item.UpdateAvailability(IsConnected);
            if (resource.Id != null && existingSmbStatuses.TryGetValue(resource.Id, out var existingStatus))
            {
                item.ApplySmbStatus(existingStatus);
            }
            Resources.Add(item);

            switch (GeksaCorporateConfigurationCodec.ParseResourceType(resource.Type))
            {
                case GeksaResourceType.Rdp:
                    _allRdpResources.Add(item);
                    break;
                case GeksaResourceType.Ssh:
                    _allSshResources.Add(item);
                    break;
                case GeksaResourceType.Smb:
                    _allFileStorageResources.Add(item);
                    break;
            }
        }

        FilterRdpResources();
        FilterFileStorageResources();
        FilterSshResources();

        HasResources = Resources.Count > 0;
        HasNoResources = !HasResources;
        ResourceSummaryText = HasResources
            ? TriskoLocalization.Format("RDP: {0}  ·  Файлы: {1}  ·  SSH: {2}", _allRdpResources.Count, _allFileStorageResources.Count, _allSshResources.Count)
            : TriskoLocalization.Text("Ресурсы ещё не загружены");
    }

    private async Task TogglePinResourceAsync(GeksaResourceProfile profile)
    {
        try
        {
            await _userResourceService.UpsertAsync(profile);
            await LoadConfigurationAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            FailureMessage = TriskoLocalization.Text("Не удалось сохранить закрепление подключения.");
        }
    }

    private async Task ToggleAutoLaunchResourceAsync(GeksaResourceProfile profile)
    {
        try
        {
            await _userResourceService.UpsertAsync(profile);
            await LoadConfigurationAsync(CancellationToken.None);
            this.RaisePropertyChanged(nameof(SmbAutomationSummaryText));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            FailureMessage = TriskoLocalization.Text("Не удалось сохранить автозапуск подключения.");
        }
    }

    private void ClearResources()
    {
        Resources.Clear();
        _allRdpResources.Clear();
        _allFileStorageResources.Clear();
        _allSshResources.Clear();
        RemoteDesktopResources.Clear();
        PinnedRemoteDesktopResources.Clear();
        UnpinnedRemoteDesktopResources.Clear();
        FileStorageResources.Clear();
        PinnedFileStorageResources.Clear();
        UnpinnedFileStorageResources.Clear();
        SshResources.Clear();
        PinnedSshResources.Clear();
        UnpinnedSshResources.Clear();
        HasResources = false;
        HasNoResources = true;
        HasRemoteDesktopResources = false;
        HasNoRemoteDesktopResources = true;
        HasPinnedRemoteDesktopResources = false;
        HasUnpinnedRemoteDesktopResources = false;
        HasFileStorageResources = false;
        HasNoFileStorageResources = true;
        HasPinnedFileStorageResources = false;
        HasUnpinnedFileStorageResources = false;
        HasSshResources = false;
        HasNoSshResources = true;
        HasPinnedSshResources = false;
        HasUnpinnedSshResources = false;
        RdpEmptyTitle = TriskoLocalization.Text("RDP-подключения не загружены");
        RdpEmptySubtitle = TriskoLocalization.Text("Нажмите «Добавить RDP» или импортируйте корпоративный комплект .gac3.");
        FileStorageEmptyTitle = TriskoLocalization.Text("Файловые ресурсы не загружены");
        FileStorageEmptySubtitle = TriskoLocalization.Text("Нажмите «Добавить SMB» или импортируйте корпоративный комплект .gac3.");
        SshEmptyTitle = TriskoLocalization.Text("SSH-подключения не загружены");
        SshEmptySubtitle = TriskoLocalization.Text("Нажмите «Добавить SSH» или импортируйте корпоративный комплект .gac3.");
        ResourceSummaryText = TriskoLocalization.Text("Ресурсы ещё не загружены");
    }

    public void SelectSettingsSubPage(GeksaSettingsSubPage subPage)
    {
        CurrentSettingsSubPage = subPage;
        IsSettingsMainSubPage = subPage == GeksaSettingsSubPage.Main;
        IsSettingsOneCSubPage = subPage == GeksaSettingsSubPage.OneCSystem;
        IsSettingsRoutingSubPage = subPage == GeksaSettingsSubPage.VpnRouting;
        IsSettingsSmbSubPage = subPage == GeksaSettingsSubPage.SmbAutomation;
        IsSettingsNetworkSubPage = subPage == GeksaSettingsSubPage.NetworkInterface;
        IsSettingsEcpServersSubPage = subPage == GeksaSettingsSubPage.EcpServers;
        IsSettingsUpdatesSubPage = subPage == GeksaSettingsSubPage.Updates;
        IsSettingsAnyDeskSubPage = subPage == GeksaSettingsSubPage.AnyDesk;

        if (subPage == GeksaSettingsSubPage.VpnRouting)
        {
            RefreshVpnRoutingSettings();
        }
        else if (subPage == GeksaSettingsSubPage.NetworkInterface)
        {
            RefreshNetworkInterfaceOptions();
        }
        else if (subPage == GeksaSettingsSubPage.OneCSystem)
        {
            _ = RefreshOneCSystemStatusAsync();
        }
        else if (subPage == GeksaSettingsSubPage.EcpServers)
        {
            EcpServerSettingsStatus =
                TriskoLocalization.Text("Серверы применяются и обновляются, только когда VirtualHere запущен.");
        }
        else if (subPage == GeksaSettingsSubPage.AnyDesk)
        {
            RefreshAnyDeskClientStatus();
            if (IsAnyDeskIntegrationEnabled)
            {
                _ = RefreshAnyDeskEnvironmentAsync();
            }
        }
    }

    private async Task ShowAnyDeskIntegrationViewAsync()
    {
        SetAnyDeskViewMode(GeksaAnyDeskViewMode.Integration);
        if (_appConfig.UiItem is not null)
        {
            _appConfig.UiItem.GeksaAnyDeskViewMode = GeksaAnyDeskViewMode.Integration;
            await ConfigHandler.SaveConfig(_appConfig);
        }
    }

    private async Task ShowEmbeddedAnyDeskViewAsync()
    {
        SetAnyDeskViewMode(GeksaAnyDeskViewMode.EmbeddedClient);
        if (_appConfig.UiItem is not null)
        {
            _appConfig.UiItem.GeksaAnyDeskViewMode = GeksaAnyDeskViewMode.EmbeddedClient;
            await ConfigHandler.SaveConfig(_appConfig);
        }

        if (!IsAnyDeskIntegrationEnabled)
        {
            return;
        }

        try
        {
            RefreshAnyDeskClientStatus();
            if (!IsAnyDeskClientReady)
            {
                AnyDeskActionStatus = AnyDeskClientStatusText;
                return;
            }
            if (IsAnyDeskEmbeddedWindowAttached || IsAnyDeskFullscreenOpen || _anyDeskEmbeddedLaunchRequested)
            {
                AnyDeskActionStatus = TriskoLocalization.Text("Окно AnyDesk открыто внутри приложения.");
                return;
            }

            _anyDeskEmbeddedLaunchRequested = true;
            await _anyDeskService.ShowAsync(AnyDeskExecutablePath, _monitorCancellation.Token);
            _ = ResetAnyDeskEmbeddedLaunchRequestAsync();
            AnyDeskActionStatus = TriskoLocalization.Text("Окно AnyDesk открыто внутри приложения.");
        }
        catch (Exception ex)
        {
            _anyDeskEmbeddedLaunchRequested = false;
            Logging.SaveLog(nameof(ShowEmbeddedAnyDeskViewAsync), ex);
            AnyDeskActionStatus = ex is FileNotFoundException or InvalidOperationException
                ? ex.Message
                : TriskoLocalization.Text("Не удалось открыть встроенное окно AnyDesk.");
        }
    }

    private async Task ResetAnyDeskEmbeddedLaunchRequestAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), _monitorCancellation.Token);
            if (!IsAnyDeskEmbeddedWindowAttached)
            {
                _anyDeskEmbeddedLaunchRequested = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetAnyDeskViewMode(GeksaAnyDeskViewMode mode)
    {
        IsEmbeddedAnyDeskView = mode == GeksaAnyDeskViewMode.EmbeddedClient;
        IsAnyDeskIntegrationView = !IsEmbeddedAnyDeskView;
        this.RaisePropertyChanged(nameof(IsEmbeddedAnyDeskActive));
    }

    private async Task BrowseAnyDeskExecutableAsync()
    {
        var path = await BrowseAnyDeskExecutableInteraction.Handle(RxVoid.Default);
        if (path.IsNullOrEmpty())
        {
            return;
        }

        AnyDeskExecutablePath = path!;
        AnyDeskSettingsStatus = TriskoLocalization.Text("Путь выбран. Нажмите «Сохранить», чтобы использовать его после добавления запуска AnyDesk.");
    }

    private void UseBundledAnyDeskExecutable()
    {
        AnyDeskExecutablePath = GeksaAnyDeskClientLocator.BundledExecutablePath;
        AnyDeskSettingsStatus = TriskoLocalization.Text("Выбран portable-клиент, поставляемый вместе с Trisko connect.");
    }

    private async Task SaveAnyDeskSettingsAsync()
    {
        if (_appConfig.UiItem is null || SelectedAnyDeskStrategy is null)
        {
            return;
        }
        var info = GeksaAnyDeskClientLocator.Inspect(AnyDeskExecutablePath);
        if (IsAnyDeskIntegrationEnabled && !info.Exists)
        {
            AnyDeskSettingsStatus = TriskoLocalization.Text("Сохранение отменено: указанный AnyDesk.exe не найден.");
            return;
        }

        _appConfig.UiItem.GeksaAnyDeskIntegrationEnabled = IsAnyDeskIntegrationEnabled;
        _appConfig.UiItem.GeksaAutoStartAnyDeskOnLaunch = AutoStartAnyDeskOnLaunch;
        this.RaisePropertyChanged(nameof(IsEmbeddedAnyDeskActive));
        _appConfig.UiItem.GeksaAnyDeskViewMode = IsEmbeddedAnyDeskView
            ? GeksaAnyDeskViewMode.EmbeddedClient
            : GeksaAnyDeskViewMode.Integration;
        _appConfig.UiItem.GeksaAnyDeskStrategy =
            GeksaAnyDeskStrategy.Direct;
        _appConfig.UiItem.GeksaAnyDeskExecutablePath = info.Exists && string.Equals(
            Path.GetFullPath(info.Path),
            Path.GetFullPath(GeksaAnyDeskClientLocator.BundledExecutablePath),
            StringComparison.OrdinalIgnoreCase)
            ? null
            : AnyDeskExecutablePath;
        await ConfigHandler.SaveConfig(_appConfig);

        if (!IsAnyDeskIntegrationEnabled)
        {
            await _anyDeskService.StopOwnedAsync(_monitorCancellation.Token);
            ClearAnyDeskEnvironment();
            AnyDeskSettingsStatus = TriskoLocalization.Text("Интеграция AnyDesk выключена. Клиент не запускается и не отображается в рабочих столах.");
            this.RaisePropertyChanged(nameof(AnyDeskIntegrationSummaryText));
            return;
        }

        RefreshAnyDeskClientStatus();
        AnyDeskSettingsStatus = TriskoLocalization.Text("Настройки AnyDesk сохранены и применены.");
        await RefreshAnyDeskEnvironmentAsync();
        _anyDeskMonitorTask ??= MonitorAnyDeskAsync(_monitorCancellation.Token);
    }

    private void RefreshAnyDeskClientStatus()
    {
        EffectiveAnyDeskExecutablePath = GeksaAnyDeskClientLocator.ResolveExecutablePath(AnyDeskExecutablePath);
        var info = GeksaAnyDeskClientLocator.Inspect(EffectiveAnyDeskExecutablePath);
        IsAnyDeskClientReady = info.IsReady;
        AnyDeskClientStatusText = info.StatusText;
        this.RaisePropertyChanged(nameof(AnyDeskIntegrationSummaryText));
    }

    [Reactive] public partial string EffectiveAnyDeskExecutablePath { get; set; } = GeksaAnyDeskClientLocator.BundledExecutablePath;

    private async Task RefreshAnyDeskEnvironmentAsync()
    {
        if (!IsAnyDeskIntegrationEnabled || IsAnyDeskBusy)
        {
            return;
        }

        IsAnyDeskBusy = true;
        try
        {
            RefreshAnyDeskClientStatus();
            var status = await _anyDeskService.GetStatusAsync(
                AnyDeskExecutablePath,
                _monitorCancellation.Token);
            ApplyAnyDeskStatus(status);

            var inspection = await _anyDeskService.InspectEnvironmentAsync(
                AnyDeskExecutablePath,
                _monitorCancellation.Token);
            AnyDeskEnvironmentWarningText = inspection.WarningText;
            this.RaisePropertyChanged(nameof(HasAnyDeskEnvironmentWarning));
            if (inspection.HasWarnings
                && !string.Equals(
                    _lastAnyDeskEnvironmentWarning,
                    inspection.WarningKindKey,
                    StringComparison.Ordinal))
            {
                _lastAnyDeskEnvironmentWarning = inspection.WarningKindKey;
                await ShowAnyDeskWarningInteraction.Handle(inspection.WarningText!);
            }
            else if (!inspection.HasWarnings)
            {
                _lastAnyDeskEnvironmentWarning = null;
            }

            ApplyPersonalizedAnyDeskConnections();
            UpdateAnyDeskStrategyWarning();
        }
        catch (OperationCanceledException) when (_monitorCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RefreshAnyDeskEnvironmentAsync), ex);
            AnyDeskActionStatus = TriskoLocalization.Text("Не удалось обновить данные AnyDesk.");
        }
        finally
        {
            IsAnyDeskBusy = false;
        }
    }

    private async Task MonitorAnyDeskAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await RefreshAnyDeskEnvironmentAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private void ClearAnyDeskEnvironment()
    {
        AnyDeskConnections.Clear();
        HasAnyDeskConnections = false;
        HasNoAnyDeskConnections = true;
        AnyDeskEnvironmentWarningText = null;
        AnyDeskActionStatus = null;
        AnyDeskNetworkStatusText = TriskoLocalization.Text("Интеграция выключена");
        AnyDeskNetworkStatusColor = "#8FA6C6";
        AnyDeskWorkstationAddressText = TriskoLocalization.Text("AnyDesk отключён");
        _lastAnyDeskEnvironmentWarning = null;
        this.RaisePropertyChanged(nameof(AnyDeskConnectionCount));
        this.RaisePropertyChanged(nameof(HasAnyDeskEnvironmentWarning));
    }

    private async Task ConnectAnyDeskAsync(GeksaAnyDeskConnection connection)
    {
        if (connection is null)
        {
            return;
        }
        await LaunchAnyDeskConnectionAsync(connection.Address, connection.DisplayName);
    }

    private async Task ConnectManualAnyDeskAsync()
    {
        var address = AnyDeskManualAddress.Trim();
        if (!GeksaAnyDeskConfigurationParser.IsSafeAddress(address))
        {
            AnyDeskActionStatus = TriskoLocalization.Text("Укажите корректный AnyDesk ID или Alias.");
            return;
        }
        await LaunchAnyDeskConnectionAsync(address, address);
    }

    private async Task LaunchAnyDeskConnectionAsync(string address, string displayName)
    {
        if (!IsAnyDeskIntegrationEnabled)
        {
            AnyDeskActionStatus = TriskoLocalization.Text("Интеграция AnyDesk выключена в настройках.");
            return;
        }
        if (IsAnyDeskBusy)
        {
            return;
        }

        IsAnyDeskBusy = true;
        try
        {
            UpdateAnyDeskStrategyWarning();
            await _anyDeskService.ConnectAsync(
                AnyDeskExecutablePath,
                address,
                _monitorCancellation.Token);
            AnyDeskActionStatus = TriskoLocalization.Format("Подключение к «{0}» открыто в AnyDesk.", displayName);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(LaunchAnyDeskConnectionAsync), ex);
            AnyDeskActionStatus = ex is FileNotFoundException or InvalidOperationException
                ? ex.Message
                : TriskoLocalization.Text("Не удалось открыть подключение AnyDesk.");
        }
        finally
        {
            IsAnyDeskBusy = false;
        }
    }

    private void ApplyAnyDeskStatus(GeksaAnyDeskRuntimeStatus status)
    {
        AnyDeskNetworkStatusText = status.StatusText;
        AnyDeskNetworkStatusColor = status.StatusColor;
        AnyDeskWorkstationAddressText = status.WorkstationAddressText;
    }

    private void UpdateAnyDeskStrategyWarning()
    {
        AnyDeskStrategyWarningText = null;
        this.RaisePropertyChanged(nameof(HasAnyDeskStrategyWarning));
    }

    private void SelectPage(GeksaShellPage page)
    {
        IsConnectionPage = page == GeksaShellPage.Connection;
        IsRemoteDesktopsPage = page == GeksaShellPage.RemoteDesktops;
        IsFileStoragePage = page == GeksaShellPage.FileStorage;
        IsSshAccessPage = page == GeksaShellPage.SshAccess;
        IsEcpPage = page == GeksaShellPage.Ecp;
        IsSettingsPage = page == GeksaShellPage.Settings;
        this.RaisePropertyChanged(nameof(IsEmbeddedAnyDeskActive));
        if (IsConnectionPage)
        {
            _ = RefreshVpnProfilesSafelyAsync();
        }
        if (IsSettingsPage)
        {
            SelectSettingsSubPage(GeksaSettingsSubPage.Main);
            RefreshNetworkInterfaceOptions();
            RefreshVpnRoutingSettings();
            _ = RefreshOneCSystemStatusAsync();
        }
        if (IsFileStoragePage)
        {
            _ = RefreshSmbStatusesSafelyAsync();
        }
        if (IsRemoteDesktopsPage)
        {
            _ = RefreshAnyDeskEnvironmentAsync();
            if (IsEmbeddedAnyDeskActive)
            {
                _ = ShowEmbeddedAnyDeskViewAsync();
            }
        }
        if (IsEcpPage)
        {
            _ = EnterEcpPageAsync();
        }
    }

    private async Task RefreshVpnProfilesSafelyAsync()
    {
        if (IsBusy)
        {
            return;
        }
        try
        {
            await RefreshVpnProfilesAsync(CancellationToken.None);
            await _controller.RefreshAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RefreshVpnProfilesSafelyAsync), ex);
            VpnProfileSelectionHint = TriskoLocalization.Text("Не удалось обновить список VPN-профилей.");
        }
    }

    private async Task RefreshVpnProfilesAsync(CancellationToken cancellationToken, bool forTest = false)
    {
        if (!forTest && (_isVpnConnectionBusy || IsVpnProfileTestBusy || IsLatencyTestBusy || _isVpnSelectionBusy))
        {
            return;
        }
        var version = ++_vpnProfilesRefreshVersion;
        cancellationToken.ThrowIfCancellationRequested();
        var profiles = await AppManager.Instance.ProfileModels(string.Empty, string.Empty) ?? [];
        var subscriptions = await AppManager.Instance.SubItems() ?? [];
        UpdateSubscriptionPresentation(subscriptions);
        var profileExItems = await ProfileExManager.Instance.GetProfileExs();
        var delays = profileExItems.ToDictionary(
            item => item.IndexId,
            item => item.Delay,
            StringComparer.OrdinalIgnoreCase);
        cancellationToken.ThrowIfCancellationRequested();

        if (version != _vpnProfilesRefreshVersion ||
            (!forTest && (_isVpnConnectionBusy || IsVpnProfileTestBusy || IsLatencyTestBusy || _isVpnSelectionBusy)))
        {
            return;
        }
        var options = profiles
            .Where(profile => profile.IndexId.IsNotEmpty())
            .GroupBy(profile => profile.IndexId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var profile = group.First();
                return GeksaVpnProfileOption.From(profile);
            })
            .OrderBy(option => option.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        foreach (var option in options)
        {
            option.IsFavorite = _appConfig.UiItem?.GeksaFavoriteVpnIds.Contains(option.IndexId) == true;
            if (delays.TryGetValue(option.IndexId, out var delay) && delay != 0)
            {
                option.ApplyDelay(delay);
            }
        }

        _isSynchronizingVpnProfileSelection = true;
        try
        {
            VpnProfiles.Clear();
            RegularVpnProfiles.Clear();
            AnyDeskVpnProfiles.Clear();
            var regularOptions = options.Where(option => !option.IsAnyDesk).ToList();
            var anyDeskOptions = options.Where(option => option.IsAnyDesk).ToList();
            if (regularOptions.Count > 0)
            {
                var automatic = GeksaVpnProfileOption.CreateAutomatic();
                VpnProfiles.Add(automatic);
                RegularVpnProfiles.Add(automatic);
            }
            foreach (var option in regularOptions)
            {
                VpnProfiles.Add(option);
                RegularVpnProfiles.Add(option);
            }
            if (anyDeskOptions.Count > 0)
            {
                var automatic = GeksaVpnProfileOption.CreateAutomatic(isAnyDesk: true);
                VpnProfiles.Add(automatic);
                AnyDeskVpnProfiles.Add(automatic);
            }
            foreach (var option in anyDeskOptions)
            {
                VpnProfiles.Add(option);
                AnyDeskVpnProfiles.Add(option);
            }
            HasRegularVpnProfiles = regularOptions.Count > 0;
            HasAnyDeskVpnProfiles = anyDeskOptions.Count > 0;
            RegularVpnProfileCount = regularOptions.Count;
            AnyDeskVpnProfileCount = anyDeskOptions.Count;
            SelectedVpnProfile = _appConfig.UiItem.GeksaAutoSelectVpnProfile
                ? FindSelectedAutomaticProfile()
                : FindVpnProfile(_appConfig.IndexId);
        }
        finally
        {
            _isSynchronizingVpnProfileSelection = false;
        }

        HasVpnProfiles = options.Count > 0;
        SynchronizeSelectedVpnProfile(_appConfig.IndexId);
        UpdateAnyDeskStrategyWarning();
        UpdateVpnProfileSelectionHint();
    }

    private void SelectVpnServerSection(bool _) => IsRegularVpnServerSection = true;

    private void UpdateSubscriptionPresentation(IReadOnlyCollection<SubItem> subscriptions)
    {
        var enabled = subscriptions.Where(item => item.Enabled && item.Url.IsNotEmpty()).ToList();
        HasRegularVpnSubscription = enabled.Count > 0;
        RegularSubscriptionUsage = GeksaSubscriptionUsagePresentation.Create(enabled);
        Subscriptions.Clear();
        foreach (var subscription in subscriptions.Where(item => item.Url.IsNotEmpty()))
            Subscriptions.Add(new TriskoSubscriptionItem(subscription, this));
    }

    public ObservableCollection<TriskoSubscriptionItem> Subscriptions { get; } = [];

    public async Task SelectVpnProfileAsync(GeksaVpnProfileOption profile)
    {
        if (profile is null || _isSynchronizingVpnProfileSelection)
        {
            return;
        }

        if (await RejectVpnProfileChangeWhileConnectedAsync(profile))
        {
            return;
        }

        await _vpnProfileSelectionLock.WaitAsync();
        try
        {
            if (!CanSelectVpnProfile)
            {
                SynchronizeSelectedVpnProfile(_appConfig.IndexId);
                UpdateVpnProfileSelectionHint();
                return;
            }
            if (profile.IsAutomatic && IsSelectedAutomaticProfile(profile))
            {
                SynchronizeSelectedVpnProfile(_appConfig.IndexId);
                return;
            }
            if (!profile.IsAutomatic &&
                !_appConfig.UiItem.GeksaAutoSelectVpnProfile &&
                string.Equals(profile.IndexId, _appConfig.IndexId, StringComparison.OrdinalIgnoreCase))
            {
                SynchronizeSelectedVpnProfile(_appConfig.IndexId);
                return;
            }

            var previousIndexId = _appConfig.IndexId;
            var previousAutomatic = _appConfig.UiItem.GeksaAutoSelectVpnProfile;
            var previousAutomaticAnyDesk = _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile;
            _isVpnSelectionBusy = true;
            ApplySnapshot(_controller.Snapshot);
            _desiredConnected = false;
            _localFailureMessage = null;
            FailureMessage = null;
            try
            {
                if (profile.IsAutomatic)
                {
                    _automaticProfileId = null;
                    _appConfig.UiItem.GeksaAutoSelectVpnProfile = true;
                    _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile = profile.IsAnyDesk;
                    if (await ConfigHandler.SaveConfig(_appConfig) != 0)
                    {
                        throw new InvalidOperationException("The automatic VPN profile mode could not be saved.");
                    }
                }
                else
                {
                    var item = await AppManager.Instance.GetProfileItem(profile.IndexId);
                    if (item is null)
                    {
                        throw new InvalidOperationException("The selected VPN profile no longer exists.");
                    }

                    _appConfig.UiItem.GeksaAutoSelectVpnProfile = false;
                    _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile = false;
                    // Используем тот же штатный путь, что ProfilesViewModel.SetDefaultServer.
                    if (await ConfigHandler.SetDefaultServerIndex(_appConfig, profile.IndexId) != 0)
                    {
                        throw new InvalidOperationException("The selected VPN profile could not be saved.");
                    }
                }

                LatencyStatusText = TriskoLocalization.Text("Проверка задержки ещё не выполнялась");
                LatencyStatusColor = "#8FA6C6";
                if (!profile.IsAutomatic)
                {
                    await _controller.RefreshAsync();
                }
                VpnProfileSelectionHint =
                    profile.IsAutomatic
                        ? TriskoLocalization.Text("Режим «Авто» проверит все серверы и выберет самый быстрый перед подключением.")
                        : TriskoLocalization.Format("Профиль «{0}» выбран для следующего подключения.", profile.DisplayName);
                AddConnectionLog(
                    profile.IsAutomatic
                        ? TriskoLocalization.Text("Выбран режим «Авто».")
                        : TriskoLocalization.Format("Выбрано подключение: {0} ({1}).", profile.DisplayName, profile.ProtocolName),
                    "#72E5BD");
                SynchronizeSelectedVpnProfile(_appConfig.IndexId);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(SelectVpnProfileAsync), ex);
                _appConfig.IndexId = previousIndexId;
                _appConfig.UiItem.GeksaAutoSelectVpnProfile = previousAutomatic;
                _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile = previousAutomaticAnyDesk;
                SynchronizeSelectedVpnProfile(previousIndexId);
                _localFailureMessage = TriskoLocalization.Text("Не удалось выбрать VPN-профиль.");
                FailureMessage = _localFailureMessage;
            }
        }
        finally
        {
            _isVpnSelectionBusy = false;
            ApplySnapshot(_controller.Snapshot);
            _vpnProfileSelectionLock.Release();
        }
    }

    private async Task ConnectUsingAutomaticFallbackAsync()
    {
        _desiredConnected = false;
        _reconnectAttempt = 0;
        _nextReconnectAtUtc = DateTimeOffset.MinValue;
        var result = await GeksaAutoConnectSequence.RunAsync(
            _lastAutoConnectCandidates,
            async (candidate, attempt) =>
            {
                var profile = candidate.Profile;
                if (FindVpnProfile(profile.IndexId) is null ||
                    await AppManager.Instance.GetProfileItem(profile.IndexId) is null)
                {
                    AddConnectionLog(TriskoLocalization.Format("Сервер «{0}» удалён; пропускаю его.", profile.DisplayName), "#F3B35A");
                    return false;
                }

                _localFailureMessage = null;
                FailureMessage = null;
                if (await ConfigHandler.SetDefaultServerIndex(_appConfig, profile.IndexId) != 0)
                {
                    throw new GeksaConfigurationException(TriskoLocalization.Format("Не удалось сохранить выбор «{0}».", profile.DisplayName));
                }
                _automaticProfileId = profile.IndexId;
                SynchronizeSelectedVpnProfile(profile.IndexId);
                AddConnectionLog(
                    TriskoLocalization.Format("Авто: попытка {0} из {1} — ", attempt, Math.Min(3, _lastAutoConnectCandidates.Count)) +
                    TriskoLocalization.Format("{0}, {1} мс.", profile.DisplayName, candidate.DelayMilliseconds), "#72E5BD");
                return true;
            },
            async () =>
            {
                var snapshot = await _controller.ConnectAsync();
                ApplyStartupDiagnostics(GeksaStartupDiagnosticsService.Instance.LastReport);
                UpdateEffectiveNetworkInterfaceText();
                return snapshot;
            },
            () => _controller.DisconnectAsync(),
            (candidate, snapshot) => AddConnectionLog(
                TriskoLocalization.Format("Авто: «{0}» не подключился: ", candidate.Profile.DisplayName) +
                (snapshot.Failure?.Message ?? TriskoLocalization.Text("Ядро не подтвердило подключение выбранного профиля.")),
                "#F3B35A"));

        if (result.Connected is not null)
        {
            _desiredConnected = true;
            _localFailureMessage = null;
            FailureMessage = null;
            AddConnectionLog(TriskoLocalization.Format("Авто: подключение установлено через «{0}».", result.Connected.Profile.DisplayName), "#3FD69A");
            return;
        }

        _localFailureMessage = result.StopFailed
            ? TriskoLocalization.Text("Не удалось остановить VPN после неудачной попытки. Перебор серверов прекращён.")
            : result.Attempts == 0
                ? TriskoLocalization.Text("Серверы для подключения больше недоступны. Обновите подписку.")
                : TriskoLocalization.Format("Не удалось подключиться: попыток — {0}. VPN отключён.", result.Attempts);
        FailureMessage = _localFailureMessage;
        AddConnectionLog(_localFailureMessage, "#F0717E");
    }

    private void SynchronizeSelectedVpnProfile(string? indexId)
    {
        var snapshot = _controller.Snapshot;
        var actual = _appConfig.UiItem.GeksaAutoSelectVpnProfile
            ? FindVpnProfile(_controller.Snapshot.IsCoreRunning ? _controller.Snapshot.ProfileId : _automaticProfileId)
            : FindVpnProfile(indexId);
        if (snapshot.IsCoreRunning && snapshot.RunningProfile is { } running)
        {
            if (_displayedRunningProfile != running)
            {
                _displayedRunningProfile = running;
                _runningVpnProfileOption = GeksaVpnProfileOption.From(running);
            }
            var listed = FindVpnProfile(running.IndexId);
            // Reuse the live test result only if the listed endpoint still describes
            // the running configuration; subscription replacement may remove it.
            actual = listed is not null && listed.DisplayName == GeksaVpnProfileOption.CleanSubscriptionDisplayName(running.Name)
                && listed.Address == running.Address && listed.Port == running.Port
                && listed.ProtocolName == GeksaVpnProfileOption.GetProtocolName(running.Protocol)
                    ? listed : _runningVpnProfileOption;
        }
        var selected = _appConfig.UiItem.GeksaAutoSelectVpnProfile
            ? FindSelectedAutomaticProfile()
            : FindVpnProfile(indexId);
        EffectiveVpnProfile = actual ?? selected;
        SelectedVpnDisplayName = _appConfig.UiItem.GeksaAutoSelectVpnProfile
            ? actual is null
                ? selected?.IsAnyDesk == true
                    ? TriskoLocalization.Text("Авто AnyDesk · сервер ещё не выбран")
                    : TriskoLocalization.Text("Авто · сервер ещё не выбран")
                : selected?.IsAnyDesk == true
                    ? TriskoLocalization.Format("Авто AnyDesk · {0}", actual.DisplayName)
                    : TriskoLocalization.Format("Авто · {0}", actual.DisplayName)
            : actual?.DisplayName ?? selected?.DisplayName ?? TriskoLocalization.Text("Не выбран");
        if (ReferenceEquals(SelectedVpnProfile, selected))
        {
            return;
        }

        _isSynchronizingVpnProfileSelection = true;
        try
        {
            SelectedVpnProfile = selected;
        }
        finally
        {
            _isSynchronizingVpnProfileSelection = false;
        }
    }

    private GeksaVpnProfileOption? FindVpnProfile(string? indexId) =>
        VpnProfiles.FirstOrDefault(option => string.Equals(
            option.IndexId,
            indexId,
            StringComparison.OrdinalIgnoreCase));

    private GeksaVpnProfileOption? FindSelectedAutomaticProfile() =>
        FindVpnProfile(_appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile
            ? GeksaVpnProfileOption.AutomaticAnyDeskIndexId
            : GeksaVpnProfileOption.AutomaticIndexId);

    private bool IsSelectedAutomaticProfile(GeksaVpnProfileOption profile) =>
        profile.IsAutomatic &&
        _appConfig.UiItem.GeksaAutoSelectVpnProfile &&
        profile.IsAnyDesk == _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile;

    private async Task SelectVpnProfileFromMenuAsync(GeksaVpnProfileOption profile)
    {
        if (profile is null)
        {
            return;
        }

        await SelectVpnProfileAsync(profile);
    }

    private async Task ConnectVpnProfileAsync(GeksaVpnProfileOption profile)
    {
        if (profile is null || IsBusy)
        {
            return;
        }

        if (await RejectVpnProfileChangeWhileConnectedAsync(profile))
        {
            return;
        }

        await SelectVpnProfileAsync(profile);
        var selectedSuccessfully = profile.IsAutomatic
            ? IsSelectedAutomaticProfile(profile)
            : !_appConfig.UiItem.GeksaAutoSelectVpnProfile &&
              string.Equals(_appConfig.IndexId, profile.IndexId, StringComparison.OrdinalIgnoreCase);
        if (selectedSuccessfully)
        {
            await ConnectAsync();
        }
    }

    private async Task<bool> RejectVpnProfileChangeWhileConnectedAsync(GeksaVpnProfileOption profile)
    {
        if (!_controller.Snapshot.IsCoreRunning || IsCurrentVpnProfileSelection(profile))
        {
            return false;
        }

        SynchronizeSelectedVpnProfile(_appConfig.IndexId);
        UpdateVpnProfileSelectionHint();
        await ShowVpnProfileSwitchBlockedInteraction.Handle(
            TriskoLocalization.Text("Сейчас VPN уже подключён. Чтобы подключиться к другому серверу, ") +
            TriskoLocalization.Text("сначала отключите VPN, затем выберите нужный сервер и подключитесь снова."));
        return true;
    }

    private bool IsCurrentVpnProfileSelection(GeksaVpnProfileOption profile) =>
        profile.IsAutomatic
            ? IsSelectedAutomaticProfile(profile)
            : !_appConfig.UiItem.GeksaAutoSelectVpnProfile &&
              string.Equals(_appConfig.IndexId, profile.IndexId, StringComparison.OrdinalIgnoreCase);

    private void UpdateVpnProfileSelectionHint()
    {
        VpnProfileSelectionHint = !HasVpnProfiles
            ? TriskoLocalization.Text("VPN-профили не найдены. Добавьте VPN-ссылку или подписку.")
            : !CanSelectVpnProfile
                ? TriskoLocalization.Text("Чтобы сменить профиль, сначала отключите VPN.")
                : _appConfig.UiItem.GeksaAutoSelectVpnProfile
                    ? _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile
                        ? TriskoLocalization.Text("Перед подключением будет выбран самый быстрый сервер группы AnyDesk.")
                        : TriskoLocalization.Text("Перед подключением будет выбран самый быстрый сервер.")
                    : TriskoLocalization.Text("Выбранный профиль будет использован при следующем подключении.");
    }

    private async Task RefreshVpnSubscriptionStatusAsync()
    {
        var subscriptions = await AppManager.Instance.SubItems() ?? [];
        UpdateSubscriptionPresentation(subscriptions);
        var enabled = subscriptions.Count(item => item.Enabled && item.Url.IsNotEmpty());
        VpnSubscriptionStatus = enabled switch
        {
            0 => TriskoLocalization.Text("VPN-подписка не настроена"),
            1 => TriskoLocalization.Text("VPN-подписка настроена"),
            _ => TriskoLocalization.Format("Настроено VPN-подписок: {0}", enabled)
        };
    }

    private async Task ConfigureVpnSubscriptionAsync(
        bool onboarding,
        GeksaVpnSubscriptionRequest? directRequest = null, string? editId = null)
    {
        if (IsBusy || IsVpnSubscriptionBusy || _controller.Snapshot.IsCoreRunning)
        {
            return;
        }

        var subscriptions = await AppManager.Instance.SubItems() ?? [];
        var request = directRequest ?? await ConfigureVpnSubscriptionInteraction.Handle(
            new GeksaVpnSubscriptionEditContext(null));
        if (request is null)
        {
            if (onboarding)
            {
                AddConnectionLog(TriskoLocalization.Text("Настройка VPN-подписки отложена пользователем."), "#F3B35A");
            }
            return;
        }

        if (IsBusy || IsVpnSubscriptionBusy || _controller.Snapshot.IsCoreRunning)
        {
            return;
        }
        IsVpnSubscriptionBusy = true;
        ApplySnapshot(_controller.Snapshot);
        try
        {
            var subscription = TriskoSubscriptionPolicy.Resolve(subscriptions, request, editId);
            subscription.Url = request.Url;
            subscription.Remarks = request.Name.IsNotEmpty() ? request.Name : subscription.Remarks;
            subscription.Enabled = true;
            subscription.AutoUpdateInterval = subscription.AutoUpdateInterval > 0
                ? GeksaSubscriptionPolicy.MigrateInterval(subscription.AutoUpdateInterval)
                : GeksaSubscriptionPolicy.DefaultIntervalMinutes;
            if (await ConfigHandler.AddSubItem(_appConfig, subscription) != 0)
            {
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось сохранить VPN-подписку."));
            }

            subscription = (await AppManager.Instance.SubItems() ?? [])
                .First(item => string.Equals(item.Url, request.Url, StringComparison.Ordinal));
            _appConfig.SubIndexId = subscription.Id;
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                throw new GeksaConfigurationException(TriskoLocalization.Text("Подписка сохранена, но не выбрана."));
            }

            AddConnectionLog(TriskoLocalization.Text("VPN-подписка сохранена. Загружаю список серверов…"), "#72E5BD");
            var updated = await UpdateVpnSubscriptionsCoreAsync(subscription.Id);
            if (!updated)
            {
                throw new GeksaConfigurationException(
                    TriskoLocalization.Text("Не удалось получить серверы из подписки. Проверьте ссылку и доступ к интернету."));
            }

            await RefreshVpnProfilesAsync(CancellationToken.None);
            await RefreshVpnSubscriptionStatusAsync();
            await _controller.RefreshAsync();
            AddConnectionLog(TriskoLocalization.Text("Серверы подписки импортированы и готовы к выбору."), "#3FD69A");
        }
        catch (GeksaConfigurationException ex)
        {
            _localFailureMessage = ex.UserMessage;
            FailureMessage = _localFailureMessage;
            VpnSubscriptionStatus = ex.UserMessage;
            AddConnectionLog(ex.UserMessage, "#F0717E");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(ConfigureVpnSubscriptionAsync), ex);
            _localFailureMessage = TriskoLocalization.Text("Не удалось настроить VPN-подписку.");
            FailureMessage = _localFailureMessage;
            VpnSubscriptionStatus = _localFailureMessage;
            AddConnectionLog(_localFailureMessage, "#F0717E");
        }
        finally
        {
            // Keep saved subscriptions visible even when a provider is temporarily offline.
            try { await RefreshVpnSubscriptionStatusAsync(); }
            catch (Exception refreshError) { Logging.SaveLog(nameof(RefreshVpnSubscriptionStatusAsync), refreshError); }
            IsVpnSubscriptionBusy = false;
            ApplySnapshot(_controller.Snapshot);
        }
    }

    public async Task RunScheduledSubscriptionUpdateAsync(Func<Task> update)
    {
        if (!_initializationCompleted.Task.IsCompletedSuccessfully)
        {
            return;
        }

        Task pending = Task.CompletedTask;
        await RunOnUiThreadAsync(() =>
        {
            // Take the UI busy flag before the first await, just like manual updates.
            // A skipped update keeps its due time and is retried after disconnect.
            if (IsBusy || IsVpnSubscriptionBusy || _desiredConnected || _controller.Snapshot.IsCoreRunning || CoreManager.Instance.IsCoreRunning)
            {
                return;
            }
            pending = RunScheduledSubscriptionUpdateCoreAsync(update);
        });
        await pending;
    }

    private async Task RunScheduledSubscriptionUpdateCoreAsync(Func<Task> update)
    {
        IsVpnSubscriptionBusy = true;
        ApplySnapshot(_controller.Snapshot);
        try
        {
            await update();
            await RefreshVpnProfilesAsync(CancellationToken.None);
            await RefreshVpnSubscriptionStatusAsync();
            await _controller.RefreshAsync();
        }
        finally
        {
            IsVpnSubscriptionBusy = false;
            ApplySnapshot(_controller.Snapshot);
        }
    }

    private async Task UpdateVpnSubscriptionsAsync()
    {
        if (IsBusy)
        {
            return;
        }
        if (_controller.Snapshot.IsCoreRunning)
        {
            FailureMessage = TriskoLocalization.Text("Перед обновлением VPN-подписки отключите VPN.");
            AddConnectionLog(FailureMessage, "#F3B35A");
            return;
        }

        IsVpnSubscriptionBusy = true;
        ApplySnapshot(_controller.Snapshot);
        try
        {
            AddConnectionLog(TriskoLocalization.Text("Начато обновление VPN-подписки."), "#72E5BD");
            if (!await UpdateVpnSubscriptionsCoreAsync(string.Empty))
            {
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось обновить VPN-подписку."));
            }
            await RefreshVpnProfilesAsync(CancellationToken.None);
            await RefreshVpnSubscriptionStatusAsync();
            await _controller.RefreshAsync();
            AddConnectionLog(TriskoLocalization.Text("VPN-подписка обновлена."), "#3FD69A");
            Notifications.Show(TriskoLocalization.Text("Подписка"), TriskoLocalization.Text("VPN-подписка обновлена."), GeksaNotificationKind.Success);
        }
        catch (GeksaConfigurationException ex)
        {
            FailureMessage = ex.UserMessage;
            AddConnectionLog(ex.UserMessage, "#F0717E");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(UpdateVpnSubscriptionsAsync), ex);
            ReportOperationFailure(TriskoLocalization.Text("Не удалось обновить VPN-подписку."), ex);
            AddConnectionLog(FailureMessage, "#F0717E");
        }
        finally
        {
            IsVpnSubscriptionBusy = false;
            ApplySnapshot(_controller.Snapshot);
        }
    }

    private async Task<bool> UpdateVpnSubscriptionsCoreAsync(string subId)
    {
        var succeeded = false;
        await SubscriptionHandler.UpdateProcess(
            _appConfig,
            subId,
            blProxy: false,
            async (success, message) =>
            {
                succeeded |= success;
                if (message.IsNotEmpty() && !message.StartsWith("---", StringComparison.Ordinal))
                {
                    RunOnUiThread(() => VpnSubscriptionStatus = message);
                }
                await Task.CompletedTask;
            });
        return succeeded;
    }

    private async Task<GeksaVpnProfileOption?> TestAllVpnProfilesAsync()
    {
        _lastAutoConnectCandidates.Clear();
        var results = await RunVpnProfileTestsAsync(null);
        ApplyAutomaticProfileDelays(results);
        var automatic = _appConfig.UiItem.GeksaAutoSelectVpnProfile
            ? FindSelectedAutomaticProfile()
            : null;
        IReadOnlyList<GeksaVpnTestCandidate> eligible = automatic is null
            ? results
            : GeksaVpnProfileOption.CandidatesForAutomaticProfile(automatic, results);
        _lastAutoConnectCandidates.AddRange(eligible);
        var fastest = eligible.FirstOrDefault();
        if (fastest is null)
        {
            if (!_localFailureMessage.IsNullOrEmpty()) return null;
            _localFailureMessage = automatic is null
                ? TriskoLocalization.Text("VPN-серверы не ответили на проверку задержки.")
                : automatic.IsAnyDesk
                    ? TriskoLocalization.Text("Серверы AnyDesk не ответили на проверку задержки.")
                    : TriskoLocalization.Text("Обычные VPN-серверы не ответили на проверку задержки.");
            FailureMessage = _localFailureMessage;
            AddConnectionLog(_localFailureMessage, "#F0717E");
            return null;
        }

        var fastestMessage = automatic is null
            ? TriskoLocalization.Format("Самое быстрое подключение: {0} — {1} мс.", fastest.Profile.DisplayName, fastest.DelayMilliseconds)
            : TriskoLocalization.Format("Самое быстрое подключение в группе «{0}»: ", (automatic.IsAnyDesk ? "AnyDesk" : TriskoLocalization.Text("Обычные"))) +
              TriskoLocalization.Format("{0} — {1} мс.", fastest.Profile.DisplayName, fastest.DelayMilliseconds);
        AddConnectionLog(fastestMessage, "#3FD69A");
        // Testing never changes the active/default server. Selection belongs to the connection transaction.
        return fastest.Profile;
    }

    private async Task<IReadOnlyList<GeksaVpnTestCandidate>> RunVpnProfileTestsAsync(string? profileId)
    {
        if (!await _vpnProfileTestLock.WaitAsync(0))
        {
            return [];
        }

        GeksaVpnTestSession? session = null;
        List<GeksaVpnProfileOption> automaticProfiles = [];
        IsVpnProfileTestBusy = true;
        ApplySnapshot(_controller.Snapshot);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_monitorCancellation.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await RefreshVpnProfilesAsync(timeout.Token, forTest: true);
            _localFailureMessage = null;
            FailureMessage = null;
            var testConfig = _networkBindingSelector.CreateSpeedtestConfiguration(_appConfig);
            AddConnectionLog(TriskoLocalization.Format("Проверка задержки через адаптер «{0}» ({1}).", testConfig.CoreBasicItem.BindInterface, testConfig.CoreBasicItem.SendThrough), "#8FA6C6");
            var targets = VpnProfiles
                .Where(p => !p.IsAutomatic && (profileId is null || p.IndexId == profileId))
                .ToList();
            session = new GeksaVpnTestSession(targets);
            session.Start();
            if (profileId is null)
            {
                automaticProfiles = VpnProfiles.Where(profile => profile.IsAutomatic).ToList();
                foreach (var automatic in automaticProfiles)
                {
                    automatic.MarkTesting();
                }
            }
            var profiles = await AppManager.Instance.GetProfileItemsByIndexIds(targets.Select(p => p.IndexId));
            if (profiles.Count == 0)
            {
                throw new GeksaConfigurationException(TriskoLocalization.Text("Выбранные VPN-серверы больше не существуют. Обновите подписку."));
            }

            AddConnectionLog(TriskoLocalization.Format("Начата проверка реальной задержки: {0} серверов.", profiles.Count), "#72E5BD");
            _vpnSpeedtestService = new SpeedtestService(testConfig, result =>
                RunOnUiThreadAsync(() =>
                {
                    var option = session.Apply(result.IndexId ?? string.Empty, result.Delay);
                    if (option is not null)
                    {
                        AddConnectionLogCore($"{option.DisplayName}: {option.DelayText}", option.DelayColor);
                    }
                }));

            // The native task completes only after all test cores have stopped.
            await _vpnSpeedtestService.RunLoopAsync(ESpeedActionType.Realping, profiles, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            var ranked = session.Complete();
            ApplyAutomaticProfileDelays(ranked);
            if (ranked.Count == 0)
            {
                _localFailureMessage = TriskoLocalization.Text("VPN-серверы не ответили на проверку задержки.");
                FailureMessage = _localFailureMessage;
                AddConnectionLog(FailureMessage, "#F0717E");
            }
            return ranked;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RunVpnProfileTestsAsync), ex);
            _localFailureMessage = ex is OperationCanceledException
                ? TriskoLocalization.Text("Проверка серверов прервана или превысила время ожидания.")
                : ex is TimeoutException
                    ? TriskoLocalization.Text("Не удалось завершить остановку тестового VPN-ядра. Проверка завершилась с ошибкой.")
                : ex is GeksaConfigurationException configurationException
                    ? configurationException.UserMessage
                : ex is GeksaVpnOperationException vpnException
                    ? vpnException.UserMessage : TriskoLocalization.Text("Не удалось проверить VPN-серверы.");
            ReportOperationFailure(_localFailureMessage, ex);
            AddConnectionLog(FailureMessage, "#F0717E");
            return [];
        }
        finally
        {
            session?.Complete();
            foreach (var automatic in automaticProfiles.Where(profile => profile.IsTesting))
            {
                automatic.ApplyDelay(0);
            }
            _vpnSpeedtestService = null;
            IsVpnProfileTestBusy = false;
            ApplySnapshot(_controller.Snapshot);
            _vpnProfileTestLock.Release();
        }
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        return completion.Task;
    }

    private void AddConnectionLog(string message, string color)
    {
        RunOnUiThread(() => AddConnectionLogCore(message, color));
    }

    private void AddConnectionLogCore(string message, string color)
    {
        if (ThemeMode == GeksaThemeMode.Light)
        {
            if (string.Equals(color, "#3FD69A", StringComparison.OrdinalIgnoreCase))
            {
                color = "#059669";
            }
            else if (string.Equals(color, "#F0717E", StringComparison.OrdinalIgnoreCase))
            {
                color = "#DC2626";
            }
        }

        ConnectionLogEntries.Insert(0, new GeksaConnectionLogEntry(
            DateTime.Now.ToString("HH:mm:ss"),
            message,
            color));
        while (ConnectionLogEntries.Count > 60)
        {
            ConnectionLogEntries.RemoveAt(ConnectionLogEntries.Count - 1);
        }
        HasConnectionLogEntries = ConnectionLogEntries.Count > 0;
    }

    private static void RunOnUiThread(Action action)
    {
        RxSchedulers.MainThreadScheduler.Schedule(action);
    }

    private async Task EnterEcpPageAsync()
    {
        try
        {
            IsVirtualHereRunning = await _virtualHereService.IsRunningAsync(_monitorCancellation.Token);
            if (!IsVirtualHereRunning)
            {
                EcpDevices.Clear();
                HasEcpDevices = false;
                HasNoEcpDevices = true;
                EcpFailureMessage = null;
                EcpStatusText = TriskoLocalization.Text("VirtualHere выключен");
                return;
            }

            _ecpAutoScanTask ??= MonitorEcpAutoScanAsync(_monitorCancellation.Token);
            await ApplyConfiguredEcpServersAsync(_monitorCancellation.Token);
            await RefreshEcpDevicesAsync();
        }
        catch (OperationCanceledException) when (_monitorCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(EnterEcpPageAsync), ex);
            EcpFailureMessage = TriskoLocalization.Text("Не удалось проверить состояние VirtualHere.");
        }
    }

    private async Task StartVirtualHereAsync()
    {
        if (IsEcpBusy)
        {
            return;
        }

        IsEcpBusy = true;
        EcpFailureMessage = null;
        EcpStatusText = TriskoLocalization.Text("Запуск VirtualHere…");
        try
        {
            await ApplyConfiguredEcpServersAsync(_monitorCancellation.Token);
            var devices = await _virtualHereService.ScanAsync(_monitorCancellation.Token);
            IsVirtualHereRunning = true;
            UpdateEcpDevices(devices);
            _ecpAutoScanTask ??= MonitorEcpAutoScanAsync(_monitorCancellation.Token);
        }
        catch (OperationCanceledException) when (_monitorCancellation.IsCancellationRequested)
        {
        }
        catch (GeksaVirtualHereException ex)
        {
            IsVirtualHereRunning = await _virtualHereService.IsRunningAsync(CancellationToken.None);
            EcpFailureMessage = ex.UserMessage;
            EcpStatusText = IsVirtualHereRunning
                ? TriskoLocalization.Text("VirtualHere запущен, но устройства получить не удалось")
                : TriskoLocalization.Text("Не удалось запустить VirtualHere");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(StartVirtualHereAsync), ex);
            IsVirtualHereRunning = await _virtualHereService.IsRunningAsync(CancellationToken.None);
            EcpFailureMessage = TriskoLocalization.Text("Не удалось запустить VirtualHere.");
            EcpStatusText = TriskoLocalization.Text("Не удалось запустить VirtualHere");
        }
        finally
        {
            IsEcpBusy = false;
        }
    }

    private Task RefreshEcpDevicesAsync() =>
        RunEcpRefreshAsync(reRegisterServers: false, showProgress: true, waitForTurn: true);

    private Task RefreshEcpEnvironmentAsync() =>
        RunEcpRefreshAsync(reRegisterServers: true, showProgress: true, waitForTurn: true);

    private Task RefreshEcpDevicesInBackgroundAsync() =>
        RunEcpRefreshAsync(reRegisterServers: false, showProgress: false, waitForTurn: false);

    private async Task RunEcpRefreshAsync(
        bool reRegisterServers,
        bool showProgress,
        bool waitForTurn)
    {
        var cancellationToken = _monitorCancellation.Token;
        var lockTaken = waitForTurn
            ? await _ecpRefreshLock.WaitAsync(Timeout.Infinite, cancellationToken)
            : await _ecpRefreshLock.WaitAsync(0, cancellationToken);
        if (!lockTaken)
        {
            return;
        }

        if (showProgress)
        {
            IsEcpBusy = true;
        }
        EcpFailureMessage = null;
        if (showProgress)
        {
            EcpStatusText = reRegisterServers
                ? TriskoLocalization.Text("Переподключение серверов VirtualHere и поиск USB-устройств…")
                : TriskoLocalization.Text("Идёт поиск USB-устройств в локальной сети…");
        }

        try
        {
            if (reRegisterServers)
            {
                await ReRegisterConfiguredEcpServersAsync(cancellationToken);
            }

            var devices = await _virtualHereService.ScanAsync(cancellationToken);
            IsVirtualHereRunning = true;
            UpdateEcpDevices(devices);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (GeksaVirtualHereException ex)
        {
            EcpFailureMessage = ex.UserMessage;
            EcpStatusText = TriskoLocalization.Text("Не удалось получить список USB-устройств");
            HasEcpDevices = EcpDevices.Count > 0;
            HasNoEcpDevices = !HasEcpDevices;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RunEcpRefreshAsync), ex);
            EcpFailureMessage = TriskoLocalization.Text("Ошибка при обращении к клиенту VirtualHere.");
            EcpStatusText = TriskoLocalization.Text("Не удалось получить список USB-устройств");
        }
        finally
        {
            if (showProgress)
            {
                IsEcpBusy = false;
            }
            _ecpRefreshLock.Release();
        }
    }

    private void UpdateEcpDevices(IReadOnlyList<GeksaVirtualUsbDevice> devices)
    {
        EcpDevices.Clear();
        foreach (var device in devices)
        {
            EcpDevices.Add(new GeksaEcpDeviceViewModel(device, RunEcpDeviceActionAsync));
        }

        HasEcpDevices = EcpDevices.Count > 0;
        HasNoEcpDevices = !HasEcpDevices;
        var availableCount = devices.Count(device => device.State == GeksaVirtualUsbState.Available);
        var localCount = devices.Count(device => device.State == GeksaVirtualUsbState.InUseLocally);
        var remoteCount = devices.Count(device => device.State == GeksaVirtualUsbState.InUseRemotely);
        EcpStatusText = devices.Count == 0
            ? TriskoLocalization.Text("VirtualHere работает, но доступные USB-устройства не найдены")
            : TriskoLocalization.Format("Найдено: {0} · свободно: {1} · подключено здесь: {2} · занято: {3}", devices.Count, availableCount, localCount, remoteCount);
    }

    private async Task AddEcpServerAsync()
    {
        if (IsEcpServerSettingsBusy)
        {
            return;
        }
        if (!GeksaVirtualHereHubConfiguration.TryCreate(
                NewEcpServerHost,
                NewEcpServerPort,
                out var configuration,
                out var validationError))
        {
            EcpServerSettingsStatus = validationError;
            return;
        }
        if (EcpServers.Any(item => string.Equals(
                item.Endpoint,
                configuration.Endpoint,
                StringComparison.OrdinalIgnoreCase)))
        {
            EcpServerSettingsStatus = TriskoLocalization.Format("Сервер {0} уже добавлен.", configuration.Endpoint);
            return;
        }

        IsEcpServerSettingsBusy = true;
        try
        {
            await _virtualHereService.AddManualHubAsync(
                configuration.Endpoint,
                _monitorCancellation.Token);
            EcpServers.Add(new GeksaEcpServerItemViewModel(configuration, RemoveEcpServerAsync));
            HasEcpServers = true;
            HasNoEcpServers = false;
            await SaveEcpServerSettingsAsync();
            NewEcpServerHost = string.Empty;
            EcpServerSettingsStatus =
                TriskoLocalization.Format("Сервер {0} добавлен в VirtualHere. Выполняется сканирование…", configuration.Endpoint);
            this.RaisePropertyChanged(nameof(EcpServerSettingsSummaryText));
            await Task.Delay(500, _monitorCancellation.Token);
            await RefreshEcpDevicesAsync();
        }
        catch (GeksaVirtualHereException ex)
        {
            EcpServerSettingsStatus = ex.UserMessage;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            EcpServerSettingsStatus = TriskoLocalization.Text("Не удалось добавить сервер VirtualHere.");
        }
        finally
        {
            IsEcpServerSettingsBusy = false;
        }
    }

    private async Task RemoveEcpServerAsync(GeksaEcpServerItemViewModel item)
    {
        if (IsEcpServerSettingsBusy)
        {
            return;
        }

        IsEcpServerSettingsBusy = true;
        try
        {
            await _virtualHereService.RemoveManualHubAsync(
                item.Endpoint,
                _monitorCancellation.Token);
            EcpServers.Remove(item);
            HasEcpServers = EcpServers.Count > 0;
            HasNoEcpServers = !HasEcpServers;
            await SaveEcpServerSettingsAsync();
            EcpServerSettingsStatus = TriskoLocalization.Format("Сервер {0} удалён из VirtualHere.", item.Endpoint);
            this.RaisePropertyChanged(nameof(EcpServerSettingsSummaryText));
            await RefreshEcpDevicesAsync();
        }
        catch (GeksaVirtualHereException ex)
        {
            EcpServerSettingsStatus = ex.UserMessage;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            EcpServerSettingsStatus = TriskoLocalization.Text("Не удалось удалить сервер VirtualHere.");
        }
        finally
        {
            IsEcpServerSettingsBusy = false;
        }
    }

    private async Task SaveEcpAutoScanSettingsAsync()
    {
        if (_appConfig.UiItem is null)
        {
            return;
        }
        if (!int.TryParse(EcpAutoScanIntervalText.Trim(), out var intervalSeconds) ||
            intervalSeconds is < 1 or > 3600)
        {
            EcpAutoScanSettingsStatus = TriskoLocalization.Text("Интервал должен быть числом от 1 до 3600 секунд.");
            return;
        }

        var oldEnabled = _appConfig.UiItem.GeksaEcpAutoScanEnabled;
        var oldInterval = _appConfig.UiItem.GeksaEcpAutoScanIntervalSeconds;
        _appConfig.UiItem.GeksaEcpAutoScanEnabled = IsEcpAutoScanEnabled;
        _appConfig.UiItem.GeksaEcpAutoScanIntervalSeconds = intervalSeconds;
        if (await ConfigHandler.SaveConfig(_appConfig) != 0)
        {
            _appConfig.UiItem.GeksaEcpAutoScanEnabled = oldEnabled;
            _appConfig.UiItem.GeksaEcpAutoScanIntervalSeconds = oldInterval;
            IsEcpAutoScanEnabled = oldEnabled;
            EcpAutoScanIntervalSeconds = Math.Clamp(oldInterval, 1, 3600);
            EcpAutoScanIntervalText = EcpAutoScanIntervalSeconds.ToString();
            EcpAutoScanSettingsStatus = TriskoLocalization.Text("Не удалось сохранить параметры автосканирования ЭЦП.");
            return;
        }

        EcpAutoScanIntervalSeconds = intervalSeconds;
        EcpAutoScanIntervalText = intervalSeconds.ToString();
        EcpAutoScanSettingsStatus = IsEcpAutoScanEnabled
            ? TriskoLocalization.Format("Автосканирование включено: каждые {0} сек.", intervalSeconds)
            : TriskoLocalization.Text("Автосканирование ЭЦП выключено.");
        this.RaisePropertyChanged(nameof(EcpServerSettingsSummaryText));
        if (IsEcpAutoScanEnabled)
        {
            _ = RefreshEcpDevicesInBackgroundAsync();
        }
    }

    private async Task SaveAutoCheckUpdatesSettingsAsync()
    {
        if (_appConfig.UiItem is null)
        {
            return;
        }
        if (!int.TryParse(CheckUpdateIntervalHoursText.Trim(), out var intervalHours) ||
            intervalHours is < 1 or > 720)
        {
            AutoCheckUpdatesStatus = TriskoLocalization.Text("Интервал проверки должен быть целым числом от 1 до 720 часов.");
            return;
        }

        var oldEnabled = _appConfig.UiItem.GeksaAutoCheckUpdatesEnabled;
        var oldInterval = _appConfig.UiItem.GeksaCheckUpdateIntervalHours;
        _appConfig.UiItem.GeksaAutoCheckUpdatesEnabled = AutoCheckUpdatesEnabled;
        _appConfig.UiItem.GeksaCheckUpdateIntervalHours = intervalHours;
        if (await ConfigHandler.SaveConfig(_appConfig) != 0)
        {
            _appConfig.UiItem.GeksaAutoCheckUpdatesEnabled = oldEnabled;
            _appConfig.UiItem.GeksaCheckUpdateIntervalHours = oldInterval;
            AutoCheckUpdatesEnabled = oldEnabled;
            CheckUpdateIntervalHours = Math.Clamp(oldInterval, 1, 720);
            CheckUpdateIntervalHoursText = CheckUpdateIntervalHours.ToString();
            AutoCheckUpdatesStatus = TriskoLocalization.Text("Не удалось сохранить параметры автоматической проверки обновлений.");
            return;
        }

        CheckUpdateIntervalHours = intervalHours;
        CheckUpdateIntervalHoursText = intervalHours.ToString();
        AutoCheckUpdatesStatus = AutoCheckUpdatesEnabled
            ? TriskoLocalization.Format("Параметры сохранены: проверка каждые {0} ч.", intervalHours)
            : TriskoLocalization.Text("Параметры сохранены: автоматическая проверка выключена.");
        this.RaisePropertyChanged(nameof(UpdatesSummaryText));
    }

    private async Task SaveEcpServerSettingsAsync()
    {
        if (_appConfig.UiItem is null)
        {
            return;
        }

        _appConfig.UiItem.GeksaVirtualHereHubs = EcpServers
            .Select(item => new GeksaVirtualHereHubConfiguration
            {
                Host = item.Host,
                Port = item.Port
            })
            .ToList();
        await ConfigHandler.SaveConfig(_appConfig);
    }

    private async Task ApplyConfiguredEcpServersAsync(CancellationToken cancellationToken)
    {
        foreach (var endpoint in GetConfiguredEcpServerEndpoints())
        {
            try
            {
                await _virtualHereService.AddManualHubAsync(endpoint, cancellationToken);
            }
            catch (GeksaVirtualHereException ex)
            {
                Logging.SaveLog(nameof(ApplyConfiguredEcpServersAsync), ex);
                EcpServerSettingsStatus =
                    TriskoLocalization.Format("Не удалось применить сервер {0}: {1}", endpoint, ex.UserMessage);
            }
        }
    }

    private async Task ReRegisterConfiguredEcpServersAsync(CancellationToken cancellationToken)
    {
        var endpoints = GetConfiguredEcpServerEndpoints();
        if (endpoints.Count == 0)
        {
            return;
        }

        foreach (var endpoint in endpoints)
        {
            try
            {
                await _virtualHereService.RemoveManualHubAsync(endpoint, cancellationToken);
            }
            catch (GeksaVirtualHereException ex)
            {
                // Сервер мог отсутствовать в текущем экземпляре VirtualHere — всё равно пробуем добавить заново.
                Logging.SaveLog(nameof(ReRegisterConfiguredEcpServersAsync), ex);
            }
        }

        await Task.Delay(250, cancellationToken);
        var failedEndpoints = new List<string>();
        foreach (var endpoint in endpoints)
        {
            try
            {
                await _virtualHereService.AddManualHubAsync(endpoint, cancellationToken);
            }
            catch (GeksaVirtualHereException ex)
            {
                failedEndpoints.Add(endpoint);
                Logging.SaveLog(nameof(ReRegisterConfiguredEcpServersAsync), ex);
            }
        }

        if (failedEndpoints.Count > 0)
        {
            EcpFailureMessage =
                TriskoLocalization.Format("Не удалось повторно добавить серверы VirtualHere: {0}.", string.Join(", ", failedEndpoints));
            EcpServerSettingsStatus = EcpFailureMessage;
        }
        else
        {
            EcpServerSettingsStatus = endpoints.Count == 1
                ? TriskoLocalization.Format("Сервер {0} переподключён к VirtualHere.", endpoints[0])
                : TriskoLocalization.Format("Серверы VirtualHere переподключены: {0}.", endpoints.Count);
        }

        await Task.Delay(750, cancellationToken);
    }

    private IReadOnlyList<string> GetConfiguredEcpServerEndpoints() =>
        EcpServers
            .Select(item => item.Endpoint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task RefreshEcpAfterNetworkChangeAsync(bool reRegisterServers)
    {
        try
        {
            if (!IsVirtualHereRunning ||
                !await _virtualHereService.IsRunningAsync(_monitorCancellation.Token))
            {
                IsVirtualHereRunning = false;
                return;
            }

            await Task.Delay(reRegisterServers ? 750 : 250, _monitorCancellation.Token);
            await RunEcpRefreshAsync(
                reRegisterServers,
                showProgress: true,
                waitForTurn: true);
        }
        catch (OperationCanceledException) when (_monitorCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RefreshEcpAfterNetworkChangeAsync), ex);
        }
    }

    private async Task RunEcpDeviceActionAsync(
        GeksaEcpDeviceViewModel item,
        GeksaEcpDeviceAction action)
    {
        EcpFailureMessage = null;
        try
        {
            switch (action)
            {
                case GeksaEcpDeviceAction.Use:
                    await _virtualHereService.UseAsync(item.Address, _monitorCancellation.Token);
                    break;
                case GeksaEcpDeviceAction.StopUsing:
                    await _virtualHereService.StopUsingAsync(item.Address, _monitorCancellation.Token);
                    break;
                case GeksaEcpDeviceAction.ToggleAutoUseDevice:
                    await _virtualHereService.ToggleAutoUseDeviceAsync(item.Address, _monitorCancellation.Token);
                    break;
                case GeksaEcpDeviceAction.ToggleAutoUsePort:
                    await _virtualHereService.ToggleAutoUsePortAsync(item.Address, _monitorCancellation.Token);
                    break;
                case GeksaEcpDeviceAction.OpenVirtualHere:
                    _virtualHereService.OpenClientWindow();
                    return;
                case GeksaEcpDeviceAction.Refresh:
                    break;
            }

            await Task.Delay(350, _monitorCancellation.Token);
            await RefreshEcpDevicesAsync();
        }
        catch (GeksaVirtualHereException ex)
        {
            EcpFailureMessage = ex.UserMessage;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            EcpFailureMessage = TriskoLocalization.Text("Не удалось выполнить действие с USB-устройством.");
        }
    }

    private void OpenVirtualHere()
    {
        try
        {
            _virtualHereService.OpenClientWindow();
        }
        catch (GeksaVirtualHereException ex)
        {
            EcpFailureMessage = ex.UserMessage;
        }
    }

    private void RefreshNetworkInterfaceOptions()
    {
        var selectedName = _appConfig.TunModeItem.GeksaAutoSelectInterface
            ? null
            : _appConfig.TunModeItem.GeksaPreferredInterface;

        try
        {
            var options = _networkBindingSelector.GetAvailableOptions().ToList();
            if (!selectedName.IsNullOrEmpty()
                && options.All(option => !string.Equals(
                    option.InterfaceName,
                    selectedName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(new GeksaNetworkInterfaceOption(
                    selectedName,
                    TriskoLocalization.Format("{0}  ·  сейчас недоступен", selectedName),
                    IsAutomatic: false));
            }

            NetworkInterfaceOptions.Clear();
            foreach (var option in options)
            {
                NetworkInterfaceOptions.Add(option);
            }

            SelectedNetworkInterfaceOption = _appConfig.TunModeItem.GeksaAutoSelectInterface
                ? NetworkInterfaceOptions.FirstOrDefault(option => option.IsAutomatic)
                : NetworkInterfaceOptions.FirstOrDefault(option => string.Equals(
                    option.InterfaceName,
                    selectedName,
                    StringComparison.OrdinalIgnoreCase));
            NetworkInterfaceSettingStatus = _appConfig.TunModeItem.GeksaAutoSelectInterface
                ? TriskoLocalization.Text("Автоматический выбор активен")
                : TriskoLocalization.Format("Ручной выбор: {0}", selectedName);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            NetworkInterfaceSettingStatus = TriskoLocalization.Text("Не удалось обновить список сетевых адаптеров");
        }

        UpdateEffectiveNetworkInterfaceText();
    }

    private async Task SaveNetworkInterfaceAsync()
    {
        if (SelectedNetworkInterfaceOption is null)
        {
            NetworkInterfaceSettingStatus = TriskoLocalization.Text("Сначала выберите режим или сетевой адаптер");
            return;
        }

        _desiredConnected = false;
        CanEditNetworkInterface = false;
        var oldAutoSelect = _appConfig.TunModeItem.GeksaAutoSelectInterface;
        var oldPreferred = _appConfig.TunModeItem.GeksaPreferredInterface;
        try
        {
            _appConfig.TunModeItem.GeksaAutoSelectInterface = SelectedNetworkInterfaceOption.IsAutomatic;
            _appConfig.TunModeItem.GeksaPreferredInterface = SelectedNetworkInterfaceOption.IsAutomatic
                ? null
                : SelectedNetworkInterfaceOption.InterfaceName;

            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                _appConfig.TunModeItem.GeksaAutoSelectInterface = oldAutoSelect;
                _appConfig.TunModeItem.GeksaPreferredInterface = oldPreferred;
                NetworkInterfaceSettingStatus = TriskoLocalization.Text("Не удалось сохранить выбор адаптера");
                return;
            }

            NetworkInterfaceSettingStatus = SelectedNetworkInterfaceOption.IsAutomatic
                ? TriskoLocalization.Text("Сохранено: адаптер будет выбираться автоматически при каждом подключении")
                : TriskoLocalization.Format("Сохранено: использовать {0}", SelectedNetworkInterfaceOption.InterfaceName);
        }
        catch (Exception ex)
        {
            _appConfig.TunModeItem.GeksaAutoSelectInterface = oldAutoSelect;
            _appConfig.TunModeItem.GeksaPreferredInterface = oldPreferred;
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            NetworkInterfaceSettingStatus = TriskoLocalization.Text("Не удалось сохранить выбор адаптера");
        }
        finally
        {
            CanEditNetworkInterface = !_controller.Snapshot.IsCoreRunning
                                      && _controller.Snapshot.State is not GeksaVpnState.Connecting
                                          and not GeksaVpnState.Disconnecting;
        }
    }

    private void UpdateEffectiveNetworkInterfaceText()
    {
        var interfaceName = _appConfig.CoreBasicItem.BindInterface;
        var localAddress = _appConfig.CoreBasicItem.SendThrough;
        if (interfaceName.IsNullOrEmpty())
        {
            EffectiveNetworkInterfaceText = TriskoLocalization.Text("Фактический адаптер будет определён при подключении");
            return;
        }

        var mode = _appConfig.TunModeItem.GeksaAutoSelectInterface ? TriskoLocalization.Text("авто") : TriskoLocalization.Text("вручную");
        EffectiveNetworkInterfaceText = localAddress.IsNullOrEmpty()
            ? TriskoLocalization.Format("Выбранный адаптер: {0} · {1}", interfaceName, mode)
            : TriskoLocalization.Format("Выбранный адаптер: {0} · {1} · {2}", interfaceName, localAddress, mode);
    }

    private void RefreshVpnRoutingSettings()
    {
        if (VpnRoutingModeOptions.Count == 0)
        {
            foreach (var option in GeksaVpnRoutingModeOption.All)
            {
                VpnRoutingModeOptions.Add(option);
            }
        }

        SelectedVpnRoutingModeOption = VpnRoutingModeOptions.FirstOrDefault(option =>
            option.Mode == _appConfig.TunModeItem.GeksaRoutingMode)
            ?? VpnRoutingModeOptions.First();
        RefreshManualVpnRouteItems();
    }

    private void RefreshManualVpnRouteItems()
    {
        ManualVpnRoutes.Clear();
        foreach (var route in GeksaVpnRouteParser.NormalizeMany(
                     _appConfig.TunModeItem.GeksaManualVpnRoutes))
        {
            ManualVpnRoutes.Add(new GeksaVpnRouteItemViewModel(
                route,
                RemoveManualVpnRouteAsync));
        }
        HasNoManualVpnRoutes = ManualVpnRoutes.Count == 0;
    }

    private async Task SaveVpnRoutingModeAsync()
    {
        if (!CanEditVpnRouting || SelectedVpnRoutingModeOption is null)
        {
            VpnRoutingSettingStatus = TriskoLocalization.Text("Отключите VPN и выберите режим маршрутизации.");
            return;
        }

        var oldMode = _appConfig.TunModeItem.GeksaRoutingMode;
        CanEditVpnRouting = false;
        try
        {
            _appConfig.TunModeItem.GeksaRoutingMode = SelectedVpnRoutingModeOption.Mode;
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                _appConfig.TunModeItem.GeksaRoutingMode = oldMode;
                VpnRoutingSettingStatus = TriskoLocalization.Text("Не удалось сохранить режим маршрутизации.");
                return;
            }

            VpnRoutingSettingStatus =
                TriskoLocalization.Format("Сохранено: {0}. Применится при следующем подключении.", SelectedVpnRoutingModeOption.DisplayName);
        }
        catch (Exception ex)
        {
            _appConfig.TunModeItem.GeksaRoutingMode = oldMode;
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            VpnRoutingSettingStatus = TriskoLocalization.Text("Не удалось сохранить режим маршрутизации.");
        }
        finally
        {
            CanEditVpnRouting = !_controller.Snapshot.IsCoreRunning
                                && _controller.Snapshot.State is not GeksaVpnState.Connecting
                                    and not GeksaVpnState.Disconnecting;
        }
    }

    private async Task AddManualVpnRouteAsync()
    {
        if (!CanEditVpnRouting)
        {
            VpnRoutingSettingStatus = TriskoLocalization.Text("Отключите VPN перед изменением маршрутов.");
            return;
        }

        if (!GeksaVpnRouteParser.TryNormalize(
                NewManualVpnRoute,
                out var normalized,
                out var validationError))
        {
            VpnRoutingSettingStatus = validationError;
            return;
        }

        var oldRoutes = GeksaVpnRouteParser.NormalizeMany(
            _appConfig.TunModeItem.GeksaManualVpnRoutes);
        if (oldRoutes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            VpnRoutingSettingStatus = TriskoLocalization.Format("Маршрут {0} уже добавлен.", normalized);
            return;
        }
        if (oldRoutes.Count >= GeksaVpnRouteParser.MaxManualRoutes)
        {
            VpnRoutingSettingStatus =
                TriskoLocalization.Format("Нельзя добавить больше {0} собственных маршрутов.", GeksaVpnRouteParser.MaxManualRoutes);
            return;
        }

        CanEditVpnRouting = false;
        try
        {
            _appConfig.TunModeItem.GeksaManualVpnRoutes =
                GeksaVpnRouteParser.NormalizeMany(oldRoutes.Append(normalized));
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                _appConfig.TunModeItem.GeksaManualVpnRoutes = oldRoutes;
                VpnRoutingSettingStatus = TriskoLocalization.Text("Не удалось сохранить дополнительный маршрут.");
                return;
            }

            NewManualVpnRoute = string.Empty;
            RefreshManualVpnRouteItems();
            VpnRoutingSettingStatus =
                TriskoLocalization.Format("Маршрут {0} добавлен. Он применится при следующем подключении.", normalized);
        }
        catch (Exception ex)
        {
            _appConfig.TunModeItem.GeksaManualVpnRoutes = oldRoutes;
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            VpnRoutingSettingStatus = TriskoLocalization.Text("Не удалось сохранить дополнительный маршрут.");
        }
        finally
        {
            CanEditVpnRouting = !_controller.Snapshot.IsCoreRunning
                                && _controller.Snapshot.State is not GeksaVpnState.Connecting
                                    and not GeksaVpnState.Disconnecting;
        }
    }

    private async Task RemoveManualVpnRouteAsync(string route)
    {
        if (!CanEditVpnRouting)
        {
            VpnRoutingSettingStatus = TriskoLocalization.Text("Отключите VPN перед изменением маршрутов.");
            return;
        }

        var oldRoutes = GeksaVpnRouteParser.NormalizeMany(
            _appConfig.TunModeItem.GeksaManualVpnRoutes);
        var nextRoutes = oldRoutes
            .Where(item => !string.Equals(item, route, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (nextRoutes.Count == oldRoutes.Count)
        {
            return;
        }

        CanEditVpnRouting = false;
        try
        {
            _appConfig.TunModeItem.GeksaManualVpnRoutes = nextRoutes;
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                _appConfig.TunModeItem.GeksaManualVpnRoutes = oldRoutes;
                VpnRoutingSettingStatus = TriskoLocalization.Text("Не удалось удалить дополнительный маршрут.");
                return;
            }

            RefreshManualVpnRouteItems();
            VpnRoutingSettingStatus =
                TriskoLocalization.Format("Маршрут {0} удалён. Изменение применится при следующем подключении.", route);
        }
        catch (Exception ex)
        {
            _appConfig.TunModeItem.GeksaManualVpnRoutes = oldRoutes;
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            VpnRoutingSettingStatus = TriskoLocalization.Text("Не удалось удалить дополнительный маршрут.");
        }
        finally
        {
            CanEditVpnRouting = !_controller.Snapshot.IsCoreRunning
                                && _controller.Snapshot.State is not GeksaVpnState.Connecting
                                    and not GeksaVpnState.Disconnecting;
        }
    }

    private async Task ConfigureOneCSystemAsync()
    {
        if (IsOneCSystemBusy)
        {
            return;
        }

        GeksaOneCSystemTarget target;
        try
        {
            target = GeksaOneCSystemTargetValidator.Normalize(new(
                OneCServerName,
                OneCServerAddress));
        }
        catch (Exception ex)
        {
            SetOneCError(ex.Message);
            return;
        }

        IsOneCSystemBusy = true;
        try
        {
            var inspection = await _oneCSystemService.InspectAsync(target);
            ApplyOneCInspection(inspection);
            if (inspection.State == GeksaOneCSetupState.Error)
            {
                return;
            }

            if (inspection.HasConflicts)
            {
                var entries = string.Join(Environment.NewLine, inspection.ConflictingEntries);
                var confirmed = await ConfirmOneCSystemChangeInteraction.Handle(
                    TriskoLocalization.Text("В файле hosts найдены строки с выбранным именем сервера или IP:") +
                    Environment.NewLine + Environment.NewLine +
                    entries +
                    Environment.NewLine + Environment.NewLine +
                    TriskoLocalization.Text("Продолжить? Будут удалены только показанные строки, затем программа добавит свой отдельный блок. Остальные записи hosts не изменятся."));
                if (!confirmed)
                {
                    OneCSystemStatusTitle = TriskoLocalization.Text("Настройка отменена");
                    OneCSystemStatusMessage =
                        TriskoLocalization.Text("Конфликтующие записи не изменялись. Проверьте показанный ниже список.");
                    OneCSystemStatusColor = "#E9AE57";
                    return;
                }
            }

            var oldEnabled = _appConfig.TunModeItem.GeksaOneCEnabled;
            var oldName = _appConfig.TunModeItem.GeksaOneCServerName;
            var oldAddress = _appConfig.TunModeItem.GeksaOneCServerAddress;

            _appConfig.TunModeItem.GeksaOneCEnabled = true;
            _appConfig.TunModeItem.GeksaOneCServerName = target.ServerName;
            _appConfig.TunModeItem.GeksaOneCServerAddress = target.ServerAddress;
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                RestoreOneCAppSettings(oldEnabled, oldName, oldAddress);
                SetOneCError(TriskoLocalization.Text("Не удалось сохранить настройки маршрута 1С."));
                return;
            }

            var result = await _oneCSystemService.ConfigureAsync(
                target,
                replaceConflictingEntries: inspection.HasConflicts);
            if (result.State == GeksaOneCSetupState.Error)
            {
                RestoreOneCAppSettings(oldEnabled, oldName, oldAddress);
                _ = await ConfigHandler.SaveConfig(_appConfig);
                ApplyOneCInspection(result);
                return;
            }

            OneCServerName = target.ServerName;
            OneCServerAddress = target.ServerAddress;
            IsOneCConfigured = true;
            UpdateOneCRouteText();
            ApplyOneCInspection(result);
            OneCSystemStatusMessage = _controller.Snapshot.IsCoreRunning
                ? TriskoLocalization.Format("{0} Чтобы маршрут {1}/32 начал действовать, переподключите VPN.", result.Message, target.ServerAddress)
                : TriskoLocalization.Format("{0} Маршрут {1}/32 будет добавлен при следующем подключении.", result.Message, target.ServerAddress);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(ConfigureOneCSystemAsync), ex);
            SetOneCError(ex.Message);
        }
        finally
        {
            IsOneCSystemBusy = false;
        }
    }

    private async Task RefreshOneCSystemStatusAsync()
    {
        if (IsOneCSystemBusy)
        {
            return;
        }

        GeksaOneCSystemTarget target;
        try
        {
            target = GeksaOneCSystemTargetValidator.Normalize(new(
                OneCServerName,
                OneCServerAddress));
        }
        catch (Exception ex)
        {
            SetOneCError(ex.Message);
            return;
        }

        IsOneCSystemBusy = true;
        try
        {
            var inspection = await _oneCSystemService.InspectAsync(target);
            IsOneCConfigured = _appConfig.TunModeItem.GeksaOneCEnabled
                               || inspection.HasManagedBlock;
            UpdateOneCRouteText();
            ApplyOneCInspection(inspection);
            if (!OneCInputsMatchSavedSettings())
            {
                MarkOneCSettingsChanged();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(RefreshOneCSystemStatusAsync), ex);
            SetOneCError(ex.Message);
        }
        finally
        {
            IsOneCSystemBusy = false;
        }
    }

    private async Task RemoveOneCSystemAsync()
    {
        if (IsOneCSystemBusy)
        {
            return;
        }

        var confirmed = await ConfirmOneCSystemChangeInteraction.Handle(
            TriskoLocalization.Text("Удалить управляемую настройку 1С?") + Environment.NewLine + Environment.NewLine +
            TriskoLocalization.Text("Программа удалит только свой блок из hosts и отключит автоматический маршрут 1С. Остальные строки hosts и пользовательские маршруты останутся без изменений."));
        if (!confirmed)
        {
            return;
        }

        GeksaOneCSystemTarget target;
        try
        {
            target = GeksaOneCSystemTargetValidator.Normalize(new(
                _appConfig.TunModeItem.GeksaOneCServerName,
                _appConfig.TunModeItem.GeksaOneCServerAddress));
        }
        catch (Exception ex)
        {
            SetOneCError(ex.Message);
            return;
        }

        IsOneCSystemBusy = true;
        var oldEnabled = _appConfig.TunModeItem.GeksaOneCEnabled;
        try
        {
            _appConfig.TunModeItem.GeksaOneCEnabled = false;
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                _appConfig.TunModeItem.GeksaOneCEnabled = oldEnabled;
                SetOneCError(TriskoLocalization.Text("Не удалось отключить маршрут 1С."));
                return;
            }

            var inspection = await _oneCSystemService.RemoveManagedConfigurationAsync(target);
            if (inspection.State == GeksaOneCSetupState.Error)
            {
                _appConfig.TunModeItem.GeksaOneCEnabled = oldEnabled;
                _ = await ConfigHandler.SaveConfig(_appConfig);
                ApplyOneCInspection(inspection);
                return;
            }

            IsOneCConfigured = false;
            UpdateOneCRouteText();
            ApplyOneCInspection(inspection);
            OneCSystemStatusTitle = TriskoLocalization.Text("Не настроено");
            OneCSystemStatusMessage = _controller.Snapshot.IsCoreRunning
                ? TriskoLocalization.Text("Настройка 1С удалена. Переподключите VPN, чтобы убрать маршрут из текущего TUN.")
                : TriskoLocalization.Text("Управляемый блок удалён из hosts, автоматический маршрут 1С отключён.");
            OneCSystemStatusColor = "#8FA6C6";
        }
        catch (Exception ex)
        {
            _appConfig.TunModeItem.GeksaOneCEnabled = oldEnabled;
            Logging.SaveLog(nameof(RemoveOneCSystemAsync), ex);
            SetOneCError(ex.Message);
        }
        finally
        {
            IsOneCSystemBusy = false;
        }
    }

    private void ApplyOneCInspection(GeksaOneCSystemInspection inspection)
    {
        OneCSystemStatusTitle = inspection.State switch
        {
            GeksaOneCSetupState.Ready when _appConfig.TunModeItem.GeksaOneCEnabled => TriskoLocalization.Text("Готово"),
            GeksaOneCSetupState.Ready => TriskoLocalization.Text("Требуется внимание"),
            GeksaOneCSetupState.Warning => TriskoLocalization.Text("Требуется внимание"),
            GeksaOneCSetupState.Error => TriskoLocalization.Text("Ошибка"),
            _ => TriskoLocalization.Text("Не настроено")
        };
        OneCSystemStatusColor = inspection.State switch
        {
            GeksaOneCSetupState.Ready when _appConfig.TunModeItem.GeksaOneCEnabled => "#3FD69A",
            GeksaOneCSetupState.Warning or GeksaOneCSetupState.Ready => "#E9AE57",
            GeksaOneCSetupState.Error => "#F0717E",
            _ => "#8FA6C6"
        };
        OneCSystemStatusMessage = inspection.State == GeksaOneCSetupState.Ready
                                  && !_appConfig.TunModeItem.GeksaOneCEnabled
            ? TriskoLocalization.Text("В hosts найден блок Trisko, но автоматический маршрут 1С выключен. Выполните настройку заново.")
            : inspection.Message;
        OneCHostsEntriesText = string.Join(Environment.NewLine, inspection.RelatedEntries);
        HasOneCHostsEntries = inspection.RelatedEntries.Count > 0;
    }

    private void MarkOneCSettingsChanged()
    {
        if (OneCInputsMatchSavedSettings())
        {
            return;
        }

        OneCSystemStatusTitle = TriskoLocalization.Text("Изменения не применены");
        OneCSystemStatusMessage =
            TriskoLocalization.Text("Проверьте имя и IP, затем нажмите «Настроить систему и программу».");
        OneCSystemStatusColor = "#E9AE57";
    }

    private bool OneCInputsMatchSavedSettings() =>
        string.Equals(
            OneCServerName?.Trim().TrimEnd('.'),
            _appConfig.TunModeItem.GeksaOneCServerName,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            OneCServerAddress?.Trim(),
            _appConfig.TunModeItem.GeksaOneCServerAddress,
            StringComparison.OrdinalIgnoreCase);

    private void UpdateOneCRouteText()
    {
        OneCRouteText = _appConfig.TunModeItem.GeksaOneCEnabled
            ? TriskoLocalization.Format("Автоматический маршрут: {0}/32 · без дублей", _appConfig.TunModeItem.GeksaOneCServerAddress)
            : TriskoLocalization.Text("Автоматический маршрут 1С выключен");
    }

    private void RestoreOneCAppSettings(bool enabled, string name, string address)
    {
        _appConfig.TunModeItem.GeksaOneCEnabled = enabled;
        _appConfig.TunModeItem.GeksaOneCServerName = name;
        _appConfig.TunModeItem.GeksaOneCServerAddress = address;
        IsOneCConfigured = enabled;
        UpdateOneCRouteText();
    }

    private void SetOneCError(string message)
    {
        OneCSystemStatusTitle = TriskoLocalization.Text("Ошибка");
        OneCSystemStatusMessage = message;
        OneCSystemStatusColor = "#F0717E";
    }

    private void ApplyStartupDiagnostics(GeksaStartupDiagnosticsReport report)
    {
        if (report.CheckedAtUtc == DateTimeOffset.MinValue)
        {
            return;
        }

        StartupDiagnosticsTitle = report.DisplayTitle;
        StartupDiagnosticsMessage = report.DisplayMessage;
        StartupDiagnosticsColor = report.StatusColor;
    }

    private async Task TestLatencyAsync()
    {
        var profile = EffectiveVpnProfile ?? SelectedVpnProfile;
        if (profile is null)
        {
            FailureMessage = TriskoLocalization.Text("Сначала выберите VPN-профиль.");
            return;
        }

        await TestVpnProfileLatencyAsync(profile);
    }

    private async Task TestVpnProfileLatencyAsync(GeksaVpnProfileOption profile)
    {
        if (profile is null || IsBusy)
        {
            return;
        }
        IsLatencyTestBusy = true;
        _localFailureMessage = null;
        LatencyStatusText = profile.IsAutomatic
            ? TriskoLocalization.Text("Проверяю задержку всех подключений…")
            : TriskoLocalization.Format("Проверяю задержку через «{0}»…", profile.DisplayName);
        LatencyStatusColor = "#6F9CFF";
        ApplySnapshot(_controller.Snapshot);
        try
        {
            var results = await RunVpnProfileTestsAsync(profile.IsAutomatic ? null : profile.IndexId);
            var result = profile.IsAutomatic
                ? GeksaVpnProfileOption.CandidatesForAutomaticProfile(profile, results).FirstOrDefault()
                : results.FirstOrDefault();
            if (profile.IsAutomatic)
            {
                (FindVpnProfile(profile.IndexId) ?? profile)
                    .ApplyDelay(result?.DelayMilliseconds ?? 0);
            }
            LatencyStatusText = result is null
                ? TriskoLocalization.Text("Нет ответа на тестовый запрос.")
                : TriskoLocalization.Format("{0}: {1} мс", result.Profile.DisplayName, result.DelayMilliseconds);
            var isLight = ThemeMode == GeksaThemeMode.Light;
            LatencyStatusColor = result is null
                ? (isLight ? "#DC2626" : "#F0717E")
                : (isLight ? "#059669" : "#3FD69A");
        }
        finally
        {
            IsLatencyTestBusy = false;
            ApplySnapshot(_controller.Snapshot);
        }
    }

    private async Task AddResourceAsync(GeksaResourceType resourceType)
    {
        await SaveEditedResourceAsync(new GeksaResourceEditRequest(resourceType));
    }

    private async Task EditResourceAsync(GeksaResourceProfile resource)
    {
        var resourceType = GeksaCorporateConfigurationCodec.ParseResourceType(resource.Type);
        var canDisconnectSmb = false;
        if (resourceType == GeksaResourceType.Smb)
        {
            try
            {
                var status = await _smbDriveService.GetStatusAsync(resource);
                canDisconnectSmb = status.State == GeksaSmbDriveState.Connected;
                FindResourceItem(resource)?.ApplySmbStatus(status);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            }
        }

        await SaveEditedResourceAsync(new GeksaResourceEditRequest(
            resourceType,
            resource,
            canDisconnectSmb));
    }

    private async Task SaveEditedResourceAsync(GeksaResourceEditRequest request)
    {
        _localFailureMessage = null;
        FailureMessage = null;
        try
        {
            var editResult = await EditResourceInteraction.Handle(request);
            if (editResult is null)
            {
                return;
            }

            if (editResult.DisconnectSmbRequested)
            {
                if (request.Resource is null || request.ResourceType != GeksaResourceType.Smb)
                {
                    throw new GeksaConfigurationException(
                        TriskoLocalization.Text("Отключение доступно только для подключённого SMB-диска."));
                }

                await DisconnectSmbResourceAsync(request.Resource);
                return;
            }

            if (editResult.DeleteRequested)
            {
                if (request.Resource is not null)
                {
                    await DeleteResourceAsync(request.Resource);
                }
                return;
            }

            var edited = editResult.Resource;
            if (edited is null)
            {
                return;
            }

            if (request.Resource is not null && SmbMappingChanged(request.Resource, edited))
            {
                var existingStatus = await _smbDriveService.GetStatusAsync(request.Resource);
                if (existingStatus.State == GeksaSmbDriveState.Connected)
                {
                    await _smbDriveService.DisconnectAsync(request.Resource);
                }
            }

            await _userResourceService.UpsertAsync(edited);
            await LoadConfigurationAsync(CancellationToken.None);
            await RefreshSmbStatusesSafelyAsync();
            LocalConfigurationStatus = request.Resource is null
                ? TriskoLocalization.Format("Добавлено подключение «{0}»", edited.DisplayName)
                : TriskoLocalization.Format("Изменения подключения «{0}» сохранены", edited.DisplayName);
        }
        catch (GeksaConfigurationException ex)
        {
            ReportOperationFailure(ex.UserMessage, ex);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            _localFailureMessage = TriskoLocalization.Text("Не удалось сохранить подключение.");
            FailureMessage = _localFailureMessage;
        }
    }

    private async Task AddManualVpnAsync()
    {
        _localFailureMessage = null;
        FailureMessage = null;
        if (_controller.Snapshot.IsCoreRunning)
        {
            _localFailureMessage = TriskoLocalization.Text("Перед добавлением VPN-конфигурации отключите текущее подключение.");
            FailureMessage = _localFailureMessage;
            return;
        }

        try
        {
            var input = await AddManualVpnInteraction.Handle(RxVoid.Default);
            if (input is null)
            {
                return;
            }

            var profile = FmtHandler.ResolveConfig(input.ShareUri, out _)
                ?? throw new GeksaConfigurationException(TriskoLocalization.Text("v2rayN не распознал VPN-ссылку."));
            profile.IndexId = string.Empty;
            profile.Subid = string.Empty;
            profile.IsSub = false;
            profile.Remarks = input.DisplayName;
            if (await ConfigHandler.AddServer(_appConfig, profile) != 0 ||
                await ConfigHandler.SetDefaultServerIndex(_appConfig, profile.IndexId) != 0)
            {
                throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось сохранить VPN-профиль."));
            }

            _appConfig.UiItem.GeksaAutoSelectVpnProfile = false;
            _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile = false;
            _appConfig.TunModeItem.EnableTun = input.EnableTun;
            if (await ConfigHandler.SaveConfig(_appConfig) != 0)
            {
                throw new GeksaConfigurationException(TriskoLocalization.Text("VPN-профиль добавлен, но его настройки не сохранились."));
            }

            LocalConfigurationStatus = TriskoLocalization.Format("Добавлен и выбран VPN-профиль «{0}»", input.DisplayName);
            await RefreshVpnProfilesAsync(CancellationToken.None);
            await _controller.RefreshAsync();
        }
        catch (GeksaConfigurationException ex)
        {
            ReportOperationFailure(ex.UserMessage, ex);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            _localFailureMessage = TriskoLocalization.Text("Не удалось добавить VPN-конфигурацию.");
            FailureMessage = _localFailureMessage;
        }
    }

    public async Task LaunchResourceAsync(GeksaResourceProfile resource)
    {
        _localFailureMessage = null;
        FailureMessage = null;
        var isSmb = GeksaCorporateConfigurationCodec.ParseResourceType(resource.Type) == GeksaResourceType.Smb;
        try
        {
            if (isSmb)
            {
                if (resource.RequiresVpn && _controller.Snapshot.State != GeksaVpnState.Connected)
                {
                    throw new GeksaConfigurationException(TriskoLocalization.Text("Сначала подключитесь к корпоративному VPN."));
                }

                var status = await _smbDriveService.GetStatusAsync(resource);
                if (status.State == GeksaSmbDriveState.Connected)
                {
                    await _smbDriveService.OpenAsync(resource);
                }
                else
                {
                    status = await _smbDriveService.ConnectAsync(resource);
                }
                FindResourceItem(resource)?.ApplySmbStatus(status);
            }
            else
            {
                await _resourceLauncher.LaunchAsync(resource, _controller.Snapshot);
            }
            Notifications.Show(TriskoLocalization.Text("Ресурс открыт"), TriskoLocalization.Format("«{0}»: действие выполнено.", resource.DisplayName), GeksaNotificationKind.Success);
        }
        catch (GeksaConfigurationException ex)
        {
            ReportOperationFailure(ex.UserMessage, ex);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            ReportOperationFailure(TriskoLocalization.Text("Не удалось открыть корпоративный ресурс."), ex);
        }
        finally
        {
            if (isSmb)
            {
                try
                {
                    await RefreshSmbStatusAsync(resource, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(GeksaShellViewModel), ex);
                }
            }
        }
    }

    private async Task DisconnectSmbResourceAsync(GeksaResourceProfile resource)
    {
        _localFailureMessage = null;
        FailureMessage = null;
        try
        {
            await _smbDriveService.DisconnectAsync(resource);
            await RefreshSmbStatusAsync(resource, CancellationToken.None);
            Notifications.Show(TriskoLocalization.Text("Сетевой диск"), TriskoLocalization.Format("«{0}» отключён.", resource.DisplayName), GeksaNotificationKind.Success);
        }
        catch (GeksaConfigurationException ex)
        {
            ReportOperationFailure(ex.UserMessage, ex);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            ReportOperationFailure(TriskoLocalization.Text("Не удалось отключить сетевой диск."), ex);
        }
    }

    private async Task DeleteResourceAsync(GeksaResourceProfile resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _localFailureMessage = null;
        FailureMessage = null;

        try
        {
            if (GeksaCorporateConfigurationCodec.ParseResourceType(resource.Type) == GeksaResourceType.Smb)
            {
                try
                {
                    await _smbDriveService.DisconnectAsync(resource);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(GeksaShellViewModel), ex);
                }
            }

            var active = await _configurationService.LoadAsync(CancellationToken.None);
            var isManaged = active?.Resources.Any(item => string.Equals(
                item.Id,
                resource.Id,
                StringComparison.OrdinalIgnoreCase)) == true;
            if (isManaged)
            {
                await _userResourceService.HideManagedAsync(resource.Id, CancellationToken.None);
            }
            else
            {
                await _userResourceService.DeleteAsync(resource.Id, CancellationToken.None);
            }

            await LoadConfigurationAsync(CancellationToken.None);
            await RefreshSmbStatusesSafelyAsync();
            await _controller.RefreshAsync();
            LocalConfigurationStatus = TriskoLocalization.Format("Подключение «{0}» удалено", resource.DisplayName);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            _localFailureMessage = TriskoLocalization.Text("Не удалось удалить подключение.");
            FailureMessage = _localFailureMessage;
        }
    }

    private async Task RefreshSmbStatusesAsync(CancellationToken cancellationToken)
    {
        foreach (var item in _allFileStorageResources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await _smbDriveService.GetStatusAsync(item.Resource, cancellationToken);
            item.ApplySmbStatus(status);
        }
    }

    private async Task RefreshSmbStatusesSafelyAsync()
    {
        try
        {
            await RefreshSmbStatusesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
            _localFailureMessage = ex is GeksaConfigurationException configurationException
                ? configurationException.UserMessage
                : TriskoLocalization.Text("Не удалось проверить подключённые сетевые диски.");
            FailureMessage = _localFailureMessage;
        }
    }

    private async Task AutoLaunchAllConfiguredResourcesAsync()
    {
        try
        {
            await Task.Delay(1500);
            if (!_controller.Snapshot.IsCoreRunning || _controller.Snapshot.State != GeksaVpnState.Connected)
            {
                return;
            }

            var autoLaunchItems = Resources
                .Where(x => x.AutoLaunchOnVpn)
                .ToList();

            foreach (var item in autoLaunchItems)
            {
                if (!_controller.Snapshot.IsCoreRunning || _controller.Snapshot.State != GeksaVpnState.Connected)
                {
                    break;
                }

                await LaunchResourceAsync(item.Resource);
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(AutoLaunchAllConfiguredResourcesAsync), ex);
        }
    }

    private async Task AutoConnectAllSmbDrivesAsync()
    {
        try
        {
            foreach (var item in _allFileStorageResources)
            {
                var status = await _smbDriveService.GetStatusAsync(item.Resource, CancellationToken.None);
                if (status.State != GeksaSmbDriveState.Connected)
                {
                    await _smbDriveService.ConnectAsync(item.Resource, CancellationToken.None);
                }
                var updated = await _smbDriveService.GetStatusAsync(item.Resource, CancellationToken.None);
                item.ApplySmbStatus(updated);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
        }
    }

    private async Task AutoDisconnectAllSmbDrivesAsync()
    {
        try
        {
            foreach (var item in _allFileStorageResources)
            {
                var status = await _smbDriveService.GetStatusAsync(item.Resource, CancellationToken.None);
                if (status.State == GeksaSmbDriveState.Connected)
                {
                    await _smbDriveService.DisconnectAsync(item.Resource);
                }
                var updated = await _smbDriveService.GetStatusAsync(item.Resource, CancellationToken.None);
                item.ApplySmbStatus(updated);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
        }
    }

    private async Task RefreshSmbStatusAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken)
    {
        var status = await _smbDriveService.GetStatusAsync(resource, cancellationToken);
        FindResourceItem(resource)?.ApplySmbStatus(status);
    }

    private GeksaResourceItemViewModel? FindResourceItem(GeksaResourceProfile resource) =>
        Resources.FirstOrDefault(item => string.Equals(
            item.Resource.Id,
            resource.Id,
            StringComparison.OrdinalIgnoreCase));

    private static bool SmbMappingChanged(
        GeksaResourceProfile previous,
        GeksaResourceProfile current)
    {
        if (GeksaCorporateConfigurationCodec.ParseResourceType(previous.Type) != GeksaResourceType.Smb ||
            GeksaCorporateConfigurationCodec.ParseResourceType(current.Type) != GeksaResourceType.Smb)
        {
            return false;
        }

        return !string.Equals(previous.Host, current.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(previous.Path.Trim('\\', '/'), current.Path.Trim('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                (previous.DriveLetter ?? string.Empty).Trim(),
                (current.DriveLetter ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task MonitorEcpAutoScanAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var previousEnabled = IsEcpAutoScanEnabled;
        var previousInterval = EcpAutoScanIntervalSeconds;
        var nextScanAtUtc = DateTimeOffset.UtcNow.AddSeconds(previousInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var currentInterval = Math.Clamp(EcpAutoScanIntervalSeconds, 1, 3600);
                if (previousEnabled != IsEcpAutoScanEnabled || previousInterval != currentInterval)
                {
                    previousEnabled = IsEcpAutoScanEnabled;
                    previousInterval = currentInterval;
                    nextScanAtUtc = DateTimeOffset.UtcNow.AddSeconds(currentInterval);
                }

                if (!IsVirtualHereRunning ||
                    !IsEcpAutoScanEnabled ||
                    DateTimeOffset.UtcNow < nextScanAtUtc)
                {
                    continue;
                }

                nextScanAtUtc = DateTimeOffset.UtcNow.AddSeconds(currentInterval);
                if (!await _virtualHereService.IsRunningAsync(cancellationToken))
                {
                    IsVirtualHereRunning = false;
                    EcpStatusText = TriskoLocalization.Text("VirtualHere выключен");
                    continue;
                }
                await RefreshEcpDevicesInBackgroundAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(MonitorEcpAutoScanAsync), ex);
        }
    }

    private async Task MonitorVpnAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try { await CheckVpnRecoveryOnUiThreadAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(MonitorVpnAsync), ex);
                    Notifications.Show(TriskoLocalization.Text("Проверка VPN"), TriskoLocalization.Text("Не удалось проверить состояние подключения."), GeksaNotificationKind.Error, ex.ToString());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaShellViewModel), ex);
        }
    }

    private void ApplyAutomaticProfileDelays(IEnumerable<GeksaVpnTestCandidate> candidates)
    {
        var ranked = candidates.ToList();
        foreach (var automatic in VpnProfiles.Where(profile => profile.IsAutomatic))
        {
            var fastest = GeksaVpnProfileOption
                .CandidatesForAutomaticProfile(automatic, ranked)
                .FirstOrDefault();
            automatic.ApplyDelay(fastest?.DelayMilliseconds ?? 0);
        }
    }

    private async Task MonitorConnectionDurationAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                RxSchedulers.MainThreadScheduler.Schedule(UpdateConnectionDuration);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(MonitorConnectionDurationAsync), ex);
        }
    }

    private void UpdateConnectionDuration()
    {
        if (!IsConnected || _connectionStartedAtUtc is not { } connectedAtUtc)
        {
            ConnectionDurationText = "00:00:00";
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - connectedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        ConnectionDurationText =
            $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void ApplySnapshot(GeksaVpnSnapshot snapshot)
    {
        if (snapshot.UpdatedAtUtc < _controller.Snapshot.UpdatedAtUtc)
        {
            return;
        }
        IsBusy = snapshot.State is GeksaVpnState.Connecting or GeksaVpnState.Disconnecting
            || _isVpnConnectionBusy
            || _isVpnSelectionBusy
            || IsVpnProfileTestBusy
            || IsVpnSubscriptionBusy
            || IsLatencyTestBusy;
        IsConnected = snapshot.State == GeksaVpnState.Connected;
        IsConnecting = snapshot.State == GeksaVpnState.Connecting ||
            (_isVpnConnectionBusy && snapshot.State != GeksaVpnState.Connected);
        IsConnectionIdle = snapshot.State == GeksaVpnState.Disconnected && !_isVpnConnectionBusy;
        var isConnectedNow = IsConnected;
        if (isConnectedNow && !_previousVpnConnected)
        {
            _connectionStartedAtUtc = DateTimeOffset.UtcNow;
            UpdateConnectionDuration();
            if (IsVirtualHereRunning)
            {
                _ = RefreshEcpAfterNetworkChangeAsync(reRegisterServers: true);
            }
            if (AutoConnectSmbOnVpn)
            {
                _ = AutoConnectAllSmbDrivesAsync();
            }
            if (AutoLaunchResourcesOnVpn)
            {
                _ = AutoLaunchAllConfiguredResourcesAsync();
            }
        }
        else if (!isConnectedNow && _previousVpnConnected)
        {
            _connectionStartedAtUtc = null;
            UpdateConnectionDuration();
            _scanEcpWhenVpnStops = IsVirtualHereRunning;
            if (AutoDisconnectSmbOnVpn)
            {
                _ = AutoDisconnectAllSmbDrivesAsync();
            }
        }
        _previousVpnConnected = isConnectedNow;
        if (_scanEcpWhenVpnStops && snapshot.State is GeksaVpnState.Disconnected or GeksaVpnState.Failed)
        {
            _scanEcpWhenVpnStops = false;
            _ = RefreshEcpAfterNetworkChangeAsync(reRegisterServers: false);
        }
        CanEditNetworkInterface = !IsBusy && !snapshot.IsCoreRunning;
        CanEditVpnRouting = !IsBusy && !snapshot.IsCoreRunning;
        CanSelectVpnProfile = !IsBusy && !snapshot.IsCoreRunning && HasVpnProfiles;
        SynchronizeSelectedVpnProfile(_appConfig.IndexId);
        UpdateVpnProfileSelectionHint();
        UpdateEffectiveNetworkInterfaceText();
        var hasProfile = _appConfig.UiItem.GeksaAutoSelectVpnProfile
            ? VpnProfiles.Any(profile =>
                !profile.IsAutomatic &&
                profile.IsAnyDesk == _appConfig.UiItem.GeksaAutoSelectAnyDeskVpnProfile)
            : FindVpnProfile(_appConfig.IndexId) is not null;
        CanTestLatency = SelectedVpnProfile is not null &&
            !IsBusy;
        CanConnect = !IsBusy && !IsConnected && hasProfile;
        CanDisconnect = !IsBusy && snapshot.IsCoreRunning;
        CanRunPrimaryAction = CanConnect || CanDisconnect;
        HasConnectionError = snapshot.State == GeksaVpnState.Failed;
        ProfileName = snapshot.ProfileName.IsNullOrEmpty()
            ? TriskoLocalization.Text("VPN-профиль не выбран")
            : snapshot.ProfileName;
        if (snapshot.Failure is not null && !_isVpnConnectionBusy)
        {
            _localFailureMessage = snapshot.Failure.Message;
        }
        FailureMessage = _localFailureMessage;

        foreach (var resource in Resources)
        {
            resource.UpdateAvailability(IsConnected);
        }

        (StateTitle, StateDescription) = snapshot.State switch
        {
            GeksaVpnState.Connecting => (
                TriskoLocalization.Text("Подключение…"),
                TriskoLocalization.Text("v2rayN подготавливает TUN и запускает VPN-ядро.")),
            GeksaVpnState.Connected => (
                TriskoLocalization.Text("VPN подключён"),
                TriskoLocalization.Text("Корпоративное сетевое подключение готово к работе.")),
            GeksaVpnState.Disconnecting => (
                TriskoLocalization.Text("Отключение…"),
                TriskoLocalization.Text("VPN-ядро и системный прокси корректно останавливаются.")),
            GeksaVpnState.Failed => (
                TriskoLocalization.Text("Не удалось выполнить операцию"),
                snapshot.Failure?.Message ?? TriskoLocalization.Text("Проверьте расширенные настройки VPN.")),
            _ => (
                TriskoLocalization.Text("VPN отключён"),
                !hasProfile
                    ? TriskoLocalization.Text("Добавьте VPN-ссылку или подписку в настройках.")
                    : snapshot.IsCoreRunning && !snapshot.IsTunEnabled
                    ? TriskoLocalization.Text("Прокси-ядро работает, но TUN выключен. Для корпоративной сети требуется TUN.")
                    : TriskoLocalization.Text("Нажмите «Подключиться», чтобы открыть корпоративные ресурсы."))
        };

        var isLight = ThemeMode == GeksaThemeMode.Light;
        var greenColor = isLight ? "#059669" : "#3FD69A";
        var redColor = isLight ? "#DC2626" : "#F0717E";
        var disconnectedColor = isLight ? "#64748B" : "#75849A";

        (ConnectionButtonText, ConnectionStatusBadge, StatusColor) = snapshot.State switch
        {
            GeksaVpnState.Connecting => (TriskoLocalization.Text("Подключение…"), TriskoLocalization.Text("ПОДКЛЮЧЕНИЕ"), "#72E5BD"),
            GeksaVpnState.Connected => (TriskoLocalization.Text("Отключить"), TriskoLocalization.Text("ПОДКЛЮЧЕНО"), greenColor),
            GeksaVpnState.Disconnecting => (TriskoLocalization.Text("Отключение…"), TriskoLocalization.Text("ОТКЛЮЧЕНИЕ"), disconnectedColor),
            GeksaVpnState.Failed => (TriskoLocalization.Text("Повторить"), TriskoLocalization.Text("ОШИБКА"), redColor),
            _ when snapshot.IsCoreRunning => (TriskoLocalization.Text("Остановить"), TriskoLocalization.Text("TUN ВЫКЛЮЧЕН"), "#F3B35A"),
            _ => (TriskoLocalization.Text("Подключиться"), TriskoLocalization.Text("ОТКЛЮЧЕНО"), disconnectedColor)
        };

        if (_isVpnConnectionBusy && snapshot.State != GeksaVpnState.Connected)
        {
            StateTitle = TriskoLocalization.Text("Подключение…");
            StateDescription = IsVpnProfileTestBusy
                ? TriskoLocalization.Text("Проверяю задержку серверов для автоматического выбора.")
                : TriskoLocalization.Text("Проверяю запуск выбранного подключения.");
            ConnectionButtonText = TriskoLocalization.Text("Подключение…");
            ConnectionStatusBadge = TriskoLocalization.Text("ПОДКЛЮЧЕНИЕ");
            StatusColor = "#72E5BD";
            HasConnectionError = false;
        }

        UpdateReconnectCountdown();
        if (_lastLoggedVpnState != snapshot.State)
        {
            if (_lastLoggedVpnState is not null && snapshot.State == GeksaVpnState.Connected)
                Notifications.Show(TriskoLocalization.Text("VPN подключён"), TriskoLocalization.Text("Защищённое подключение установлено."), GeksaNotificationKind.Success);
            else if (_lastLoggedVpnState == GeksaVpnState.Disconnecting && snapshot.State == GeksaVpnState.Disconnected)
                Notifications.Show(TriskoLocalization.Text("VPN отключён"), TriskoLocalization.Text("Подключение остановлено."), GeksaNotificationKind.Information);
            _lastLoggedVpnState = snapshot.State;
            switch (snapshot.State)
            {
                case GeksaVpnState.Connecting:
                    AddConnectionLogCore(
                        TriskoLocalization.Text("VPN-ядро запускается. Ожидаю локальный SOCKS-порт и включение TUN."),
                        "#72E5BD");
                    break;
                case GeksaVpnState.Connected:
                    AddConnectionLogCore(
                        TriskoLocalization.Text("Локальный SOCKS-порт доступен, защищённое подключение установлено."),
                        "#3FD69A");
                    break;
                case GeksaVpnState.Disconnecting:
                    AddConnectionLogCore(TriskoLocalization.Text("Останавливаю VPN-подключение."), "#F3B35A");
                    break;
                case GeksaVpnState.Failed:
                    AddConnectionLogCore(
                        snapshot.Failure?.Message ?? TriskoLocalization.Text("VPN-ядро не удалось запустить."),
                        "#F0717E");
                    break;
                case GeksaVpnState.Disconnected when _desiredConnected:
                    AddConnectionLogCore(
                        TriskoLocalization.Text("VPN временно отключён. Выполняется автоматическое восстановление."),
                        "#F3B35A");
                    break;
                case GeksaVpnState.Disconnected:
                    AddConnectionLogCore(TriskoLocalization.Text("VPN отключён."), "#8FA6C6");
                    break;
            }
        }
    }

    private enum GeksaShellPage
    {
        Connection,
        RemoteDesktops,
        FileStorage,
        SshAccess,
        Ecp,
        Settings
    }
}

public enum GeksaSettingsSubPage
{
    Main,
    OneCSystem,
    VpnRouting,
    SmbAutomation,
    NetworkInterface,
    EcpServers,
    Updates,
    AnyDesk,
}
