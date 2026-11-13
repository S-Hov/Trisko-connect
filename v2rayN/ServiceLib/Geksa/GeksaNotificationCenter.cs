namespace ServiceLib.Geksa;

public enum GeksaNotificationKind { Information, Success, Warning, Error }

public sealed class GeksaNotificationItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Details { get; internal set; } = "";
    public GeksaNotificationKind Kind { get; init; }
    public bool IsError => Kind == GeksaNotificationKind.Error;
    public bool HasAction => ActionCommand is not null;
    public string? ActionText { get; init; }
    public System.Windows.Input.ICommand? ActionCommand { get; init; }
    public string AccentColor => Kind switch
    {
        GeksaNotificationKind.Success => "#059669",
        GeksaNotificationKind.Warning => "#D97706",
        GeksaNotificationKind.Error => "#DC2626",
        _ => "#3B82F6"
    };
}

public sealed class GeksaNotificationCenter : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private volatile bool _disposed;
    public ObservableCollection<GeksaNotificationItem> Items { get; } = [];
    public ReactiveCommand<GeksaNotificationItem, RxVoid> DismissCommand { get; }

    public GeksaNotificationCenter()
    {
        DismissCommand = ReactiveCommand.Create<GeksaNotificationItem>(item => { lock (_gate) Items.Remove(item); });
    }

    public void Show(string title, string message, GeksaNotificationKind kind = GeksaNotificationKind.Information,
        string? details = null, string? actionText = null, System.Windows.Input.ICommand? action = null)
    {
        if (string.IsNullOrWhiteSpace(message) || _disposed) return;
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            lock (_gate)
            {
                if (_disposed) return;
                // A lifecycle event can arrive through both the shell and the upstream bridge.
                var duplicate = Items.FirstOrDefault(item => item.Message == message && item.Kind == kind
                    && DateTimeOffset.UtcNow - item.CreatedAtUtc < TimeSpan.FromSeconds(5));
                if (duplicate is not null)
                {
                    if (details is not null && details.Length > duplicate.Details.Length) duplicate.Details = details;
                    return;
                }
                var item = new GeksaNotificationItem
                {
                    Title = title, Message = message, Kind = kind, Details = details ?? message,
                    ActionText = actionText, ActionCommand = action
                };
                while (Items.Count >= 4) Items.RemoveAt(0);
                Items.Add(item);
                if (!item.IsError) _ = ExpireAsync(item, _lifetime.Token);
            }
        });
    }

    private async Task ExpireAsync(GeksaNotificationItem item, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(7), cancellationToken);
            RxSchedulers.MainThreadScheduler.Schedule(() =>
            {
                lock (_gate) { if (!_disposed) Items.Remove(item); }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _lifetime.Dispose();
            DismissCommand.Dispose();
        }
    }
}
