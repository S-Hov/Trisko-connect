namespace ServiceLib.Trisko;

public static class TriskoSmbPath
{
    public static string Create(string host, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedHost = host.Trim().Trim('\\', '/');
        var normalizedPath = path.Replace('/', '\\').Trim('\\');
        return $"\\\\{normalizedHost}\\{normalizedPath}";
    }
}
