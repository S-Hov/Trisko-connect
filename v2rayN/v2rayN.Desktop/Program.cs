using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;
using Velopack;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace v2rayN.Desktop;

internal class Program
{
    public static EventWaitHandle ProgramStarted;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must be the first framework call. It may execute a fast
        // install/update hook and terminate without starting Avalonia.
        VelopackApp.Build()
            .SetAppUserModelId(WindowsShellIdentity.AppUserModelId)
            .SetAutoApplyOnStartup(false)
            .Run();

        // SMB drive mappings belong to a Windows logon session. The main app
        // runs elevated for TUN, so a short-lived helper is launched by the
        // user's Explorer and must finish before the normal UAC bootstrap.
        if (WindowsTriskoSmbDriveService.TryRunUserSessionHelper(args, out var helperExitCode))
        {
            Environment.ExitCode = helperExitCode;
            return;
        }

        // Velopack install/update hooks must run without UAC. For a normal
        // application launch we immediately restart the same executable with
        // administrator rights before any VPN or UI initialization happens.
        if (RelaunchElevatedIfRequired(args))
        {
            return;
        }

        if (OnStartup(args) == false)
        {
            Environment.Exit(0);
            return;
        }


        try
        {
            WindowsShellIdentity.Initialize();
            BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        }
        catch (System.Exception ex)
        {
            System.IO.File.WriteAllText("crash.txt", ex.ToString());
            throw;
        }
    }

    private static bool RelaunchElevatedIfRequired(string[] args)
    {
#if DEBUG
        // UI development can use a separate data directory without requesting elevation.
        // Production builds always retain the normal TUN elevation flow.
        if (args.Contains("--ui-preview") &&
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Global.DataDirectoryOverride)))
            return false;
#endif

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return false;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(global::ServiceLib.Trisko.TriskoLocalization.Text("Не удалось определить путь к приложению."));
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // The user cancelled the Windows UAC dialog. The unelevated
            // bootstrap process simply exits without showing a crash dialog.
        }

        return true;
    }

    private static bool OnStartup(string[]? Args)
    {
        if (Utils.IsWindows())
        {
            var exePathKey = Utils.GetMd5(Utils.GetExePath());
            var rebootas = (Args ?? []).Any(t => t == Global.RebootAs);
            ProgramStarted = new EventWaitHandle(false, EventResetMode.AutoReset, exePathKey, out var bCreatedNew);
            if (!rebootas && !bCreatedNew)
            {
                ProgramStarted.Set();
                return false;
            }
        }
        else
        {
            _ = new Mutex(true, "v2rayN", out var bOnlyOneInstance);
            if (!bOnlyOneInstance)
            {
                return false;
            }
        }

        // Windows loads configuration and runs startup diagnostics after the
        // splash is visible. macOS needs its configuration to build Avalonia.
        return OperatingSystem.IsWindows() || InitializeApplication();
    }

    internal static bool InitializeApplication()
    {
        if (!AppManager.Instance.InitApp())
        {
            return false;
        }

        TriskoLocalization.Initialize(AppManager.Instance.Config.UiItem.CurrentLanguage);
        AppManager.Instance.WindowDialog = new WindowDialog();
        return true;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
           .UsePlatformDetect()
           //.WithInterFont()
           .WithFontByDefault()
#if DEBUG
           .WithDeveloperTools()
#endif
           .LogToTrace()
           .UseReactiveUI(_ => { });

        if (OperatingSystem.IsMacOS())
        {
            var showInDock = Design.IsDesignMode || AppManager.Instance.Config.UiItem.MacOSShowInDock;
            builder = builder.With(new MacOSPlatformOptions { ShowInDock = showInDock });
        }

        return builder;
    }
}
