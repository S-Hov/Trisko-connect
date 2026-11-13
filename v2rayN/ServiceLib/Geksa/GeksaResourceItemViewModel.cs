namespace ServiceLib.Geksa;

public partial class GeksaResourceItemViewModel : MyReactiveObject
{
    private readonly Func<GeksaResourceProfile, Task> _open;
    private readonly Func<GeksaResourceProfile, Task> _edit;
    private readonly Func<GeksaResourceProfile, Task> _togglePin;
    private readonly Func<GeksaResourceProfile, Task> _toggleAutoLaunch;
    private readonly Func<GeksaResourceProfile, Task> _delete;
    private readonly Func<GeksaResourceProfile, Task<bool>> _toggleFavorite;
    private bool _vpnConnected;

    public GeksaResourceItemViewModel(
        GeksaResourceProfile resource,
        Func<GeksaResourceProfile, Task> open,
        Func<GeksaResourceProfile, Task>? edit = null,
        Func<GeksaResourceProfile, Task>? togglePin = null,
        Func<GeksaResourceProfile, Task>? toggleAutoLaunch = null,
        Func<GeksaResourceProfile, Task>? delete = null,
        Func<GeksaResourceProfile, Task<bool>>? toggleFavorite = null,
        bool isFavorite = false)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _edit = edit ?? (_ => Task.CompletedTask);
        _togglePin = togglePin ?? (_ => Task.CompletedTask);
        _toggleAutoLaunch = toggleAutoLaunch ?? (_ => Task.CompletedTask);
        _delete = delete ?? (_ => Task.CompletedTask);
        _toggleFavorite = toggleFavorite ?? (_ => Task.FromResult(true));
        IsFavorite = isFavorite;
        ToggleFavoriteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (await _toggleFavorite(Resource)) IsFavorite = !IsFavorite;
        });
        IsPinned = resource.IsPinned;
        AutoLaunchOnVpn = resource.AutoLaunchOnVpn;
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        EditCommand = ReactiveCommand.CreateFromTask(EditAsync);
        TogglePinCommand = ReactiveCommand.CreateFromTask(TogglePinAsync);
        ToggleAutoLaunchCommand = ReactiveCommand.CreateFromTask(ToggleAutoLaunchAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
    }

    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleFavoriteCommand { get; }
    [Reactive] public partial bool IsFavorite { get; set; }

    [Reactive]
    public partial bool IsPinned { get; set; }

    public string PinToolTipText => IsPinned ? TriskoLocalization.Text("Открепить подключение") : TriskoLocalization.Text("Закрепить подключение вверху");

    public ReactiveCommand<RxVoid, RxVoid> TogglePinCommand { get; }

    [Reactive]
    public partial bool AutoLaunchOnVpn { get; set; }

    public string AutoLaunchToolTipText => AutoLaunchOnVpn ? TriskoLocalization.Text("Отключить автозапуск при VPN") : TriskoLocalization.Text("Включить автозапуск при VPN");

    public ReactiveCommand<RxVoid, RxVoid> ToggleAutoLaunchCommand { get; }

    public GeksaResourceProfile Resource { get; }

    public string TypeBadge => GeksaCorporateConfigurationCodec.ParseResourceType(Resource.Type) switch
    {
        GeksaResourceType.Rdp => "RDP",
        GeksaResourceType.Ssh => "SSH",
        GeksaResourceType.Smb => "SMB",
        _ => "?"
    };

    public string DisplayName => Resource.DisplayName;

    public string Description => Resource.Description;

    public bool IsRdp => GeksaCorporateConfigurationCodec.ParseResourceType(Resource.Type) == GeksaResourceType.Rdp;

    public bool IsSmb => GeksaCorporateConfigurationCodec.ParseResourceType(Resource.Type) == GeksaResourceType.Smb;

    public bool IsSsh => GeksaCorporateConfigurationCodec.ParseResourceType(Resource.Type) == GeksaResourceType.Ssh;

    public string TargetText => GeksaCorporateConfigurationCodec.ParseResourceType(Resource.Type) switch
    {
        GeksaResourceType.Rdp => $"{Resource.Host}:{Resource.Port}",
        GeksaResourceType.Ssh => $"{Resource.UserName}@{Resource.Host}:{Resource.Port}",
        GeksaResourceType.Smb => $"{DriveLabel}  ·  {GeksaSmbPath.Create(Resource.Host, Resource.Path)}",
        _ => Resource.Host
    };

    public string OpenButtonText => IsSmb && SmbState != GeksaSmbDriveState.Connected
        ? TriskoLocalization.Text("Подключить диск")
        : IsSmb ? TriskoLocalization.Text("Открыть диск") : TriskoLocalization.Text("Подключиться");

    public string DriveLabel => ActualDriveLetter
        ?? (Resource.DriveLetter.IsNotEmpty() ? Resource.DriveLetter.ToUpperInvariant() : TriskoLocalization.Text("Автовыбор"));

    public string SecondaryText => Resource.Description.IsNotEmpty()
        ? Resource.Description
        : GeksaCorporateConfigurationCodec.ParseResourceType(Resource.Type) switch
        {
            GeksaResourceType.Rdp => $"{Resource.Host}:{Resource.Port}",
            GeksaResourceType.Ssh => $"{Resource.UserName}@{Resource.Host}:{Resource.Port}",
            GeksaResourceType.Smb => GeksaSmbPath.Create(Resource.Host, Resource.Path),
            _ => Resource.Host
        };

    public ReactiveCommand<RxVoid, RxVoid> OpenCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> EditCommand { get; }

    [Reactive]
    public partial bool CanOpen { get; set; }

    [Reactive]
    public partial string AvailabilityText { get; set; } = TriskoLocalization.Text("Нужен VPN");

    [Reactive]
    public partial string AvailabilityColor { get; set; } = "#F3B35A";

    [Reactive]
    public partial GeksaSmbDriveState SmbState { get; set; } = GeksaSmbDriveState.Disconnected;

    [Reactive]
    public partial GeksaSmbDriveStatus? LastSmbStatus { get; set; }

    [Reactive]
    public partial string? ActualDriveLetter { get; set; }

    [Reactive]
    public partial bool IsSmbBusy { get; set; }

    public void UpdateAvailability(bool vpnConnected)
    {
        _vpnConnected = vpnConnected;
        UpdateStatePresentation();
    }

    public void ApplySmbStatus(GeksaSmbDriveStatus status)
    {
        if (!IsSmb)
        {
            return;
        }

        LastSmbStatus = status;
        SmbState = status.State;
        ActualDriveLetter = status.DriveLetter;
        UpdateStatePresentation(status.Message);
        RaiseDerivedProperties();
    }

    private async Task OpenAsync()
    {
        IsSmbBusy = IsSmb;
        UpdateStatePresentation();
        try
        {
            await _open(Resource);
        }
        finally
        {
            IsSmbBusy = false;
            UpdateStatePresentation();
            RaiseDerivedProperties();
        }
    }

    private async Task EditAsync()
    {
        await _edit(Resource);
    }

    private async Task DeleteAsync()
    {
        await _delete(Resource);
    }

    private void UpdateStatePresentation(string? statusMessage = null)
    {
        var vpnAvailable = !Resource.RequiresVpn || _vpnConnected;
        if (!IsSmb)
        {
            CanOpen = vpnAvailable;
            AvailabilityText = CanOpen ? TriskoLocalization.Text("Доступно") : TriskoLocalization.Text("Нужен VPN");
            AvailabilityColor = CanOpen ? "#3FD69A" : "#F3B35A";
            return;
        }

        CanOpen = vpnAvailable && !IsSmbBusy && SmbState != GeksaSmbDriveState.DriveLetterConflict;
        if (!vpnAvailable)
        {
            AvailabilityText = SmbState == GeksaSmbDriveState.Connected && ActualDriveLetter.IsNotEmpty()
                ? TriskoLocalization.Format("Диск {0} подключён · нужен VPN", ActualDriveLetter)
                : TriskoLocalization.Text("Нужен VPN");
            AvailabilityColor = "#F3B35A";
            return;
        }

        (AvailabilityText, AvailabilityColor) = SmbState switch
        {
            GeksaSmbDriveState.Connected =>
                (TriskoLocalization.Format("Подключено как {0}", ActualDriveLetter), "#3FD69A"),
            GeksaSmbDriveState.DriveLetterConflict =>
                (statusMessage ?? TriskoLocalization.Format("Буква {0} занята", DriveLabel), "#F0717E"),
            GeksaSmbDriveState.Error =>
                (statusMessage ?? TriskoLocalization.Text("Ошибка сетевого диска"), "#F0717E"),
            _ =>
                (IsSmbBusy ? TriskoLocalization.Text("Выполняется…") : TriskoLocalization.Text("Диск не подключён"), "#F3B35A")
        };
    }

    private void RaiseDerivedProperties()
    {
        this.RaisePropertyChanged(nameof(TargetText));
        this.RaisePropertyChanged(nameof(DriveLabel));
        this.RaisePropertyChanged(nameof(OpenButtonText));
    }

    private async Task TogglePinAsync()
    {
        var nextPinned = !IsPinned;
        IsPinned = nextPinned;
        this.RaisePropertyChanged(nameof(PinToolTipText));
        var updated = new GeksaResourceProfile
        {
            Id = Resource.Id,
            Type = Resource.Type,
            DisplayName = Resource.DisplayName,
            Description = Resource.Description,
            Host = Resource.Host,
            Port = Resource.Port,
            UserName = Resource.UserName,
            Path = Resource.Path,
            DriveLetter = Resource.DriveLetter,
            RequiresVpn = Resource.RequiresVpn,
            FullScreen = Resource.FullScreen,
            MultiMonitor = Resource.MultiMonitor,
            IsPinned = nextPinned,
            AutoLaunchOnVpn = AutoLaunchOnVpn
        };
        await _togglePin(updated);
    }

    private async Task ToggleAutoLaunchAsync()
    {
        var nextAutoLaunch = !AutoLaunchOnVpn;
        AutoLaunchOnVpn = nextAutoLaunch;
        this.RaisePropertyChanged(nameof(AutoLaunchToolTipText));
        var updated = new GeksaResourceProfile
        {
            Id = Resource.Id,
            Type = Resource.Type,
            DisplayName = Resource.DisplayName,
            Description = Resource.Description,
            Host = Resource.Host,
            Port = Resource.Port,
            UserName = Resource.UserName,
            Path = Resource.Path,
            DriveLetter = Resource.DriveLetter,
            RequiresVpn = Resource.RequiresVpn,
            FullScreen = Resource.FullScreen,
            MultiMonitor = Resource.MultiMonitor,
            IsPinned = IsPinned,
            AutoLaunchOnVpn = nextAutoLaunch
        };
        await _toggleAutoLaunch(updated);
    }
}
