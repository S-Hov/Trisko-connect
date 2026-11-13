namespace ServiceLib.Trisko;

public enum TriskoEcpDeviceAction
{
    Use,
    StopUsing,
    ToggleAutoUseDevice,
    ToggleAutoUsePort,
    Refresh,
    OpenVirtualHere
}

public sealed partial class TriskoEcpDeviceViewModel : MyReactiveObject
{
    private readonly Func<TriskoEcpDeviceViewModel, TriskoEcpDeviceAction, Task> _action;

    public TriskoEcpDeviceViewModel(
        TriskoVirtualUsbDevice device,
        Func<TriskoEcpDeviceViewModel, TriskoEcpDeviceAction, Task> action)
    {
        Device = device;
        _action = action;
        PrimaryActionCommand = ReactiveCommand.CreateFromTask(ExecutePrimaryActionAsync);
        ConnectCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(TriskoEcpDeviceAction.Use));
        DisconnectCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(TriskoEcpDeviceAction.StopUsing));
        ToggleAutoUseDeviceCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(TriskoEcpDeviceAction.ToggleAutoUseDevice));
        ToggleAutoUsePortCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(TriskoEcpDeviceAction.ToggleAutoUsePort));
        RefreshCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(TriskoEcpDeviceAction.Refresh));
        OpenVirtualHereCommand = ReactiveCommand.CreateFromTask(() => ExecuteAsync(TriskoEcpDeviceAction.OpenVirtualHere));
    }

    public TriskoVirtualUsbDevice Device { get; }
    public string Address => Device.Address;
    public string DisplayName => Device.DisplayName;
    public string ServerText => $"{Device.ServerName} · {Device.ServerAddress}";
    public string SerialText => Device.SerialNumber.IsNotEmpty() ? TriskoLocalization.Format("Серийный номер: {0}", Device.SerialNumber) : TriskoLocalization.Text("Серийный номер не передан");
    public bool IsStorageDevice => Device.IsStorageDevice;
    public bool IsAvailable => Device.State == TriskoVirtualUsbState.Available;
    public bool IsInUseLocally => Device.State == TriskoVirtualUsbState.InUseLocally;
    public bool IsInUseRemotely => Device.State == TriskoVirtualUsbState.InUseRemotely;
    public bool CanConnect => IsAvailable && !IsBusy;
    public bool CanDisconnect => IsInUseLocally && !IsBusy;
    public bool CanRunPrimaryAction => CanConnect || CanDisconnect;
    public string PrimaryActionText => Device.State switch
    {
        TriskoVirtualUsbState.InUseLocally => TriskoLocalization.Text("Отключить от этого ПК"),
        TriskoVirtualUsbState.Available => TriskoLocalization.Text("Подключить к этому ПК"),
        TriskoVirtualUsbState.InUseRemotely when Device.InUseBy.IsNotEmpty() => TriskoLocalization.Format("Занято ({0})", Device.InUseBy),
        TriskoVirtualUsbState.InUseRemotely => TriskoLocalization.Text("Занято другим ПК"),
        _ => TriskoLocalization.Text("Недоступно")
    };
    public string AutoUseDeviceText => Device.AutoUseMode is "device" or "devicePort" ? TriskoLocalization.Text("Отключить автоиспользование устройства") : TriskoLocalization.Text("Автоиспользование этого устройства");
    public string AutoUsePortText => Device.AutoUseMode is "port" or "devicePort" ? TriskoLocalization.Text("Отключить автоиспользование порта") : TriskoLocalization.Text("Автоиспользование этого USB-порта");

    public string StatusText => Device.State switch
    {
        TriskoVirtualUsbState.Available => TriskoLocalization.Text("Свободно"),
        TriskoVirtualUsbState.InUseLocally => TriskoLocalization.Text("Подключено к этому ПК"),
        TriskoVirtualUsbState.InUseRemotely when Device.InUseBy.IsNotEmpty() => TriskoLocalization.Format("Использует: {0}", Device.InUseBy),
        TriskoVirtualUsbState.InUseRemotely => TriskoLocalization.Text("Используется другим сотрудником"),
        _ => TriskoLocalization.Text("Недоступно")
    };

    public string StatusColor => Device.State switch
    {
        TriskoVirtualUsbState.Available => "#3FD69A",
        TriskoVirtualUsbState.InUseLocally => "#5B8DFF",
        TriskoVirtualUsbState.InUseRemotely => "#F3B35A",
        _ => "#75849A"
    };

    [Reactive]
    public partial bool IsBusy { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> PrimaryActionCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ConnectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DisconnectCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleAutoUseDeviceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ToggleAutoUsePortCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenVirtualHereCommand { get; }

    private Task ExecutePrimaryActionAsync() => ExecuteAsync(
        IsInUseLocally ? TriskoEcpDeviceAction.StopUsing : TriskoEcpDeviceAction.Use);

    private async Task ExecuteAsync(TriskoEcpDeviceAction action)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        this.RaisePropertyChanged(nameof(CanConnect));
        this.RaisePropertyChanged(nameof(CanDisconnect));
        this.RaisePropertyChanged(nameof(CanRunPrimaryAction));
        this.RaisePropertyChanged(nameof(PrimaryActionText));
        try
        {
            await _action(this, action);
        }
        finally
        {
            IsBusy = false;
            this.RaisePropertyChanged(nameof(CanConnect));
            this.RaisePropertyChanged(nameof(CanDisconnect));
            this.RaisePropertyChanged(nameof(CanRunPrimaryAction));
            this.RaisePropertyChanged(nameof(PrimaryActionText));
        }
    }
}
