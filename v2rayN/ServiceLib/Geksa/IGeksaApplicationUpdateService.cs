namespace ServiceLib.Geksa;

public interface IGeksaApplicationUpdateService
{
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    string ReleaseSource { get; }

    GeksaAvailableUpdate? AvailableUpdate { get; }

    Task<GeksaUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    Task DownloadAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task ApplyAndRestartAsync(CancellationToken cancellationToken = default);
}

public enum GeksaUpdateCheckResult
{
    NotInstalled,
    UpToDate,
    Available
}

public sealed record GeksaAvailableUpdate(
    string Version,
    string? ReleaseNotesMarkdown);
