namespace ServiceLib.Geksa;

public static class GeksaSmbPath
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
