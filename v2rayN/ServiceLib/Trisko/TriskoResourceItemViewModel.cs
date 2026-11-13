namespace ServiceLib.Trisko;

public partial class TriskoResourceItemViewModel : MyReactiveObject
{
    private readonly Func<TriskoResourceProfile, Task> _open;
    private readonly Func<TriskoResourceProfile, Task> _edit;
    private readonly Func<TriskoResourceProfile, Task> _togglePin;
    private readonly Func<TriskoResourceProfile, Task> _toggleAutoLaunch;
    private readonly Func<TriskoResourceProfile, Task> _delete;
    private readonly Func<TriskoResourceProfile, Task<bool>> _toggleFavorite;
    private bool _vpnConnected;

    public TriskoResourceItemViewModel(
        TriskoResourceProfile resource,
        Func<TriskoResourceProfile, Task> open,
        Func<TriskoResourceProfile, Task>? edit = null,
        Func<TriskoResourceProfile, Task>? togglePin = null,
        Func<TriskoResourceProfile, Task>? toggleAutoLaunch = null,
        Func<TriskoResourceProfile, Task>? delete = null,
        Func<TriskoResourceProfile, Task<bool>>? toggleFavorite = null,
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

    public TriskoResourceProfile Resource { get; }

    public string TypeBadge => TriskoCorporateConfigurationCodec.ParseResourceType(Resource.Type) switch
    {
        TriskoResourceType.Rdp => "RDP",
        TriskoResourceType.Ssh => "SSH",
        TriskoResourceType.Smb => "SMB",
        _ => "?"
    };

    public string DisplayName => Resource.DisplayName;

    public string Description => Resource.Description;

    public bool IsRdp => TriskoCorporateConfigurationCodec.ParseResourceType(Resource.Type) == TriskoResourceType.Rdp;

    public bool IsSmb => TriskoCorporateConfigurationCodec.ParseResourceType(Resource.Type) == TriskoResourceType.Smb;

    public bool IsSsh => TriskoCorporateConfigurationCodec.ParseResourceType(Resource.Type) == TriskoResourceType.Ssh;

    public string TargetText => TriskoCorporateConfigurationCodec.ParseResourceType(Resource.Type) switch
    {
        TriskoResourceType.Rdp => $"{Resource.Host}:{Resource.Port}",
        TriskoResourceType.Ssh => $"{Resource.UserName}@{Resource.Host}:{Resource.Port}",
        TriskoResourceType.Smb => $"{DriveLabel}  ·  {TriskoSmbPath.Create(Resource.Host, Resource.Path)}",
        _ => Resource.Host
    };

    public string OpenButtonText => IsSmb && SmbState != TriskoSmbDriveState.Connected
        ? TriskoLocalization.Text("Подключить диск")
        : IsSmb ? TriskoLocalization.Text("Открыть диск") : TriskoLocalization.Text("Подключиться");

    public string DriveLabel => ActualDriveLetter
        ?? (Resource.DriveLetter.IsNotEmpty() ? Resource.DriveLetter.ToUpperInvariant() : TriskoLocalization.Text("Автовыбор"));

    public string SecondaryText => Resource.Description.IsNotEmpty()
        ? Resource.Description
        : TriskoCorporateConfigurationCodec.ParseResourceType(Resource.Type) switch
        {
            TriskoResourceType.Rdp => $"{Resource.Host}:{Resource.Port}",
            TriskoResourceType.Ssh => $"{Resource.UserName}@{Resource.Host}:{Resource.Port}",
            TriskoResourceType.Smb => TriskoSmbPath.Create(Resource.Host, Resource.Path),
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
    public partial TriskoSmbDriveState SmbState { get; set; } = TriskoSmbDriveState.Disconnected;

    [Reactive]
    public partial TriskoSmbDriveStatus? LastSmbStatus { get; set; }

    [Reactive]
    public partial string? ActualDriveLetter { get; set; }

    [Reactive]
    public partial bool IsSmbBusy { get; set; }

    public void UpdateAvailability(bool vpnConnected)
    {
        _vpnConnected = vpnConnected;
        UpdateStatePresentation();
    }

    public void ApplySmbStatus(TriskoSmbDriveStatus status)
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

        CanOpen = vpnAvailable && !IsSmbBusy && SmbState != TriskoSmbDriveState.DriveLetterConflict;
        if (!vpnAvailable)
        {
            AvailabilityText = SmbState == TriskoSmbDriveState.Connected && ActualDriveLetter.IsNotEmpty()
                ? TriskoLocalization.Format("Диск {0} подключён · нужен VPN", ActualDriveLetter)
                : TriskoLocalization.Text("Нужен VPN");
            AvailabilityColor = "#F3B35A";
            return;
        }

        (AvailabilityText, AvailabilityColor) = SmbState switch
        {
            TriskoSmbDriveState.Connected =>
                (TriskoLocalization.Format("Подключено как {0}", ActualDriveLetter), "#3FD69A"),
            TriskoSmbDriveState.DriveLetterConflict =>
                (statusMessage ?? TriskoLocalization.Format("Буква {0} занята", DriveLabel), "#F0717E"),
            TriskoSmbDriveState.Error =>
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
        var updated = new TriskoResourceProfile
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
        var updated = new TriskoResourceProfile
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
