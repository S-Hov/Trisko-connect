namespace ServiceLib.Geksa;

public sealed record GeksaVpnRuntimeSnapshot(
    bool IsCoreRunning,
    bool IsTunEnabled,
    string? ProfileId,
    string? ProfileName,
    ECoreType? CoreType)
{
    public bool IsTransitioning { get; init; }
    public RunningProfileInfo? RunningProfile { get; init; }
}

public interface IGeksaVpnRuntime
{
    Task<GeksaVpnRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
