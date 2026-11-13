namespace ServiceLib.Trisko;

public partial class TriskoShellViewModel
{
    public TriskoNotificationCenter Notifications { get; } = new();
    public bool IsVpnLifecycleBusy => _isVpnConnectionBusy
        || _controller.Snapshot.State is TriskoVpnState.Connecting or TriskoVpnState.Disconnecting;
    public ReactiveCommand<RxVoid, RxVoid> RetryConnectionNowCommand { get; private set; } = null!;
    public ReactiveCommand<RxVoid, RxVoid> CancelReconnectCommand { get; private set; } = null!;
    private IDisposable? _failureNotificationSubscription;

    [Reactive] public partial bool IsReconnectPending { get; set; }
    [Reactive] public partial string ReconnectCountdownText { get; set; } = "";

    private void InitializeFeedback()
    {
        RetryConnectionNowCommand = ReactiveCommand.Create(() =>
        {
            if (!IsReconnectPending || IsBusy) return;
            _nextReconnectAtUtc = DateTimeOffset.UtcNow;
            UpdateReconnectCountdown();
        });
        CancelReconnectCommand = ReactiveCommand.Create(() =>
        {
            _desiredConnected = false;
            _nextReconnectAtUtc = DateTimeOffset.MinValue;
            _reconnectAttempt = 0;
            UpdateReconnectCountdown();
            Notifications.Show(TriskoLocalization.Text("Переподключение"), TriskoLocalization.Text("Автоматическое восстановление отменено."));
        });
        _failureNotificationSubscription = this.WhenAnyValue(vm => vm.FailureMessage)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Subscribe(message =>
            {
                var failure = _controller.Snapshot.Failure;
                Notifications.Show(TriskoLocalization.Text("Ошибка"), message!, TriskoNotificationKind.Error,
                    failure?.Message == message ? failure.Details ?? $"{failure.Code}\n{message}" : message);
            });
    }

    private void ScheduleReconnect()
    {
        var backoffIndex = Math.Min(_reconnectAttempt, ReconnectBackoff.Length - 1);
        _nextReconnectAtUtc = DateTimeOffset.UtcNow + ReconnectBackoff[backoffIndex];
        _reconnectAttempt++;
        UpdateReconnectCountdown();
    }

    private void ReportOperationFailure(string message, Exception exception)
    {
        Notifications.Show(TriskoLocalization.Text("Ошибка"), message, TriskoNotificationKind.Error, exception.ToString());
        _localFailureMessage = message;
        FailureMessage = message;
    }

    private void UpdateReconnectCountdown()
    {
        IsReconnectPending = _desiredConnected && !IsConnected && !IsConnecting && !IsBusy
            && _nextReconnectAtUtc != DateTimeOffset.MinValue;
        var seconds = IsReconnectPending
            ? (int)Math.Clamp(Math.Ceiling((_nextReconnectAtUtc - DateTimeOffset.UtcNow).TotalSeconds), 0, int.MaxValue)
            : 0;
        ReconnectCountdownText = IsReconnectPending
            ? TriskoLocalization.Format("Повторное подключение через {0} с · попытка {1}", seconds, _reconnectAttempt)
            : "";
    }

    private async Task CheckVpnRecoveryAsync(CancellationToken cancellationToken)
    {
        _ = RefreshOverviewRuntimesAsync();
        if (_isVpnConnectionBusy || IsVpnProfileTestBusy || IsLatencyTestBusy || _isVpnSelectionBusy || IsVpnSubscriptionBusy)
            return;
        var snapshot = await _controller.RefreshAsync(cancellationToken);
        if (_isVpnConnectionBusy || IsVpnProfileTestBusy || IsLatencyTestBusy || _isVpnSelectionBusy || IsVpnSubscriptionBusy)
            return;
        if (snapshot.State is TriskoVpnState.Connecting or TriskoVpnState.Disconnecting) return;
        if (snapshot.State == TriskoVpnState.Connected)
        {
            _reconnectAttempt = 0;
            _nextReconnectAtUtc = DateTimeOffset.MinValue;
            UpdateReconnectCountdown();
            return;
        }
        if (!_desiredConnected) { UpdateReconnectCountdown(); return; }
        if (_nextReconnectAtUtc == DateTimeOffset.MinValue)
        {
            ScheduleReconnect();
            Notifications.Show(TriskoLocalization.Text("Соединение прервано"), TriskoLocalization.Text("VPN отключился. Запланировано восстановление."),
                TriskoNotificationKind.Warning, actionText: TriskoLocalization.Text("Повторить сейчас"), action: RetryConnectionNowCommand);
            return;
        }
        UpdateReconnectCountdown();
        if (DateTimeOffset.UtcNow < _nextReconnectAtUtc) return;

        _isVpnConnectionBusy = true;
        ApplySnapshot(_controller.Snapshot);
        try
        {
            var reconnect = await _controller.ConnectAsync(cancellationToken);
            if (reconnect.State == TriskoVpnState.Connected)
            {
                _localFailureMessage = null;
                FailureMessage = null;
                _reconnectAttempt = 0;
                _nextReconnectAtUtc = DateTimeOffset.MinValue;
            }
            else if (reconnect.Failure?.Retryable == true) ScheduleReconnect();
            else _desiredConnected = false;
        }
        finally
        {
            _isVpnConnectionBusy = false;
            ApplySnapshot(_controller.Snapshot);
            UpdateReconnectCountdown();
        }
    }

    private Task CheckVpnRecoveryOnUiThreadAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RxSchedulers.MainThreadScheduler.Schedule(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CheckVpnRecoveryAsync(cancellationToken);
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { completion.TrySetCanceled(cancellationToken); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });
        return completion.Task;
    }
}
