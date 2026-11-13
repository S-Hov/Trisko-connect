using System.Globalization;
using Semi.Avalonia;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Views;

namespace v2rayN.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    public static void ApplySemiLocale(string? language = null)
    {
        var lang = language;
        if (string.IsNullOrEmpty(lang))
        {
            lang = AppManager.Instance?.Config?.UiItem?.CurrentLanguage;
        }

        var cultureCode = lang switch
        {
            "ru" => "ru-RU",
            "zh-Hans" => "zh-CN",
            "zh-Hant" => "zh-TW",
            "en" => "en-US",
            _ => "ru-RU"
        };

        try
        {
            if (Current is not null)
            {
                SemiTheme.OverrideLocaleResources(Current, new CultureInfo(cultureCode));
            }
        }
        catch
        {
            // fallback gracefully
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Design.IsDesignMode)
        {
            var splash = new StartupSplashWindow();
            desktop.MainWindow = splash;
            splash.Loaded += async (_, _) => await CompleteStartupAsync(desktop, splash);
        }
        else
        {
            LoadApplicationResources();
            CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void LoadApplicationResources()
    {
        // Load localized resources only after the saved language is available.
        AvaloniaXamlLoader.Load(this);
        ApplySemiLocale();
    }

    private async Task CompleteStartupAsync(IClassicDesktopStyleApplicationLifetime desktop, StartupSplashWindow splash)
    {
        try
        {
            if (OperatingSystem.IsWindows() && !await Task.Run(Program.InitializeApplication))
            {
                desktop.Shutdown(1);
                return;
            }

            // InitApp ran on a worker so diagnostics don't freeze the splash.
            TriskoLocalization.Initialize(AppManager.Instance.Config.UiItem.CurrentLanguage);
            LoadApplicationResources();
            splash.SetLanguage();
            await Task.Run(() => AppManager.Instance.InitComponents());
            DataContext = StatusBarViewModel.Instance;
            CreateMainWindow();

            // Transfer ownership before closing the temporary main window.
            // The existing MainWindow.Loaded handler still handles tray startup.
            desktop.MainWindow!.Show();
            splash.Close();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("Application startup", ex);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), ex.ToString());
            desktop.Shutdown(1);
        }
    }

    private void CreateMainWindow()
    {
        var viewLocator = SimpleViewLocator.Instance;
        DataTemplates.Add(viewLocator);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = new MainWindowViewModel();
            var mainWindow = (MainWindow)viewLocator.Build(mainWindowViewModel);
            mainWindow.ViewModel = mainWindowViewModel;
            desktop.MainWindow = mainWindow;

            if (OperatingSystem.IsMacOS())
            {
                Current?.TryGetFeature<IActivatableLifetime>()?.Activated += OnMacOSActivated;
            }
        }
    }

    #region MacOS Activation

    private void OnMacOSActivated(object? sender, ActivatedEventArgs args)
    {
        if (args.Kind != ActivationKind.Reopen)
        {
            return;
        }

        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        var isMiniaturized = MacAppUtils.IsWindowMiniaturized(mainWindow);

        Dispatcher.UIThread.Post(() =>
        {
            if (isMiniaturized)
            {
                RestoreMacOSAccessoryPolicyAfterMiniaturize(mainWindow);
                mainWindow.ShowHideWindow(true);
                return;
            }

            if (!AppManager.Instance.Config.UiItem.MacOSShowInDock)
            {
                MacAppUtils.SetActivationPolicyAccessory();
            }

            mainWindow.ShowHideWindow(true);
        });
    }

    private static void RestoreMacOSAccessoryPolicyAfterMiniaturize(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock)
        {
            return;
        }

        mainWindow
            .GetObservable(Window.WindowStateProperty)
            .Skip(1)
            .Where(state => state != WindowState.Minimized)
            .Take(1)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => QueueMacOSAccessoryPolicyRestore(mainWindow));
    }

    private static void QueueMacOSAccessoryPolicyRestore(MainWindow mainWindow)
    {
        // AppKit may keep isMiniaturized set until the Dock restore animation finishes.
        DispatcherTimer.RunOnce(() => RestoreMacOSAccessoryPolicy(mainWindow), TimeSpan.FromMilliseconds(300));
    }

    private static void RestoreMacOSAccessoryPolicy(MainWindow mainWindow)
    {
        if (AppManager.Instance.Config.UiItem.MacOSShowInDock || MacAppUtils.IsWindowMiniaturized(mainWindow))
        {
            return;
        }

        MacAppUtils.SetActivationPolicyAccessory();
        mainWindow.Activate();
        mainWindow.Focus();
    }

    #endregion MacOS Activation

    #region App Event

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject != null)
        {
            Logging.SaveLog("CurrentDomain_UnhandledException", (Exception)e.ExceptionObject);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.SaveLog("TaskScheduler_UnobservedTaskException", e.Exception);
    }

    private async void MenuAddServerViaClipboardClick(object? sender, EventArgs e)
    {
        try
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: not null })
            {
                AppEvents.AddServerViaClipboardRequested.Publish();
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("MenuAddServerViaClipboardClick", ex);
        }
    }

    private async void MenuExit_Click(object? sender, EventArgs e)
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow mainWindow })
        {
            await mainWindow.RequestApplicationExitAsync(confirm: false);
            return;
        }

        await AppManager.Instance.AppExitAsync(false);
        AppManager.Instance.Shutdown(true);
    }

    #endregion App Event
}
