namespace ServiceLib.Geksa;

public static class GeksaConfigurationFileIO
{
    public static async Task<byte[]> ReadLimitedAsync(
        Stream source,
        int maxBytes = GeksaCorporateConfigurationCodec.MaxConfigurationBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        if (source.CanSeek && source.Length > maxBytes)
        {
            throw new GeksaConfigurationException(TriskoLocalization.Format("Файл конфигурации больше {0}.", FormatLimit(maxBytes)));
        }

        using var destination = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (destination.Length + read > maxBytes)
            {
                throw new GeksaConfigurationException(TriskoLocalization.Format("Файл конфигурации больше {0}.", FormatLimit(maxBytes)));
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return destination.ToArray();
    }

    public static async Task WriteTextAtomicallyAsync(
        string destinationPath,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(content);

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new GeksaConfigurationException(TriskoLocalization.Text("Не удалось определить каталог для экспорта."));
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static string SanitizeSuggestedFileName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string((value ?? string.Empty)
            .Trim()
            .Select(character => invalid.Contains(character) || char.IsControl(character)
                ? '-'
                : character)
            .ToArray());
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }
        sanitized = sanitized.Trim(' ', '.', '-');
        return sanitized.IsNullOrEmpty() ? fallback : sanitized;
    }

    private static string FormatLimit(int maxBytes) => maxBytes == 1024 * 1024
        ? TriskoLocalization.Text("1 МиБ")
        : TriskoLocalization.Format("{0} байт", maxBytes);
}
