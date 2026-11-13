namespace ServiceLib.Trisko;

public enum TriskoOneCSetupState
{
    NotConfigured,
    Ready,
    Warning,
    Error
}

public sealed record TriskoOneCSystemTarget(string ServerName, string ServerAddress);

public sealed record TriskoOneCSystemInspection(
    TriskoOneCSetupState State,
    string Message,
    IReadOnlyList<string> RelatedEntries,
    IReadOnlyList<string> ConflictingEntries,
    bool HasManagedBlock,
    bool HasManagedMapping)
{
    public bool HasConflicts => ConflictingEntries.Count > 0;
}

public interface ITriskoOneCSystemService
{
    string HostsFilePath { get; }

    Task<TriskoOneCSystemInspection> InspectAsync(
        TriskoOneCSystemTarget target,
        CancellationToken cancellationToken = default);

    Task<TriskoOneCSystemInspection> ConfigureAsync(
        TriskoOneCSystemTarget target,
        bool replaceConflictingEntries,
        CancellationToken cancellationToken = default);

    Task<TriskoOneCSystemInspection> RemoveManagedConfigurationAsync(
        TriskoOneCSystemTarget target,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsTriskoOneCSystemService : ITriskoOneCSystemService
{
    private const int MaxHostsFileBytes = 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowsTriskoOneCSystemService(string? hostsFilePath = null)
    {
        HostsFilePath = hostsFilePath
            ?? Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
    }

    public string HostsFilePath { get; }

    public async Task<TriskoOneCSystemInspection> InspectAsync(
        TriskoOneCSystemTarget target,
        CancellationToken cancellationToken = default)
    {
        var normalized = TriskoOneCSystemTargetValidator.Normalize(target);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await ReadHostsAsync(cancellationToken);
            return TriskoHostsDocument.Inspect(snapshot.Content, normalized);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(WindowsTriskoOneCSystemService), ex);
            return ErrorInspection(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TriskoOneCSystemInspection> ConfigureAsync(
        TriskoOneCSystemTarget target,
        bool replaceConflictingEntries,
        CancellationToken cancellationToken = default)
    {
        var normalized = TriskoOneCSystemTargetValidator.Normalize(target);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await ReadHostsAsync(cancellationToken);
            var next = TriskoHostsDocument.Configure(
                snapshot.Content,
                normalized,
                replaceConflictingEntries);
            if (!string.Equals(snapshot.Content, next, StringComparison.Ordinal))
            {
                await WriteHostsAtomicallyAsync(next, snapshot, cancellationToken);
            }
            FlushWindowsDnsCache();
            return TriskoHostsDocument.Inspect(next, normalized);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(WindowsTriskoOneCSystemService), ex);
            return ErrorInspection(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TriskoOneCSystemInspection> RemoveManagedConfigurationAsync(
        TriskoOneCSystemTarget target,
        CancellationToken cancellationToken = default)
    {
        var normalized = TriskoOneCSystemTargetValidator.Normalize(target);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await ReadHostsAsync(cancellationToken);
            var next = TriskoHostsDocument.RemoveManagedBlock(snapshot.Content);
            if (!string.Equals(snapshot.Content, next, StringComparison.Ordinal))
            {
                await WriteHostsAtomicallyAsync(next, snapshot, cancellationToken);
            }
            FlushWindowsDnsCache();
            return TriskoHostsDocument.Inspect(next, normalized);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(WindowsTriskoOneCSystemService), ex);
            return ErrorInspection(ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HostsFileSnapshot> ReadHostsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(HostsFilePath))
        {
            throw new FileNotFoundException(TriskoLocalization.Text("Файл hosts Windows не найден."), HostsFilePath);
        }

        var file = new FileInfo(HostsFilePath);
        if (file.Length > MaxHostsFileBytes)
        {
            throw new InvalidDataException(TriskoLocalization.Text("Файл hosts слишком большой для безопасного изменения."));
        }

        var bytes = await File.ReadAllBytesAsync(HostsFilePath, cancellationToken);
        return DecodeHostsFile(bytes);
    }

    private async Task WriteHostsAtomicallyAsync(
        string content,
        HostsFileSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(HostsFilePath)
            ?? throw new InvalidOperationException(TriskoLocalization.Text("Не удалось определить каталог файла hosts."));
        var tempPath = Path.Combine(
            directory,
            $"hosts.trisko-{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, "hosts.trisko-backup");

        try
        {
            var contentBytes = snapshot.Encoding.GetBytes(content);
            var bytes = new byte[snapshot.Preamble.Length + contentBytes.Length];
            snapshot.Preamble.CopyTo(bytes, 0);
            contentBytes.CopyTo(bytes, snapshot.Preamble.Length);
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            File.Replace(tempPath, HostsFilePath, backupPath, ignoreMetadataErrors: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    internal static HostsFileSnapshot DecodeHostsFile(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (StartsWith(bytes, [0x00, 0x00, 0xFE, 0xFF]))
        {
            return Decode(bytes, new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
        }
        if (StartsWith(bytes, [0xFF, 0xFE, 0x00, 0x00]))
        {
            return Decode(bytes, new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
        }
        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
        {
            return Decode(bytes, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 3);
        }
        if (StartsWith(bytes, [0xFE, 0xFF]))
        {
            return Decode(bytes, new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        }
        if (StartsWith(bytes, [0xFF, 0xFE]))
        {
            return Decode(bytes, new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2);
        }

        try
        {
            return new(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(bytes),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                []);
        }
        catch (DecoderFallbackException)
        {
            // Hosts entries are ASCII. Latin-1 provides a lossless byte-to-character
            // round trip for legacy ANSI comments without guessing a machine code page.
            return new(Encoding.Latin1.GetString(bytes), Encoding.Latin1, []);
        }
    }

    private static HostsFileSnapshot Decode(byte[] bytes, Encoding encoding, int preambleLength) =>
        new(
            encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength),
            encoding,
            bytes[..preambleLength]);

    private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> prefix) =>
        bytes.AsSpan().StartsWith(prefix);

    private static TriskoOneCSystemInspection ErrorInspection(Exception ex)
    {
        var message = ex switch
        {
            UnauthorizedAccessException =>
                TriskoLocalization.Text("Нет доступа к файлу hosts. Перезапустите программу от имени администратора."),
            IOException => TriskoLocalization.Format("Не удалось прочитать или изменить hosts: {0}", ex.Message),
            _ => ex.Message
        };
        return new(
            TriskoOneCSetupState.Error,
            message,
            [],
            [],
            false,
            false);
    }

    private static void FlushWindowsDnsCache()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = DnsFlushResolverCache();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(FlushWindowsDnsCache), ex);
        }
    }

    [DllImport("dnsapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DnsFlushResolverCache();

    internal sealed record HostsFileSnapshot(
        string Content,
        Encoding Encoding,
        byte[] Preamble);
}

internal static class TriskoOneCSystemTargetValidator
{
    private static readonly Regex HostNamePattern = new(
        @"^(?=.{1,253}\.?$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static TriskoOneCSystemTarget Normalize(TriskoOneCSystemTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var serverName = target.ServerName?.Trim().TrimEnd('.').ToLowerInvariant();
        if (serverName.IsNullOrEmpty() || !HostNamePattern.IsMatch(serverName))
        {
            throw new ArgumentException(
                TriskoLocalization.Text("Введите корректное имя сервера, например files.company.local."),
                nameof(target));
        }

        if (!IPAddress.TryParse(target.ServerAddress?.Trim(), out var address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                TriskoLocalization.Text("Введите корректный IPv4-адрес сервера, например 192.168.3.222."),
                nameof(target));
        }

        return new(serverName, address.ToString());
    }
}

internal static class TriskoHostsDocument
{
    internal const string BeginMarker = "# BEGIN Trisko connect managed 1C";
    internal const string EndMarker = "# END Trisko connect managed 1C";

    public static TriskoOneCSystemInspection Inspect(
        string content,
        TriskoOneCSystemTarget target)
    {
        var document = Parse(content, target);
        if (document.Error is not null)
        {
            return new(
                TriskoOneCSetupState.Error,
                document.Error,
                document.RelatedEntries,
                document.ConflictingEntries,
                document.HasManagedBlock,
                false);
        }

        if (document.HasExactManagedMapping && document.ConflictingEntries.Count == 0)
        {
            return new(
                TriskoOneCSetupState.Ready,
                TriskoLocalization.Format("Windows переводит {0} в {1}. Маршрут 1С подготовлен.", target.ServerName, target.ServerAddress),
                document.RelatedEntries,
                document.ConflictingEntries,
                true,
                true);
        }

        if (document.ConflictingEntries.Count > 0)
        {
            return new(
                TriskoOneCSetupState.Warning,
                TriskoLocalization.Text("В hosts найдены другие строки с выбранным именем или IP. Перед заменой требуется подтверждение."),
                document.RelatedEntries,
                document.ConflictingEntries,
                document.HasManagedBlock,
                document.HasExactManagedMapping);
        }

        return new(
            TriskoOneCSetupState.NotConfigured,
            document.HasManagedBlock
                ? TriskoLocalization.Text("Управляемый блок 1С есть, но его значения отличаются от выбранных. Выполните настройку заново.")
                : TriskoLocalization.Text("Система для работы с 1С ещё не настроена."),
            document.RelatedEntries,
            document.ConflictingEntries,
            document.HasManagedBlock,
            false);
    }

    public static string Configure(
        string content,
        TriskoOneCSystemTarget target,
        bool replaceConflictingEntries)
    {
        var document = Parse(content, target);
        if (document.Error is not null)
        {
            throw new InvalidDataException(document.Error);
        }
        if (document.ConflictingLineNumbers.Count > 0 && !replaceConflictingEntries)
        {
            throw new InvalidOperationException(
                TriskoLocalization.Text("В hosts есть конфликтующие записи. Для их замены требуется подтверждение."));
        }

        var lines = SplitLines(content);
        var kept = new List<string>(lines.Count + 4);
        var insideManagedBlock = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (IsMarker(trimmed, BeginMarker))
            {
                insideManagedBlock = true;
                continue;
            }
            if (IsMarker(trimmed, EndMarker))
            {
                insideManagedBlock = false;
                continue;
            }
            if (insideManagedBlock)
            {
                continue;
            }
            if (replaceConflictingEntries
                && document.ConflictingLineNumbers.Contains(index + 1))
            {
                continue;
            }
            kept.Add(lines[index]);
        }

        TrimTrailingEmptyLines(kept);
        if (kept.Count > 0)
        {
            kept.Add(string.Empty);
        }
        kept.Add(BeginMarker);
        kept.Add($"{target.ServerAddress}\t{target.ServerName}");
        kept.Add(EndMarker);

        var newline = DetectNewline(content);
        return string.Join(newline, kept) + newline;
    }

    public static string RemoveManagedBlock(string content)
    {
        var document = Parse(content, null);
        if (document.Error is not null)
        {
            throw new InvalidDataException(document.Error);
        }
        if (!document.HasManagedBlock)
        {
            return content;
        }

        var lines = SplitLines(content);
        var kept = new List<string>(lines.Count);
        var insideManagedBlock = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (IsMarker(trimmed, BeginMarker))
            {
                insideManagedBlock = true;
                if (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
                {
                    kept.RemoveAt(kept.Count - 1);
                }
                continue;
            }
            if (IsMarker(trimmed, EndMarker))
            {
                insideManagedBlock = false;
                continue;
            }
            if (!insideManagedBlock)
            {
                kept.Add(line);
            }
        }

        TrimTrailingEmptyLines(kept);
        return kept.Count == 0
            ? string.Empty
            : string.Join(DetectNewline(content), kept) + DetectNewline(content);
    }

    private static ParsedDocument Parse(
        string content,
        TriskoOneCSystemTarget? target)
    {
        var lines = SplitLines(content);
        var related = new List<string>();
        var conflicts = new List<string>();
        var conflictingLines = new HashSet<int>();
        var managedDataLines = new List<HostsEntry>();
        var managedNonDataLines = 0;
        var insideManagedBlock = false;
        var managedBlockCount = 0;
        string? error = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (IsMarker(trimmed, BeginMarker))
            {
                if (insideManagedBlock || managedBlockCount > 0)
                {
                    error = TriskoLocalization.Text("В hosts найден повреждённый или повторяющийся управляемый блок 1С.");
                    break;
                }
                insideManagedBlock = true;
                managedBlockCount++;
                continue;
            }
            if (IsMarker(trimmed, EndMarker))
            {
                if (!insideManagedBlock)
                {
                    error = TriskoLocalization.Text("В hosts найден конец управляемого блока 1С без его начала.");
                    break;
                }
                insideManagedBlock = false;
                continue;
            }

            var entry = TryParseEntry(line);
            if (insideManagedBlock)
            {
                if (entry is not null)
                {
                    managedDataLines.Add(entry);
                    related.Add(FormatLine(index + 1, line, owned: true));
                }
                else if (!trimmed.IsNullOrEmpty() && !trimmed.StartsWith('#'))
                {
                    managedNonDataLines++;
                    related.Add(FormatLine(index + 1, line, owned: true));
                }
                continue;
            }

            if (entry is not null && target is not null && Matches(entry, target))
            {
                var formatted = FormatLine(index + 1, line, owned: false);
                related.Add(formatted);
                conflicts.Add(formatted);
                conflictingLines.Add(index + 1);
            }
        }

        if (error is null && insideManagedBlock)
        {
            error = TriskoLocalization.Text("В hosts не найден конец управляемого блока 1С.");
        }

        var hasExactManagedMapping = target is not null
                                     && managedBlockCount == 1
                                     && managedNonDataLines == 0
                                     && managedDataLines.Count == 1
                                     && IsExactMapping(managedDataLines[0], target);
        return new(
            managedBlockCount > 0,
            hasExactManagedMapping,
            related,
            conflicts,
            conflictingLines,
            error);
    }

    private static HostsEntry? TryParseEntry(string line)
    {
        var data = line.Split('#', 2)[0].Trim();
        if (data.IsNullOrEmpty())
        {
            return null;
        }

        var parts = Regex.Split(data, @"\s+")
            .Where(part => !part.IsNullOrEmpty())
            .ToArray();
        if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out var address))
        {
            return null;
        }

        return new(address, parts.Skip(1).ToArray());
    }

    private static bool Matches(HostsEntry entry, TriskoOneCSystemTarget target)
    {
        var targetAddress = IPAddress.Parse(target.ServerAddress);
        return entry.Address.Equals(targetAddress)
               || entry.HostNames.Contains(target.ServerName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsExactMapping(HostsEntry entry, TriskoOneCSystemTarget target)
    {
        return entry.Address.Equals(IPAddress.Parse(target.ServerAddress))
               && entry.HostNames.Count == 1
               && string.Equals(
                   entry.HostNames[0],
                   target.ServerName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarker(string value, string marker) =>
        string.Equals(value, marker, StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    private static string DetectNewline(string content) =>
        content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static void TrimTrailingEmptyLines(List<string> lines)
    {
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static string FormatLine(int lineNumber, string line, bool owned) =>
        TriskoLocalization.Format("Строка {0}: {1}", lineNumber, line.Trim()) + (owned ? TriskoLocalization.Text("  [управляет Trisko]") : string.Empty);

    private sealed record HostsEntry(IPAddress Address, IReadOnlyList<string> HostNames);

    private sealed record ParsedDocument(
        bool HasManagedBlock,
        bool HasExactManagedMapping,
        IReadOnlyList<string> RelatedEntries,
        IReadOnlyList<string> ConflictingEntries,
        HashSet<int> ConflictingLineNumbers,
        string? Error);
}
