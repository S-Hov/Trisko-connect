using Microsoft.Win32;

namespace ServiceLib.Geksa;

public enum GeksaAnyDeskNetworkState
{
    NotAvailable,
    Stopped,
    Connecting,
    Online,
    Offline,
    Error
}

public sealed record GeksaAnyDeskConnection(
    string Address,
    string DisplayName,
    bool IsFavorite,
    bool IsPinned = false)
{
    public string SecondaryText => IsFavorite ? TriskoLocalization.Format("{0} · Избранное", Address) : TriskoLocalization.Format("{0} · Недавнее подключение", Address);
    public string BadgeText => "ANYDESK";
    public string StatusText => IsFavorite ? TriskoLocalization.Text("ИЗБРАННОЕ") : TriskoLocalization.Text("НЕДАВНЕЕ");
    public string StatusColor => IsFavorite ? "#F3B35A" : "#8FA6C6";
}

public sealed record GeksaAnyDeskRuntimeStatus(
    GeksaAnyDeskNetworkState State,
    string StatusText,
    string StatusColor,
    string? Id,
    string? Alias)
{
    public string WorkstationAddressText => Alias.IsNotEmpty()
        ? $"{Alias} · ID {GeksaAnyDeskConfigurationParser.FormatId(Id)}"
        : Id.IsNotEmpty()
            ? $"ID {GeksaAnyDeskConfigurationParser.FormatId(Id)}"
            : TriskoLocalization.Text("ID ещё не получен");
}

public sealed record GeksaAnyDeskEnvironmentInspection(
    IReadOnlyList<string> UnsupportedInstallations,
    IReadOnlyList<string> ExternalProcesses)
{
    public bool HasWarnings => UnsupportedInstallations.Count > 0 || ExternalProcesses.Count > 0;

    public string WarningKindKey =>
        $"installed:{UnsupportedInstallations.Count > 0};external:{ExternalProcesses.Count > 0}";

    public string? WarningText
    {
        get
        {
            var warnings = new List<string>();
            if (UnsupportedInstallations.Count > 0)
            {
                warnings.Add(
                    TriskoLocalization.Text("Найдена установленная версия AnyDesk, отличная от 6.1.0: ") +
                    string.Join("; ", UnsupportedInstallations) + ".");
            }
            if (ExternalProcesses.Count > 0)
            {
                warnings.Add(
                    TriskoLocalization.Text("Обнаружен запущенный AnyDesk, который не был запущен Trisko connect: ") +
                    string.Join("; ", ExternalProcesses) + ".");
            }
            return warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings);
        }
    }
}

public interface IGeksaAnyDeskService
{
    Task<IReadOnlyList<GeksaAnyDeskConnection>> GetConnectionsAsync(CancellationToken cancellationToken);
    Task<GeksaAnyDeskRuntimeStatus> GetStatusAsync(string executablePath, CancellationToken cancellationToken);
    Task StartAsync(string executablePath, CancellationToken cancellationToken);
    Task ShowAsync(string executablePath, CancellationToken cancellationToken);
    Task ConnectAsync(string executablePath, string address, CancellationToken cancellationToken);
    Task<GeksaAnyDeskEnvironmentInspection> InspectEnvironmentAsync(
        string executablePath,
        CancellationToken cancellationToken);
    Task StopOwnedAsync(CancellationToken cancellationToken);
}

public static class GeksaAnyDeskConfigurationParser
{
    public static IReadOnlyList<GeksaAnyDeskConnection> ParseRoster(string? configuration)
    {
        var values = ParseValues(configuration);
        values.TryGetValue("ad.roster.items", out var roster);
        values.TryGetValue("ad.roster.favorites", out var favoritesText);
        var favorites = SplitRecords(favoritesText)
            .SelectMany(record => record.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<GeksaAnyDeskConnection>();
        foreach (var record in SplitRecords(roster))
        {
            var fields = record.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length == 0)
            {
                continue;
            }

            var address = fields.Length > 1 && IsSafeAddress(fields[1]) ? fields[1] : fields[0];
            if (!IsSafeAddress(address))
            {
                continue;
            }

            var name = fields.Length > 2 && fields[2].IsNotEmpty()
                ? fields[2]
                : fields[0].IsNotEmpty() && !string.Equals(fields[0], address, StringComparison.OrdinalIgnoreCase)
                    ? fields[0]
                    : FormatId(address);
            var isFavorite = favorites.Contains(address)
                || fields.Skip(2).Any(field => field.Contains("fav", StringComparison.OrdinalIgnoreCase));
            result.Add(new(address, name, isFavorite));
        }

        return result
            .GroupBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.IsFavorite)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static (string? Id, string? Alias) ParseIdentity(string? configuration)
    {
        var values = ParseValues(configuration);
        values.TryGetValue("ad.anynet.id", out var id);
        values.TryGetValue("ad.anynet.alias", out var alias);
        return (id?.Trim(), alias?.Trim());
    }

    public static bool IsSafeAddress(string? value)
    {
        var address = value?.Trim();
        return address.IsNotEmpty()
            && address!.Length <= 128
            && !address.StartsWith('-')
            && address.All(character => char.IsLetterOrDigit(character)
                || character is ' ' or '-' or '_' or '.' or '@');
    }

    public static string FormatId(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (!text.All(char.IsDigit) || text.Length < 7)
        {
            return text;
        }

        var groups = new List<string>();
        for (var end = text.Length; end > 0; end -= 3)
        {
            var start = Math.Max(0, end - 3);
            groups.Insert(0, text[start..end]);
        }
        return string.Join(' ', groups);
    }

    private static Dictionary<string, string> ParseValues(string? configuration)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (configuration ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static IEnumerable<string> SplitRecords(string? value) =>
        (value ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed class WindowsGeksaAnyDeskService : IGeksaAnyDeskService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(4);
    private readonly object _ownershipLock = new();
    private string? _ownedExecutablePath;
    private HashSet<int> _preexistingProcessIds = [];

    private static string UserConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnyDesk", "user.conf");

    private static string PortableSystemConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnyDesk", "system.conf");

    private static string InstalledSystemConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AnyDesk", "system.conf");

    public async Task<IReadOnlyList<GeksaAnyDeskConnection>> GetConnectionsAsync(CancellationToken cancellationToken)
    {
        var text = await ReadSharedTextAsync(UserConfigurationPath, cancellationToken);
        return GeksaAnyDeskConfigurationParser.ParseRoster(text);
    }

    public async Task<GeksaAnyDeskRuntimeStatus> GetStatusAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        var client = GeksaAnyDeskClientLocator.Inspect(GeksaAnyDeskClientLocator.ResolveExecutablePath(executablePath));
        if (!client.Exists)
        {
            return new(GeksaAnyDeskNetworkState.NotAvailable, TriskoLocalization.Text("AnyDesk не найден"), "#F0717E", null, null);
        }

        var identityText = client.IsInstalled
            ? await ReadSharedTextAsync(InstalledSystemConfigurationPath, cancellationToken)
                ?? await ReadSharedTextAsync(PortableSystemConfigurationPath, cancellationToken)
            : await ReadSharedTextAsync(PortableSystemConfigurationPath, cancellationToken)
                ?? await ReadSharedTextAsync(InstalledSystemConfigurationPath, cancellationToken);
        var identity = GeksaAnyDeskConfigurationParser.ParseIdentity(identityText);
        if (!IsAnyDeskRunning(client.Path))
        {
            return new(GeksaAnyDeskNetworkState.Stopped, TriskoLocalization.Text("AnyDesk не запущен"), "#8FA6C6", identity.Id, identity.Alias);
        }

        try
        {
            var id = identity.Id;
            // The selected running client is authoritative; configuration is a fallback.
            {
                var idOutput = (await RunCommandAsync(client.Path, "--get-id", cancellationToken)).Trim();
                if (idOutput.IsNotEmpty() && idOutput.All(char.IsDigit))
                {
                    id = idOutput;
                }
            }

            var output = await RunCommandAsync(client.Path, "--get-status", cancellationToken);
            var normalized = output.Trim().ToLowerInvariant();
            var state = normalized switch
            {
                var value when value.Contains("online") => GeksaAnyDeskNetworkState.Online,
                var value when value.Contains("connecting") => GeksaAnyDeskNetworkState.Connecting,
                var value when value.Contains("offline") => GeksaAnyDeskNetworkState.Offline,
                _ => GeksaAnyDeskNetworkState.Connecting
            };
            return state switch
            {
                GeksaAnyDeskNetworkState.Online => new(state, TriskoLocalization.Text("AnyDesk в сети"), "#3FD69A", id, identity.Alias),
                GeksaAnyDeskNetworkState.Offline => new(state, TriskoLocalization.Text("AnyDesk не подключён к сети"), "#F0717E", id, identity.Alias),
                _ => new(state, TriskoLocalization.Text("AnyDesk подключается…"), "#F3B35A", id, identity.Alias)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(GeksaAnyDeskNetworkState.Connecting, TriskoLocalization.Text("AnyDesk запускается…"), "#F3B35A", identity.Id, identity.Alias);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(WindowsGeksaAnyDeskService), ex);
            return new(GeksaAnyDeskNetworkState.Error, TriskoLocalization.Text("Статус AnyDesk недоступен"), "#F0717E", identity.Id, identity.Alias);
        }
    }

    public Task StartAsync(string executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = EnsureClient(executablePath);
        if (IsAnyDeskRunning(client.Path))
        {
            return Task.CompletedTask;
        }
        if (client.IsInstalled) StartProcess(client.Path, "--start", hidden: true);
        else StartOwnedProcess(client.Path, "--start", hidden: true);
        return Task.CompletedTask;
    }

    public Task ShowAsync(string executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = EnsureClient(executablePath);
        if (client.IsInstalled || IsAnyDeskRunning(client.Path))
        {
            StartProcess(client.Path, null, hidden: false);
        }
        else
        {
            StartOwnedProcess(client.Path, null, hidden: false);
        }
        return Task.CompletedTask;
    }

    public Task ConnectAsync(string executablePath, string address, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = EnsureClient(executablePath);
        if (!GeksaAnyDeskConfigurationParser.IsSafeAddress(address))
        {
            throw new InvalidOperationException(TriskoLocalization.Text("Укажите корректный AnyDesk ID или Alias."));
        }
        if (client.IsInstalled || IsAnyDeskRunning(client.Path))
        {
            StartProcess(client.Path, address.Trim(), hidden: false);
        }
        else
        {
            StartOwnedProcess(client.Path, address.Trim(), hidden: false);
        }
        return Task.CompletedTask;
    }

    public Task<GeksaAnyDeskEnvironmentInspection> InspectEnvironmentAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installations = FindUnsupportedInstalledAnyDeskClients();

        string? ownedExecutablePath;
        HashSet<int> preexistingProcessIds;
        lock (_ownershipLock)
        {
            ownedExecutablePath = _ownedExecutablePath;
            preexistingProcessIds = [.. _preexistingProcessIds];
        }

        var externalProcesses = new List<string>();
        foreach (var process in Process.GetProcessesByName("AnyDesk"))
        {
            try
            {
                var processPath = process.MainModule?.FileName;
                var isOwned = ownedExecutablePath.IsNotEmpty()
                    && processPath.IsNotEmpty()
                    && !preexistingProcessIds.Contains(process.Id)
                    && string.Equals(
                        Path.GetFullPath(processPath!),
                        Path.GetFullPath(ownedExecutablePath!),
                        StringComparison.OrdinalIgnoreCase);
                if (!isOwned && (processPath is null ||
                    !GeksaAnyDeskClientLocator.Inspect(processPath).IsSupportedVersion))
                {
                    externalProcesses.Add(processPath.IsNotEmpty()
                        ? $"PID {process.Id} ({processPath})"
                        : $"PID {process.Id}");
                }
            }
            catch
            {
                externalProcesses.Add($"PID {process.Id}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return Task.FromResult(new GeksaAnyDeskEnvironmentInspection(
            installations,
            externalProcesses.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
    }

    public async Task StopOwnedAsync(CancellationToken cancellationToken)
    {
        string? executablePath;
        lock (_ownershipLock)
        {
            executablePath = _ownedExecutablePath;
        }

        if (executablePath.IsNullOrEmpty())
        {
            return;
        }

        HashSet<int> preexisting;
        lock (_ownershipLock) preexisting = [.. _preexistingProcessIds];
        var processes = FindProcesses(executablePath!);
        foreach (var process in processes.Where(process => preexisting.Contains(process.Id)).ToList())
        {
            processes.Remove(process);
            process.Dispose();
        }
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.CloseMainWindow();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(StopOwnedAsync), ex);
                }
            }

            if (processes.Any(process => !process.HasExited))
            {
                await Task.Delay(500, cancellationToken);
            }

            foreach (var process in processes.Where(process => !process.HasExited))
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }

            lock (_ownershipLock)
            {
                if (string.Equals(_ownedExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    _ownedExecutablePath = null;
                    _preexistingProcessIds.Clear();
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static GeksaAnyDeskClientInfo EnsureClient(string path)
    {
        var client = GeksaAnyDeskClientLocator.Inspect(GeksaAnyDeskClientLocator.ResolveExecutablePath(path));
        if (!client.Exists)
        {
            throw new FileNotFoundException(TriskoLocalization.Text("Portable-клиент AnyDesk не найден."), client.Path);
        }
        return client;
    }

    private static bool IsAnyDeskRunning(string executablePath)
    {
        var processes = FindProcesses(executablePath);
        foreach (var process in processes)
        {
            process.Dispose();
        }
        return processes.Count > 0;
    }

    private void StartOwnedProcess(string executablePath, string? argument, bool hidden)
    {
        var preexistingProcessIds = GetProcessIdsByName(executablePath);
        StartProcess(executablePath, argument, hidden);
        lock (_ownershipLock)
        {
            _ownedExecutablePath = Path.GetFullPath(executablePath);
            _preexistingProcessIds = preexistingProcessIds;
        }
    }

    private static HashSet<int> GetProcessIdsByName(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        var processIds = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                processIds.Add(process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }
        return processIds;
    }

    private static IReadOnlyList<string> FindUnsupportedInstalledAnyDeskClients()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDefaultInstallationPath(paths, Environment.SpecialFolder.ProgramFiles);
        AddDefaultInstallationPath(paths, Environment.SpecialFolder.ProgramFilesX86);

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    foreach (var subKeyName in uninstall?.GetSubKeyNames() ?? [])
                    {
                        using var subKey = uninstall!.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName") as string;
                        if (displayName?.Contains("AnyDesk", StringComparison.OrdinalIgnoreCase) != true)
                        {
                            continue;
                        }

                        var displayVersion = subKey?.GetValue("DisplayVersion") as string;
                        if (!GeksaAnyDeskClientLocator.IsSupportedVersion(displayVersion))
                        {
                            warnings.Add(TriskoLocalization.Format("{0} ({1})", displayVersion ?? TriskoLocalization.Text("неизвестная версия"), displayName));
                        }

                        AddCandidatePath(paths, subKey?.GetValue("DisplayIcon") as string);
                        var installLocation = subKey?.GetValue("InstallLocation") as string;
                        if (installLocation.IsNotEmpty())
                        {
                            AddCandidatePath(paths, Path.Combine(installLocation!, "AnyDesk.exe"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(FindUnsupportedInstalledAnyDeskClients), ex);
                }
            }
        }

        foreach (var client in paths.Where(File.Exists).Select(GeksaAnyDeskClientLocator.Inspect))
        {
            if (!client.IsSupportedVersion)
            {
                warnings.Add(TriskoLocalization.Format("{0} ({1})", client.Version ?? TriskoLocalization.Text("неизвестная версия"), client.Path));
            }
        }
        return warnings.ToList();
    }

    private static void AddDefaultInstallationPath(HashSet<string> paths, Environment.SpecialFolder folder)
    {
        var root = Environment.GetFolderPath(folder);
        if (root.IsNotEmpty())
        {
            paths.Add(Path.Combine(root, "AnyDesk", "AnyDesk.exe"));
        }
    }

    private static void AddCandidatePath(HashSet<string> paths, string? rawPath)
    {
        var path = rawPath?.Trim().Trim('"');
        if (path.IsNullOrEmpty())
        {
            return;
        }
        var executableSuffix = path!.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (executableSuffix >= 0)
        {
            path = path[..(executableSuffix + 4)];
        }
        paths.Add(path);
    }

    private static void StartProcess(string executablePath, string? argument, bool hidden)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
            CreateNoWindow = hidden,
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };
        if (argument.IsNotEmpty())
        {
            startInfo.ArgumentList.Add(argument!);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(TriskoLocalization.Text("AnyDesk не удалось запустить."));
    }

    private static List<Process> FindProcesses(string executablePath)
    {
        var expected = Path.GetFullPath(executablePath);
        var result = new List<Process>();
        var processName = Path.GetFileNameWithoutExtension(expected);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.MainModule?.FileName is { } path
                    && string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(process);
                    continue;
                }
            }
            catch
            {
                // A process owned by another user is irrelevant for this portable client.
            }
            process.Dispose();
        }
        return result;
    }

    private static async Task<string> RunCommandAsync(
        string executablePath,
        string argument,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(TriskoLocalization.Text("AnyDesk не удалось запустить для проверки статуса."));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return await outputTask;
    }

    private static async Task<string?> ReadSharedTextAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
