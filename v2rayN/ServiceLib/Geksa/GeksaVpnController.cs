namespace ServiceLib.Geksa;

/// <summary>
/// Stable Geksa-facing boundary over the v2rayN VPN lifecycle.
/// All mutating operations are serialized so the UI cannot start overlapping
/// reload and stop operations by clicking buttons repeatedly.
/// </summary>
public sealed class GeksaVpnController
{
    private readonly IGeksaVpnRuntime _runtime;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private GeksaVpnSnapshot _snapshot = GeksaVpnSnapshot.Disconnected;

    public GeksaVpnController(IGeksaVpnRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public EventChannel<GeksaVpnSnapshot> SnapshotChanged { get; } = new();

    public GeksaVpnSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task<GeksaVpnSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var runtimeSnapshot = await _runtime.GetSnapshotAsync(cancellationToken);
            var state = runtimeSnapshot.IsTransitioning ? GeksaVpnState.Connecting : IsVpnReady(runtimeSnapshot)
                ? GeksaVpnState.Connected
                : GeksaVpnState.Disconnected;

            return Publish(CreateSnapshot(state, runtimeSnapshot));
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<GeksaVpnSnapshot> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var before = await _runtime.GetSnapshotAsync(cancellationToken);
            if (before.IsTransitioning)
            {
                return Publish(CreateSnapshot(GeksaVpnState.Connecting, before));
            }
            if (IsVpnReady(before))
            {
                return Publish(CreateSnapshot(GeksaVpnState.Connected, before));
            }

            Publish(CreateSnapshot(GeksaVpnState.Connecting, before));

            try
            {
                await Task.Run(async () => await _runtime.ConnectAsync(cancellationToken), cancellationToken);
                var after = await _runtime.GetSnapshotAsync(cancellationToken);
                if (!IsVpnReady(after))
                {
                    return Publish(CreateFailure(
                        after,
                        "coreNotRunning",
                        TriskoLocalization.Text("VPN-ядро не запустилось."),
                        retryable: true));
                }

                return Publish(CreateSnapshot(GeksaVpnState.Connected, after));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var current = await GetSnapshotWithoutCancellationAsync(before);
                Publish(CreateFailure(
                    current,
                    "connectCanceled",
                    TriskoLocalization.Text("Подключение было отменено."),
                    retryable: true));
                throw;
            }
            catch (GeksaVpnOperationException ex)
            {
                Logging.SaveLog(nameof(GeksaVpnController), ex);
                var current = await GetSnapshotWithoutCancellationAsync(before);
                return Publish(CreateFailure(
                    current,
                    ex.Code,
                    ex.UserMessage,
                    ex.Retryable, ex.ToString()));
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(GeksaVpnController), ex);
                var current = await GetSnapshotWithoutCancellationAsync(before);
                return Publish(CreateFailure(
                    current,
                    "connectFailed",
                    TriskoLocalization.Text("Не удалось подключиться к VPN."),
                    retryable: true, details: ex.ToString()));
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<GeksaVpnSnapshot> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var before = await _runtime.GetSnapshotAsync(cancellationToken);
            if (!before.IsCoreRunning)
            {
                await _runtime.DisconnectAsync(cancellationToken);
                var cleaned = await _runtime.GetSnapshotAsync(cancellationToken);
                return Publish(CreateSnapshot(GeksaVpnState.Disconnected, cleaned));
            }

            Publish(CreateSnapshot(GeksaVpnState.Disconnecting, before));

            try
            {
                await Task.Run(async () => await _runtime.DisconnectAsync(cancellationToken), cancellationToken);
                var after = await _runtime.GetSnapshotAsync(cancellationToken);
                if (after.IsCoreRunning)
                {
                    return Publish(CreateFailure(
                        after,
                        "coreStillRunning",
                        TriskoLocalization.Text("VPN-ядро не остановилось."),
                        retryable: true));
                }

                return Publish(CreateSnapshot(GeksaVpnState.Disconnected, after));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var current = await GetSnapshotWithoutCancellationAsync(before);
                Publish(CreateFailure(
                    current,
                    "disconnectCanceled",
                    TriskoLocalization.Text("Отключение было отменено."),
                    retryable: true));
                throw;
            }
            catch (GeksaVpnOperationException ex)
            {
                Logging.SaveLog(nameof(GeksaVpnController), ex);
                var current = await GetSnapshotWithoutCancellationAsync(before);
                return Publish(CreateFailure(
                    current,
                    ex.Code,
                    ex.UserMessage,
                    ex.Retryable, ex.ToString()));
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(GeksaVpnController), ex);
                var current = await GetSnapshotWithoutCancellationAsync(before);
                return Publish(CreateFailure(
                    current,
                    "disconnectFailed",
                    TriskoLocalization.Text("Не удалось отключить VPN."),
                    retryable: true, details: ex.ToString()));
            }
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<GeksaVpnRuntimeSnapshot> GetSnapshotWithoutCancellationAsync(
        GeksaVpnRuntimeSnapshot fallback)
    {
        try
        {
            return await _runtime.GetSnapshotAsync(CancellationToken.None);
        }
        catch
        {
            return fallback;
        }
    }

    private GeksaVpnSnapshot Publish(GeksaVpnSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
        SnapshotChanged.Publish(snapshot);
        return snapshot;
    }

    private static GeksaVpnSnapshot CreateSnapshot(
        GeksaVpnState state,
        GeksaVpnRuntimeSnapshot runtimeSnapshot)
    {
        return new GeksaVpnSnapshot(
            state,
            runtimeSnapshot.IsCoreRunning,
            runtimeSnapshot.IsTunEnabled,
            runtimeSnapshot.ProfileId,
            runtimeSnapshot.ProfileName,
            runtimeSnapshot.CoreType,
            Failure: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow)
        {
            RunningProfile = runtimeSnapshot.RunningProfile
        };
    }

    private static GeksaVpnSnapshot CreateFailure(
        GeksaVpnRuntimeSnapshot runtimeSnapshot,
        string code,
        string message,
        bool retryable,
        string? details = null)
    {
        return new GeksaVpnSnapshot(
            GeksaVpnState.Failed,
            runtimeSnapshot.IsCoreRunning,
            runtimeSnapshot.IsTunEnabled,
            runtimeSnapshot.ProfileId,
            runtimeSnapshot.ProfileName,
            runtimeSnapshot.CoreType,
            new GeksaVpnFailure(code, message, retryable) { Details = details },
            DateTimeOffset.UtcNow)
        {
            RunningProfile = runtimeSnapshot.RunningProfile
        };
    }

    private static bool IsVpnReady(GeksaVpnRuntimeSnapshot snapshot)
    {
        return !snapshot.IsTransitioning && snapshot.IsCoreRunning && snapshot.IsTunEnabled;
    }
}
