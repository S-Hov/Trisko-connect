namespace ServiceLib.Trisko;

/// <summary>
/// Stable Trisko-facing boundary over the v2rayN VPN lifecycle.
/// All mutating operations are serialized so the UI cannot start overlapping
/// reload and stop operations by clicking buttons repeatedly.
/// </summary>
public sealed class TriskoVpnController
{
    private readonly ITriskoVpnRuntime _runtime;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private TriskoVpnSnapshot _snapshot = TriskoVpnSnapshot.Disconnected;

    public TriskoVpnController(ITriskoVpnRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public EventChannel<TriskoVpnSnapshot> SnapshotChanged { get; } = new();

    public TriskoVpnSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task<TriskoVpnSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var runtimeSnapshot = await _runtime.GetSnapshotAsync(cancellationToken);
            var state = runtimeSnapshot.IsTransitioning ? TriskoVpnState.Connecting : IsVpnReady(runtimeSnapshot)
                ? TriskoVpnState.Connected
                : TriskoVpnState.Disconnected;

            return Publish(CreateSnapshot(state, runtimeSnapshot));
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public async Task<TriskoVpnSnapshot> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var before = await _runtime.GetSnapshotAsync(cancellationToken);
            if (before.IsTransitioning)
            {
                return Publish(CreateSnapshot(TriskoVpnState.Connecting, before));
            }
            if (IsVpnReady(before))
            {
                return Publish(CreateSnapshot(TriskoVpnState.Connected, before));
            }

            Publish(CreateSnapshot(TriskoVpnState.Connecting, before));

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

                return Publish(CreateSnapshot(TriskoVpnState.Connected, after));
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
            catch (TriskoVpnOperationException ex)
            {
                Logging.SaveLog(nameof(TriskoVpnController), ex);
                var current = await GetSnapshotWithoutCancellationAsync(before);
                return Publish(CreateFailure(
                    current,
                    ex.Code,
                    ex.UserMessage,
                    ex.Retryable, ex.ToString()));
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(TriskoVpnController), ex);
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

    public async Task<TriskoVpnSnapshot> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            var before = await _runtime.GetSnapshotAsync(cancellationToken);
            if (!before.IsCoreRunning)
            {
                await _runtime.DisconnectAsync(cancellationToken);
                var cleaned = await _runtime.GetSnapshotAsync(cancellationToken);
                return Publish(CreateSnapshot(TriskoVpnState.Disconnected, cleaned));
            }

            Publish(CreateSnapshot(TriskoVpnState.Disconnecting, before));

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

                return Publish(CreateSnapshot(TriskoVpnState.Disconnected, after));
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
            catch (TriskoVpnOperationException ex)
            {
                Logging.SaveLog(nameof(TriskoVpnController), ex);
                var current = await GetSnapshotWithoutCancellationAsync(before);
                return Publish(CreateFailure(
                    current,
                    ex.Code,
                    ex.UserMessage,
                    ex.Retryable, ex.ToString()));
            }
            catch (Exception ex)
            {
                Logging.SaveLog(nameof(TriskoVpnController), ex);
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

    private async Task<TriskoVpnRuntimeSnapshot> GetSnapshotWithoutCancellationAsync(
        TriskoVpnRuntimeSnapshot fallback)
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

    private TriskoVpnSnapshot Publish(TriskoVpnSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
        SnapshotChanged.Publish(snapshot);
        return snapshot;
    }

    private static TriskoVpnSnapshot CreateSnapshot(
        TriskoVpnState state,
        TriskoVpnRuntimeSnapshot runtimeSnapshot)
    {
        return new TriskoVpnSnapshot(
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

    private static TriskoVpnSnapshot CreateFailure(
        TriskoVpnRuntimeSnapshot runtimeSnapshot,
        string code,
        string message,
        bool retryable,
        string? details = null)
    {
        return new TriskoVpnSnapshot(
            TriskoVpnState.Failed,
            runtimeSnapshot.IsCoreRunning,
            runtimeSnapshot.IsTunEnabled,
            runtimeSnapshot.ProfileId,
            runtimeSnapshot.ProfileName,
            runtimeSnapshot.CoreType,
            new TriskoVpnFailure(code, message, retryable) { Details = details },
            DateTimeOffset.UtcNow)
        {
            RunningProfile = runtimeSnapshot.RunningProfile
        };
    }

    private static bool IsVpnReady(TriskoVpnRuntimeSnapshot snapshot)
    {
        return !snapshot.IsTransitioning && snapshot.IsCoreRunning && snapshot.IsTunEnabled;
    }
}
