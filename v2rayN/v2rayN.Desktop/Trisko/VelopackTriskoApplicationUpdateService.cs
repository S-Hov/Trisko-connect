using Velopack;
using Velopack.Sources;

namespace v2rayN.Desktop.Trisko;

public sealed class VelopackTriskoApplicationUpdateService : ITriskoApplicationUpdateService
{
    public const string ReleaseRepositoryUrl =
        "https://github.com/S-Hov/Trisko-connect";

    private readonly TriskoVpnController _vpnController;
    private readonly UpdateManager _updateManager;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private UpdateInfo? _updateInfo;

    public VelopackTriskoApplicationUpdateService(TriskoVpnController vpnController)
    {
        _vpnController = vpnController ?? throw new ArgumentNullException(nameof(vpnController));
        _updateManager = new UpdateManager(new GithubSource(
            ReleaseRepositoryUrl,
            accessToken: null,
            prerelease: false));
    }

    public bool IsInstalled => _updateManager.IsInstalled;

    public string CurrentVersion =>
        _updateManager.CurrentVersion?.ToString() ?? Utils.GetVersionInfo();

    public string ReleaseSource => "S-Hov/Trisko-connect";

    public TriskoAvailableUpdate? AvailableUpdate { get; private set; }

    public async Task<TriskoUpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return TriskoUpdateCheckResult.NotInstalled;
        }

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _updateInfo = await _updateManager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();
            AvailableUpdate = _updateInfo is null
                ? null
                : new TriskoAvailableUpdate(
                    _updateInfo.TargetFullRelease.Version.ToString(),
                    _updateInfo.TargetFullRelease.NotesMarkdown);
            return _updateInfo is null
                ? TriskoUpdateCheckResult.UpToDate
                : TriskoUpdateCheckResult.Available;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DownloadAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var update = _updateInfo ?? throw new InvalidOperationException(
                TriskoLocalization.Text("Сначала проверьте наличие новой версии."));
            await _updateManager.DownloadUpdatesAsync(
                update,
                value => progress?.Report(value),
                cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ApplyAndRestartAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var update = _updateInfo ?? throw new InvalidOperationException(
                TriskoLocalization.Text("Обновление не подготовлено."));

            var disconnected = await _vpnController.DisconnectAsync(cancellationToken);
            if (disconnected.IsCoreRunning || disconnected.State != TriskoVpnState.Disconnected)
            {
                throw new InvalidOperationException(
                    TriskoLocalization.Text("Не удалось остановить VPN. Обновление отменено, чтобы не повредить TUN."));
            }

            await AppManager.Instance.AppExitAsync(needShutdown: false);
            _updateManager.ApplyUpdatesAndRestart(update.TargetFullRelease);
        }
        finally
        {
            _operationGate.Release();
        }
    }
}
