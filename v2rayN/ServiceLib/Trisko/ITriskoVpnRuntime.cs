namespace ServiceLib.Trisko;

public sealed record TriskoVpnRuntimeSnapshot(
    bool IsCoreRunning,
    bool IsTunEnabled,
    string? ProfileId,
    string? ProfileName,
    ECoreType? CoreType)
{
    public bool IsTransitioning { get; init; }
    public RunningProfileInfo? RunningProfile { get; init; }
}

public interface ITriskoVpnRuntime
{
    Task<TriskoVpnRuntimeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
