using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ServiceLib.Geksa;

public enum GeksaAnyDeskStrategy
{
    Direct = 2
}

public enum GeksaAnyDeskViewMode
{
    Integration = 0,
    EmbeddedClient = 1
}

public sealed record GeksaAnyDeskStrategyOption(
    GeksaAnyDeskStrategy Value,
    string DisplayName,
    string Description,
    bool IsExperimental = false,
    string IconData = "",
    string BadgeText = "",
    string IconColor = "#4F8CFF");

public sealed record GeksaAnyDeskClientInfo(
    string Path,
    bool Exists,
    string? Version,
    bool IsSupportedVersion,
    string? Sha256,
    bool HasExpectedSha256,
    bool IsInstalled = false,
    bool IsAlreadyRunning = false)
{
    public bool IsReady => Exists && IsSupportedVersion && (HasExpectedSha256 || IsInstalled || IsAlreadyRunning);

    public string StatusText => !Exists
        ? TriskoLocalization.Text("Portable-клиент не найден")
        : IsReady
            ? IsInstalled ? TriskoLocalization.Format("Используется установленный AnyDesk {0}", Version) : IsAlreadyRunning
                ? TriskoLocalization.Format("Используется запущенный AnyDesk {0}", Version) : TriskoLocalization.Format("AnyDesk {0} готов к запуску без установки", Version)
            : IsSupportedVersion
                ? TriskoLocalization.Text("AnyDesk 6.1.0 найден, но его контрольная сумма отличается от проверенного файла")
            : TriskoLocalization.Format("Найдена версия {0}; ожидается AnyDesk 6.1.0", Version ?? TriskoLocalization.Text("неизвестна"));
}

public static class GeksaAnyDeskClientLocator
{
    public const string SupportedVersion = "6.1.0";
    public const string ExpectedSha256 =
        "8D7A66B358A2AA010B3ECA61F1881DAA0A3A480B620CF1704361CE956F5B3A08";

    public static string BundledExecutablePath => Path.Combine(
        AppContext.BaseDirectory,
        "Tools",
        "AnyDesk",
        "AnyDesk.exe");

    public static GeksaAnyDeskClientInfo Inspect(string? path)
    {
        var normalizedPath = path?.Trim().Trim('"');
        if (normalizedPath.IsNullOrEmpty())
        {
            normalizedPath = BundledExecutablePath;
        }

        if (!File.Exists(normalizedPath))
        {
            return new(normalizedPath, false, null, false, null, false);
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(normalizedPath);
            var version = versionInfo.FileVersion ?? versionInfo.ProductVersion;
            var supported = IsSupportedVersion(version);
            using var stream = File.OpenRead(normalizedPath);
            var sha256 = Convert.ToHexString(SHA256.HashData(stream));
            return new(
                normalizedPath,
                true,
                version,
                supported,
                sha256,
                string.Equals(sha256, ExpectedSha256, StringComparison.OrdinalIgnoreCase),
                IsInstalledPath(normalizedPath!),
                RunningClients().Any(client => string.Equals(client.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaAnyDeskClientLocator), ex);
            return new(normalizedPath, true, null, false, null, false);
        }
    }

    public static bool IsSupportedVersion(string? version) =>
        Version.TryParse(Regex.Match(version?.Trim() ?? "", @"^(?:ad\s+)?(\d+\.\d+\.\d+(?:\.\d+)?)$",
            RegexOptions.IgnoreCase).Groups[1].Value, out var parsed)
        && parsed.Major == 6
        && parsed.Minor == 1
        && parsed.Build == 0;

    internal static bool IsInstalledPath(string path) => InstalledPaths().Any(candidate =>
        string.Equals(Path.GetFullPath(path), candidate, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> InstalledPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(root)) paths.Add(Path.Combine(root, "AnyDesk", "AnyDesk.exe"));
        }
        if (!OperatingSystem.IsWindows()) return paths.ToList();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = key.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    using var entry = uninstall!.OpenSubKey(name);
                    if ((entry?.GetValue("DisplayName") as string)?.Contains("AnyDesk", StringComparison.OrdinalIgnoreCase) != true) continue;
                    var location = entry?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(location)) paths.Add(Path.Combine(location.Trim('"'), "AnyDesk.exe"));
                    var icon = (entry?.GetValue("DisplayIcon") as string)?.Trim().Trim('"');
                    var end = icon?.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) ?? -1;
                    if (end >= 0) paths.Add(icon![..(end + 4)]);
                }
            }
            catch { }
        }
        return paths.ToList();
    }

    private static IReadOnlyList<(string Path, bool HasWindow)> RunningClients()
    {
        var running = new List<(string Path, bool HasWindow)>();
        if (!OperatingSystem.IsWindows()) return running;
        foreach (var process in Process.GetProcessesByName("AnyDesk"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null && IsSupportedVersion(FileVersionInfo.GetVersionInfo(path).FileVersion))
                        running.Add((path, process.MainWindowHandle != IntPtr.Zero));
                }
                catch { /* Inaccessible processes remain visible in environment warnings. */ }
            }
        }
        return running;
    }

    public static string ResolveExecutablePath(string preferredPath)
    {
        if (!OperatingSystem.IsWindows()) return preferredPath;
        var running = RunningClients();
        var active = running.OrderByDescending(client => client.HasWindow).FirstOrDefault();
        if (active.Path is not null) return active.Path;
        foreach (var path in InstalledPaths())
        {
            try
            {
                if (File.Exists(path) && IsSupportedVersion(FileVersionInfo.GetVersionInfo(path).FileVersion)) return path;
            }
            catch { }
        }
        return preferredPath;
    }
}
