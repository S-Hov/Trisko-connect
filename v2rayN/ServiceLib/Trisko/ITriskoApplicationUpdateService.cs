namespace ServiceLib.Trisko;

public interface ITriskoApplicationUpdateService
{
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    string ReleaseSource { get; }

    TriskoAvailableUpdate? AvailableUpdate { get; }

    Task<TriskoUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    Task DownloadAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task ApplyAndRestartAsync(CancellationToken cancellationToken = default);
}

public enum TriskoUpdateCheckResult
{
    NotInstalled,
    UpToDate,
    Available
}

public sealed record TriskoAvailableUpdate(
    string Version,
    string? ReleaseNotesMarkdown);
