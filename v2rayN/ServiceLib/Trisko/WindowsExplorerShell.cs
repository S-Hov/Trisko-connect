using System.Globalization;

namespace ServiceLib.Trisko;

/// <summary>
/// Sends an Open request to the Explorer instance which owns the
/// interactive desktop. This is important when the caller is elevated: a
/// mapped drive belongs to the unelevated user logon session and is not
/// necessarily visible to a newly created elevated process.
/// </summary>
internal static class WindowsExplorerShell
{
    private static readonly Guid ShellWindowsClassId =
        new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    private const int ShellWindowClassDesktop = 8;
    private const int ShellWindowFindNeedDispatch = 1;
    public static Task OpenFolderAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                TriskoLocalization.Text("Проводник доступен только в Windows."));
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                OpenFolder(path);
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Trisko Explorer shell request"
        };

        // Shell automation requires an STA thread. A dedicated thread also
        // avoids depending on the apartment used by the UI framework.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static Task LaunchProcessAsync(
        string executable,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                TriskoLocalization.Text("Запуск в пользовательском сеансе поддерживается только в Windows."));
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                WithDesktopShell(shell =>
                    InvokeMethod(
                        shell,
                        "ShellExecute",
                        executable,
                        arguments,
                        workingDirectory,
                        string.Empty,
                        0));
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Trisko user-session process request"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void OpenFolder(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                TriskoLocalization.Text("Проводник доступен только в Windows."));
        }

        WithDesktopShell(shell => InvokeMethod(shell, "Open", path));
    }

    private static void WithDesktopShell(Action<object> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                TriskoLocalization.Text("Проводник доступен только в Windows."));
        }

        object? shellWindows = null;
        object? desktopWindow = null;
        object? desktopDocument = null;
        object? shellApplication = null;

        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(
                ShellWindowsClassId,
                throwOnError: true)
                ?? throw new InvalidOperationException(
                    TriskoLocalization.Text("Windows не предоставила компонент ShellWindows."));

            shellWindows = Activator.CreateInstance(shellWindowsType)
                ?? throw new InvalidOperationException(
                    TriskoLocalization.Text("Не удалось подключиться к Проводнику Windows."));

            object?[] findWindowArguments =
            [
                null,
                null,
                ShellWindowClassDesktop,
                0,
                ShellWindowFindNeedDispatch
            ];
            desktopWindow = InvokeMethod(
                shellWindows,
                "FindWindowSW",
                findWindowArguments);
            if (desktopWindow is null)
            {
                throw new InvalidOperationException(
                    TriskoLocalization.Text("Не найден пользовательский Проводник Windows."));
            }

            // Document.Application is IShellFolderViewDual.Application from
            // the real desktop Explorer. Calling Open on this object delegates
            // folder navigation to the desktop user's non-elevated shell.
            desktopDocument = GetProperty(desktopWindow, "Document");
            if (desktopDocument is null)
            {
                throw new InvalidOperationException(
                    TriskoLocalization.Text("Проводник не предоставил объект рабочего стола."));
            }

            shellApplication = GetProperty(desktopDocument, "Application");
            if (shellApplication is null)
            {
                throw new InvalidOperationException(
                    TriskoLocalization.Text("Проводник не предоставил службу открытия папок."));
            }

            action(shellApplication);
        }
        finally
        {
            ReleaseComObject(shellApplication);
            ReleaseComObject(desktopDocument);
            ReleaseComObject(desktopWindow);
            ReleaseComObject(shellWindows);
        }
    }

    private static object? GetProperty(object target, string propertyName) =>
        target.GetType().InvokeMember(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture);

    private static object? InvokeMethod(object target, string methodName, params object?[] arguments) =>
        target.GetType().InvokeMember(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod,
            binder: null,
            target,
            arguments,
            CultureInfo.InvariantCulture);

    private static void ReleaseComObject(object? value)
    {
        if (!OperatingSystem.IsWindows()
            || value is null
            || !Marshal.IsComObject(value))
        {
            return;
        }

        try
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
        catch (InvalidComObjectException)
        {
            // Multiple automation properties may resolve to the same RCW.
            // It has already been released, so there is nothing else to do.
        }
    }
}
