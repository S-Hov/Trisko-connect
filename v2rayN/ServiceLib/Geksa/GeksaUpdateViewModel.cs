namespace ServiceLib.Geksa;

public partial class GeksaUpdateViewModel : MyReactiveObject
{
    private readonly IGeksaApplicationUpdateService _updateService;

    public GeksaUpdateViewModel(IGeksaApplicationUpdateService updateService)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        RunActionCommand = ReactiveCommand.CreateFromTask(RunActionAsync);
    }

    public ReactiveCommand<RxVoid, RxVoid> RunActionCommand { get; }

    public string CurrentVersionText => $"Trisko connect {_updateService.CurrentVersion}";

    public string ReleaseSourceText => _updateService.ReleaseSource;

    public bool IsInstalled => _updateService.IsInstalled;

    [Reactive]
    public partial bool IsBusy { get; set; }

    [Reactive]
    public partial int DownloadProgress { get; set; }

    [Reactive]
    public partial bool IsDownloadProgressVisible { get; set; }

    [Reactive]
    public partial bool HasAvailableUpdate { get; set; }

    [Reactive]
    public partial string AvailableVersionText { get; set; } = TriskoLocalization.Text("Проверка ещё не выполнялась");

    [Reactive]
    public partial string StatusText { get; set; } = TriskoLocalization.Text("Центр обновлений готов к работе.");

    [Reactive]
    public partial string? ReleaseNotesText { get; set; }

    [Reactive]
    public partial string? ErrorMessage { get; set; }

    [Reactive]
    public partial string ActionButtonText { get; set; } = TriskoLocalization.Text("Проверить обновления");

    [Reactive]
    public partial bool CanRunAction { get; set; } = true;

    public async Task InitializeAsync(bool checkImmediately)
    {
        if (!IsInstalled)
        {
            CanRunAction = false;
            ActionButtonText = TriskoLocalization.Text("Недоступно в portable-версии");
            StatusText = TriskoLocalization.Text("Автообновление работает после установки через TriskoConnect-Setup.exe.");
            AvailableVersionText = TriskoLocalization.Text("Portable-режим");
            return;
        }

        if (checkImmediately)
        {
            await CheckForUpdatesAsync();
            return;
        }

        ApplyKnownUpdateState();
    }

    private async Task RunActionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (HasAvailableUpdate)
        {
            await DownloadAndInstallAsync();
        }
        else
        {
            await CheckForUpdatesAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        BeginOperation(TriskoLocalization.Text("Проверяем наличие новой версии…"), TriskoLocalization.Text("Проверка…"));
        try
        {
            var result = await _updateService.CheckAsync();
            HasAvailableUpdate = result == GeksaUpdateCheckResult.Available;
            if (_updateService.AvailableUpdate is { } update)
            {
                AvailableVersionText = TriskoLocalization.Format("Доступна версия {0}", update.Version);
                ReleaseNotesText = update.ReleaseNotesMarkdown;
                StatusText = TriskoLocalization.Text("Обновление найдено. Его можно скачать и установить сейчас.");
                ActionButtonText = TriskoLocalization.Text("Скачать и установить");
            }
            else
            {
                AvailableVersionText = TriskoLocalization.Text("Установлена актуальная версия");
                ReleaseNotesText = null;
                StatusText = TriskoLocalization.Text("Новых версий нет.");
                ActionButtonText = TriskoLocalization.Text("Проверить снова");
            }
        }
        catch (Exception exception)
        {
            HandleError(TriskoLocalization.Text("Не удалось проверить обновления"), exception);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task DownloadAndInstallAsync()
    {
        BeginOperation(TriskoLocalization.Text("Загрузка обновления…"), TriskoLocalization.Text("Загрузка 0%"));
        IsDownloadProgressVisible = true;
        try
        {
            var progress = new Progress<int>(value =>
            {
                DownloadProgress = value;
                ActionButtonText = TriskoLocalization.Format("Загрузка {0}%", value);
                StatusText = TriskoLocalization.Format("Загрузка обновления: {0}%", value);
            });
            await _updateService.DownloadAsync(progress);
            StatusText = TriskoLocalization.Text("Останавливаем VPN, устанавливаем обновление и перезапускаем программу…");
            ActionButtonText = TriskoLocalization.Text("Установка…");
            await _updateService.ApplyAndRestartAsync();
        }
        catch (Exception exception)
        {
            HandleError(TriskoLocalization.Text("Не удалось установить обновление"), exception);
            EndOperation();
        }
    }

    private void BeginOperation(string status, string actionText)
    {
        IsBusy = true;
        CanRunAction = false;
        ErrorMessage = null;
        DownloadProgress = 0;
        IsDownloadProgressVisible = false;
        StatusText = status;
        ActionButtonText = actionText;
    }

    private void EndOperation()
    {
        IsBusy = false;
        CanRunAction = IsInstalled;
    }

    private void HandleError(string prefix, Exception exception)
    {
        Logging.SaveLog(nameof(GeksaUpdateViewModel), exception);
        ErrorMessage = IsNetworkError(exception)
            ? TriskoLocalization.Text("Сервер обновлений недоступен. Проверьте интернет и повторите попытку.")
            : $"{prefix}: {exception.Message}";
        StatusText = prefix + ".";
        ActionButtonText = HasAvailableUpdate ? TriskoLocalization.Text("Повторить установку") : TriskoLocalization.Text("Проверить снова");
    }

    private void ApplyKnownUpdateState()
    {
        if (_updateService.AvailableUpdate is not { } update)
        {
            AvailableVersionText = TriskoLocalization.Text("Установлена актуальная версия");
            StatusText = TriskoLocalization.Text("Новых версий нет.");
            ActionButtonText = TriskoLocalization.Text("Проверить снова");
            return;
        }

        HasAvailableUpdate = true;
        AvailableVersionText = TriskoLocalization.Format("Доступна версия {0}", update.Version);
        ReleaseNotesText = update.ReleaseNotesMarkdown;
        StatusText = TriskoLocalization.Text("Обновление найдено. Его можно скачать и установить сейчас.");
        ActionButtonText = TriskoLocalization.Text("Скачать и установить");
    }

    private static bool IsNetworkError(Exception exception)
    {
        return exception is HttpRequestException or TaskCanceledException or IOException or SocketException ||
            exception.InnerException is not null && IsNetworkError(exception.InnerException);
    }
}
