namespace ServiceLib.Trisko;

using ServiceLib.ViewModels;

/// <summary>
/// Thin adapter to the existing v2rayN lifecycle. It intentionally delegates
/// configuration generation, TUN handling and process management to upstream.
/// </summary>
public sealed class V2RayNTriskoVpnRuntime : ITriskoVpnRuntime
{
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly TriskoNetworkBindingSelector _networkBindingSelector;
    private readonly Config _config;

    public V2RayNTriskoVpnRuntime(MainWindowViewModel mainWindowViewModel)
        : this(mainWindowViewModel, new TriskoNetworkBindingSelector())
    {
    }

    public V2RayNTriskoVpnRuntime(
        MainWindowViewModel mainWindowViewModel,
        TriskoNetworkBindingSelector networkBindingSelector)
    {
        _mainWindowViewModel = mainWindowViewModel
            ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
        _networkBindingSelector = networkBindingSelector
            ?? throw new ArgumentNullException(nameof(networkBindingSelector));
        _config = AppManager.Instance.Config;
    }

    public async Task<TriskoVpnRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var core = CoreManager.Instance;
        var active = core.ActiveProfile;
        if (core.IsCoreRunning && active is not null)
        {
            return new TriskoVpnRuntimeSnapshot(true, core.IsTunRunning,
                active.IndexId, active.Name, active.CoreType)
            {
                IsTransitioning = _mainWindowViewModel.IsReloading,
                RunningProfile = active
            };
        }
        var profile = await AppManager.Instance.GetProfileItem(_config.IndexId);
        cancellationToken.ThrowIfCancellationRequested();

        ECoreType? coreType = profile is null
            ? null
            : AppManager.Instance.GetCoreType(profile, profile.ConfigType);

        return new TriskoVpnRuntimeSnapshot(
            CoreManager.Instance.IsCoreRunning,
            CoreManager.Instance.IsTunRunning,
            profile?.IndexId,
            profile?.Remarks,
            coreType)
        {
            IsTransitioning = _mainWindowViewModel.IsReloading
        };
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profile = await AppManager.Instance.GetProfileItem(_config.IndexId);
        if (profile is null)
        {
            throw new TriskoVpnOperationException(
                "profileMissing",
                TriskoLocalization.Text("VPN-профиль не настроен. Откройте расширенные настройки VPN и добавьте профиль."),
                retryable: false);
        }

        var localPortBeforeDiagnostics = _config.Inbound.First().LocalPort;
        var startupReport = await TriskoStartupDiagnosticsService.Instance
            .InspectAndRepairAsync(_config, cancellationToken);
        if (startupReport.EffectivePort != localPortBeforeDiagnostics)
        {
            AppManager.Instance.ResetStatePorts();
        }
        if (startupReport.HasErrors)
        {
            throw new TriskoVpnOperationException(
                "startupDiagnosticsFailed",
                startupReport.DisplayMessage,
                retryable: true);
        }

        var requiredInterface = _config.TunModeItem.TriskoAutoSelectInterface
            ? null
            : _config.TunModeItem.TriskoPreferredInterface;
        if (!_config.TunModeItem.TriskoAutoSelectInterface && requiredInterface.IsNullOrEmpty())
        {
            throw new TriskoVpnOperationException(
                "networkInterfaceSelectionInvalid",
                TriskoLocalization.Text("Ручной режим включён, но сетевой адаптер не выбран."),
                retryable: false);
        }

        var binding = await _networkBindingSelector.SelectAsync(
            profile.Address,
            profile.Port,
            requiredInterface,
            cancellationToken);

        var oldEnableTun = _config.TunModeItem.EnableTun;
        var oldPreserveBinding = _config.TunModeItem.PreserveOutboundInterfaceBinding;
        var oldBindInterface = _config.CoreBasicItem.BindInterface;
        var oldSendThrough = _config.CoreBasicItem.SendThrough;
        var settingsChanged = false;

        if (!oldEnableTun)
        {
            if (Utils.IsWindows() && !Utils.IsAdministrator())
            {
                throw new TriskoVpnOperationException(
                    "administratorRequired",
                    TriskoLocalization.Text("Для первого запуска TUN откройте Trisko connect от имени администратора."));
            }

            _config.TunModeItem.EnableTun = true;
            settingsChanged = true;
        }

        if (!_config.TunModeItem.PreserveOutboundInterfaceBinding)
        {
            _config.TunModeItem.PreserveOutboundInterfaceBinding = true;
            settingsChanged = true;
        }

        if (!string.Equals(
                _config.CoreBasicItem.BindInterface,
                binding.InterfaceName,
                StringComparison.OrdinalIgnoreCase))
        {
            _config.CoreBasicItem.BindInterface = binding.InterfaceName;
            settingsChanged = true;
        }

        var localAddress = binding.LocalAddress.ToString();
        if (!string.Equals(
                _config.CoreBasicItem.SendThrough,
                localAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            _config.CoreBasicItem.SendThrough = localAddress;
            settingsChanged = true;
        }

        if (settingsChanged && await ConfigHandler.SaveConfig(_config) != 0)
        {
            _config.TunModeItem.EnableTun = oldEnableTun;
            _config.TunModeItem.PreserveOutboundInterfaceBinding = oldPreserveBinding;
            _config.CoreBasicItem.BindInterface = oldBindInterface;
            _config.CoreBasicItem.SendThrough = oldSendThrough;
            throw new TriskoVpnOperationException(
                "networkBindingSaveFailed",
                TriskoLocalization.Text("Не удалось сохранить выбранный сетевой адаптер."));
        }

        Logging.SaveLog(
            $"Trisko VPN underlay: mode={(_config.TunModeItem.TriskoAutoSelectInterface ? "auto" : "manual")}, " +
            $"interface={binding.InterfaceName}, ifIndex={binding.InterfaceIndex}, " +
            $"local={binding.LocalAddress}, remote={binding.RemoteAddress}:{profile.Port}");

        await _mainWindowViewModel.Reload();
        cancellationToken.ThrowIfCancellationRequested();
        if (CoreManager.Instance.LastStartFailure is { } failure)
        {
            throw new TriskoVpnOperationException("coreStartupFailed", failure, retryable: true);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await CoreManager.Instance.CoreStop();
        await SysProxyHandler.UpdateSysProxy(_config, forceDisable: true);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
