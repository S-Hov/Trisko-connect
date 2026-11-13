namespace ServiceLib.Geksa;

/// <summary>
/// Performs a read-only startup inventory and repairs only Geksa-owned local
/// port configuration. It never stops or reconfigures third-party processes,
/// services, adapters or VPN clients.
/// </summary>
public sealed class GeksaStartupDiagnosticsService
{
    private static readonly int[] RequiredPortOffsets = [0, 1, 2, 3, 4, 5, 6, 21];
    private static readonly string[] RelevantProcessNames = ["Trisko.Connect", "xray", "sing-box", "v2rayN"];
    private const int MinimumAutomaticPort = 10_000;
    private const int MaximumAutomaticBasePort = 48_000;

    private readonly Func<IReadOnlyList<GeksaPortOwner>> _portOwnerProvider;
    private readonly Func<IReadOnlyList<GeksaProcessObservation>> _processProvider;
    private readonly Func<bool> _administratorProvider;
    private readonly Func<Config, Task<int>> _configSaver;

    public GeksaStartupDiagnosticsService()
        : this(
            GetWindowsPortOwners,
            GetRelevantProcesses,
            () => !Utils.IsWindows() || Utils.IsAdministrator(),
            ConfigHandler.SaveConfig)
    {
    }

    public GeksaStartupDiagnosticsService(
        Func<IReadOnlyList<GeksaPortOwner>> portOwnerProvider,
        Func<IReadOnlyList<GeksaProcessObservation>> processProvider,
        Func<bool> administratorProvider,
        Func<Config, Task<int>> configSaver)
    {
        _portOwnerProvider = portOwnerProvider ?? throw new ArgumentNullException(nameof(portOwnerProvider));
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        _administratorProvider = administratorProvider ?? throw new ArgumentNullException(nameof(administratorProvider));
        _configSaver = configSaver ?? throw new ArgumentNullException(nameof(configSaver));
    }

    public static GeksaStartupDiagnosticsService Instance { get; } = new();

    public GeksaStartupDiagnosticsReport LastReport { get; private set; } =
        GeksaStartupDiagnosticsReport.NotRun;

    public async Task<GeksaStartupDiagnosticsReport> InspectAndRepairAsync(
        Config config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<GeksaStartupDiagnosticIssue>();
        var isAdministrator = _administratorProvider();
        if (!isAdministrator)
        {
            issues.Add(new(
                GeksaStartupDiagnosticSeverity.Error,
                "administratorRequired",
                TriskoLocalization.Text("Программа запущена без прав администратора. TUN не сможет работать."),
                AutoFixed: false));
        }

        config.Inbound ??= [];
        if (config.Inbound.Count == 0)
        {
            config.Inbound.Add(new InItem
            {
                Protocol = nameof(EInboundProtocol.socks),
                LocalPort = 10808,
                UdpEnabled = true,
                SniffingEnabled = true,
            });
        }

        var configuredPort = config.Inbound.First().LocalPort;
        var effectivePort = configuredPort;
        IReadOnlyList<GeksaPortOwner> portOwners;
        try
        {
            portOwners = _portOwnerProvider();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaStartupDiagnosticsService), ex);
            portOwners = [];
            issues.Add(new(
                GeksaStartupDiagnosticSeverity.Warning,
                "portInventoryFailed",
                TriskoLocalization.Text("Не удалось получить полный список владельцев локальных портов."),
                AutoFixed: false));
        }

        var conflictingOwners = GetConflictingOwners(configuredPort, portOwners);
        if (conflictingOwners.Count > 0)
        {
            var freeBasePort = FindAvailableBasePort(configuredPort, portOwners.Select(owner => owner.Port));
            if (freeBasePort is null)
            {
                issues.Add(new(
                    GeksaStartupDiagnosticSeverity.Error,
                    "localPortRangeUnavailable",
                    TriskoLocalization.Format("Локальный диапазон от порта {0} занят, а свободный диапазон найти не удалось.", configuredPort),
                    AutoFixed: false));
            }
            else
            {
                effectivePort = freeBasePort.Value;
                var inbound = config.Inbound.First();
                inbound.LocalPort = effectivePort;
                if (await _configSaver(config) == 0)
                {
                    var ownersText = string.Join(", ", conflictingOwners
                        .Select(owner => owner.OwnerDisplayName)
                        .Distinct(StringComparer.OrdinalIgnoreCase));
                    issues.Add(new(
                        GeksaStartupDiagnosticSeverity.Info,
                        "localPortRangeChanged",
                        TriskoLocalization.Format("Порт {0} был занят: {1}. ", configuredPort, ownersText) +
                        TriskoLocalization.Format("Trisko автоматически выбрала свободный диапазон {0}–{1}. ", effectivePort, effectivePort + RequiredPortOffsets.Max()) +
                        TriskoLocalization.Text("Сторонние процессы не изменялись."),
                        AutoFixed: true));
                }
                else
                {
                    inbound.LocalPort = configuredPort;
                    effectivePort = configuredPort;
                    issues.Add(new(
                        GeksaStartupDiagnosticSeverity.Error,
                        "localPortSaveFailed",
                        TriskoLocalization.Text("Найден свободный локальный порт, но сохранить его в настройках не удалось."),
                        AutoFixed: false));
                }
            }
        }

        try
        {
            foreach (var process in _processProvider()
                         .Where(process => process.ProcessId != Environment.ProcessId))
            {
                var isAnotherTrisko = process.ProcessName.Equals(
                    "Trisko.Connect",
                    StringComparison.OrdinalIgnoreCase);
                issues.Add(new(
                    isAnotherTrisko
                        ? GeksaStartupDiagnosticSeverity.Warning
                        : GeksaStartupDiagnosticSeverity.Info,
                    isAnotherTrisko ? "anotherGeksaInstance" : "externalVpnCoreDetected",
                    isAnotherTrisko
                        ? TriskoLocalization.Format("Обнаружена другая копия Trisko connect (PID {0}). ", process.ProcessId) +
                          TriskoLocalization.Text("Не подключайте две копии одновременно.")
                        : TriskoLocalization.Format("Обнаружен внешний процесс {0}. Он не остановлен и не изменён.", process.DisplayName),
                    AutoFixed: false));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(GeksaStartupDiagnosticsService), ex);
            issues.Add(new(
                GeksaStartupDiagnosticSeverity.Warning,
                "processInventoryFailed",
                TriskoLocalization.Text("Не удалось получить полный список VPN-процессов и служб."),
                AutoFixed: false));
        }

        var report = new GeksaStartupDiagnosticsReport(
            isAdministrator,
            configuredPort,
            effectivePort,
            issues,
            DateTimeOffset.UtcNow);
        LastReport = report;
        Logging.SaveLog($"Trisko startup diagnostics: {report.LogSummary}");
        return report;
    }

    public static int? FindAvailableBasePort(int configuredPort, IEnumerable<int> occupiedPorts)
    {
        var occupied = occupiedPorts.ToHashSet();
        if (IsPortRangeAvailable(configuredPort, occupied))
        {
            return configuredPort;
        }

        var firstCandidate = Math.Max(MinimumAutomaticPort, configuredPort + 32);
        for (var candidate = firstCandidate; candidate <= MaximumAutomaticBasePort; candidate += 32)
        {
            if (IsPortRangeAvailable(candidate, occupied))
            {
                return candidate;
            }
        }

        for (var candidate = MinimumAutomaticPort; candidate < firstCandidate; candidate += 32)
        {
            if (IsPortRangeAvailable(candidate, occupied))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static bool IsTcpListenerOwnedByProcess(int port, int processId)
    {
        if (!Utils.IsWindows())
        {
            return true;
        }

        try
        {
            return WindowsIpHelper.GetTcpListeners().Any(owner =>
                owner.Port == port && owner.ProcessId == processId);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(IsTcpListenerOwnedByProcess), ex);
            return false;
        }
    }

    private static bool IsPortRangeAvailable(int basePort, IReadOnlySet<int> occupiedPorts)
    {
        return basePort >= 1024
               && basePort + RequiredPortOffsets.Max() < Global.MaxPort
               && RequiredPortOffsets.All(offset => !occupiedPorts.Contains(basePort + offset));
    }

    private static IReadOnlyList<GeksaPortOwner> GetConflictingOwners(
        int basePort,
        IReadOnlyList<GeksaPortOwner> owners)
    {
        var requiredPorts = RequiredPortOffsets.Select(offset => basePort + offset).ToHashSet();
        return owners
            .Where(owner => requiredPorts.Contains(owner.Port))
            .OrderBy(owner => owner.Port)
            .ThenBy(owner => owner.ProcessId)
            .ToArray();
    }

    private static IReadOnlyList<GeksaProcessObservation> GetRelevantProcesses()
    {
        var observations = new List<GeksaProcessObservation>();
        foreach (var processName in RelevantProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        observations.Add(new(
                            process.ProcessName,
                            process.Id,
                            IsLikelyServiceProcess(process),
                            FormatProcessDisplayName(process.ProcessName, process.Id, IsLikelyServiceProcess(process))));
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited between enumeration and inspection.
                    }
                }
            }
        }
        return observations;
    }

    private static IReadOnlyList<GeksaPortOwner> GetWindowsPortOwners()
    {
        if (!Utils.IsWindows())
        {
            return [];
        }

        var owners = new List<GeksaPortOwner>();
        owners.AddRange(WindowsIpHelper.GetTcpListeners());
        owners.AddRange(WindowsIpHelper.GetUdpListeners());
        return owners
            .DistinctBy(owner => new { owner.Protocol, owner.Port, owner.ProcessId })
            .ToArray();
    }

    private static GeksaPortOwner CreatePortOwner(string protocol, int port, int processId)
    {
        var processName = TriskoLocalization.Text("неизвестный процесс");
        var isService = false;
        try
        {
            using var process = Process.GetProcessById(processId);
            processName = process.ProcessName;
            isService = IsLikelyServiceProcess(process);
        }
        catch
        {
            // The process may have already exited or be protected.
        }

        return new GeksaPortOwner(
            protocol,
            port,
            processId,
            processName,
            isService,
            FormatProcessDisplayName(processName, processId, isService));
    }

    private static bool IsLikelyServiceProcess(Process process)
    {
        try
        {
            return process.SessionId == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatProcessDisplayName(string processName, int processId, bool isService)
    {
        return isService
            ? TriskoLocalization.Format("{0} (PID {1}, системная служба)", processName, processId)
            : $"{processName} (PID {processId})";
    }

    private static class WindowsIpHelper
    {
        private const int AfInet = 2;
        private const uint ErrorInsufficientBuffer = 122;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr table,
            ref int tableLength,
            bool sort,
            int ipVersion,
            TcpTableClass tableClass,
            uint reserved = 0);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr table,
            ref int tableLength,
            bool sort,
            int ipVersion,
            UdpTableClass tableClass,
            uint reserved = 0);

        public static IReadOnlyList<GeksaPortOwner> GetTcpListeners()
        {
            var tableLength = 0;
            var result = GetExtendedTcpTable(
                IntPtr.Zero,
                ref tableLength,
                sort: false,
                AfInet,
                TcpTableClass.OwnerPidListener);
            if (result != ErrorInsufficientBuffer || tableLength <= 0)
            {
                return [];
            }

            var buffer = Marshal.AllocHGlobal(tableLength);
            try
            {
                result = GetExtendedTcpTable(
                    buffer,
                    ref tableLength,
                    sort: false,
                    AfInet,
                    TcpTableClass.OwnerPidListener);
                if (result != 0)
                {
                    return [];
                }

                var count = Marshal.ReadInt32(buffer);
                var rowPointer = IntPtr.Add(buffer, sizeof(int));
                var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
                var owners = new List<GeksaPortOwner>(count);
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rowPointer);
                    owners.Add(CreatePortOwner(
                        "TCP",
                        ConvertPort(row.LocalPort),
                        checked((int)row.OwningPid)));
                    rowPointer = IntPtr.Add(rowPointer, rowSize);
                }
                return owners;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static IReadOnlyList<GeksaPortOwner> GetUdpListeners()
        {
            var tableLength = 0;
            var result = GetExtendedUdpTable(
                IntPtr.Zero,
                ref tableLength,
                sort: false,
                AfInet,
                UdpTableClass.OwnerPid);
            if (result != ErrorInsufficientBuffer || tableLength <= 0)
            {
                return [];
            }

            var buffer = Marshal.AllocHGlobal(tableLength);
            try
            {
                result = GetExtendedUdpTable(
                    buffer,
                    ref tableLength,
                    sort: false,
                    AfInet,
                    UdpTableClass.OwnerPid);
                if (result != 0)
                {
                    return [];
                }

                var count = Marshal.ReadInt32(buffer);
                var rowPointer = IntPtr.Add(buffer, sizeof(int));
                var rowSize = Marshal.SizeOf<UdpRowOwnerPid>();
                var owners = new List<GeksaPortOwner>(count);
                for (var index = 0; index < count; index++)
                {
                    var row = Marshal.PtrToStructure<UdpRowOwnerPid>(rowPointer);
                    owners.Add(CreatePortOwner(
                        "UDP",
                        ConvertPort(row.LocalPort),
                        checked((int)row.OwningPid)));
                    rowPointer = IntPtr.Add(rowPointer, rowSize);
                }
                return owners;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static int ConvertPort(uint networkPort)
        {
            return unchecked((ushort)IPAddress.NetworkToHostOrder((short)networkPort));
        }

        private enum TcpTableClass
        {
            OwnerPidListener = 3
        }

        private enum UdpTableClass
        {
            OwnerPid = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TcpRowOwnerPid
        {
            public uint State;
            public uint LocalAddress;
            public uint LocalPort;
            public uint RemoteAddress;
            public uint RemotePort;
            public uint OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UdpRowOwnerPid
        {
            public uint LocalAddress;
            public uint LocalPort;
            public uint OwningPid;
        }
    }
}

public enum GeksaStartupDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record GeksaStartupDiagnosticIssue(
    GeksaStartupDiagnosticSeverity Severity,
    string Code,
    string Message,
    bool AutoFixed);

public sealed record GeksaStartupDiagnosticsReport(
    bool IsAdministrator,
    int ConfiguredPort,
    int EffectivePort,
    IReadOnlyList<GeksaStartupDiagnosticIssue> Issues,
    DateTimeOffset CheckedAtUtc)
{
    public static GeksaStartupDiagnosticsReport NotRun { get; } = new(
        IsAdministrator: false,
        ConfiguredPort: 0,
        EffectivePort: 0,
        Issues: [],
        CheckedAtUtc: DateTimeOffset.MinValue);

    public bool HasErrors => Issues.Any(issue => issue.Severity == GeksaStartupDiagnosticSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == GeksaStartupDiagnosticSeverity.Warning);

    public bool HasAutomaticFixes => Issues.Any(issue => issue.AutoFixed);

    public string DisplayTitle => HasErrors
        ? TriskoLocalization.Text("Проверка запуска: требуется внимание")
        : HasWarnings
            ? TriskoLocalization.Text("Проверка запуска: есть предупреждения")
            : HasAutomaticFixes
                ? TriskoLocalization.Text("Проверка запуска: конфликт исправлен")
                : TriskoLocalization.Text("Проверка запуска: всё готово");

    public string DisplayMessage
    {
        get
        {
            if (Issues.Count == 0)
            {
                return TriskoLocalization.Format("Права администратора подтверждены. Локальный диапазон от порта {0} свободен.", EffectivePort);
            }

            return string.Join(Environment.NewLine, Issues.Select(issue => $"• {issue.Message}"));
        }
    }

    public string StatusColor => HasErrors
        ? "#F0717E"
        : HasWarnings
            ? "#F3B35A"
            : HasAutomaticFixes
                ? "#6F9CFF"
                : "#3FD69A";

    public string LogSummary =>
        $"admin={IsAdministrator}, configuredPort={ConfiguredPort}, effectivePort={EffectivePort}, " +
        $"issues=[{string.Join("; ", Issues.Select(issue => $"{issue.Code}:{issue.Message}"))}]";
}

public sealed record GeksaPortOwner(
    string Protocol,
    int Port,
    int ProcessId,
    string ProcessName,
    bool IsLikelyService,
    string OwnerDisplayName);

public sealed record GeksaProcessObservation(
    string ProcessName,
    int ProcessId,
    bool IsLikelyService,
    string DisplayName);
