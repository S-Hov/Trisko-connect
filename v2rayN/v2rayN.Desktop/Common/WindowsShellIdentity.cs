using System.Runtime.InteropServices;

namespace v2rayN.Desktop.Common;

internal static class WindowsShellIdentity
{
    // Matches Velopack's package/shortcut identity for installed and portable builds.
    internal const string AppUserModelId = "velopack.TriskoConnect-Desktop";

    internal static void Initialize()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        if (result < 0) Logging.SaveLog(nameof(WindowsShellIdentity), Marshal.GetExceptionForHR(result));
        // Refresh only this executable's shell item after an in-place icon update.
        if (Environment.ProcessPath is { } executable)
            SHChangeNotify(0x00002000, 0x0005, executable, IntPtr.Zero);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void SHChangeNotify(int eventId, uint flags, string item, IntPtr secondItem);
}
