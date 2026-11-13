namespace ServiceLib.Geksa;

public sealed record GeksaAutoConnectResult(
    GeksaVpnTestCandidate? Connected,
    int Attempts,
    bool StopFailed = false);

/// <summary>Bounded failover over the existing v2rayN select/connect/stop operations.</summary>
public static class GeksaAutoConnectSequence
{
    public static async Task<GeksaAutoConnectResult> RunAsync(
        IReadOnlyList<GeksaVpnTestCandidate> ranked,
        Func<GeksaVpnTestCandidate, int, Task<bool>> select,
        Func<Task<GeksaVpnSnapshot>> connect,
        Func<Task<GeksaVpnSnapshot>> disconnect,
        Action<GeksaVpnTestCandidate, GeksaVpnSnapshot> failed)
    {
        var attempts = 0;
        foreach (var candidate in ranked.Where(c => c.DelayMilliseconds > 0 && !c.Profile.IsAutomatic)
                     .DistinctBy(c => c.Profile.IndexId).OrderBy(c => c.DelayMilliseconds).Take(3))
        {
            if (!await select(candidate, attempts + 1))
            {
                continue;
            }
            attempts++;
            var snapshot = await connect();
            if (snapshot.State == GeksaVpnState.Connected &&
                string.Equals(snapshot.ProfileId, candidate.Profile.IndexId, StringComparison.OrdinalIgnoreCase))
            {
                return new(candidate, attempts);
            }
            failed(candidate, snapshot);
            var stopped = await disconnect();
            if (stopped.IsCoreRunning || stopped.IsTunEnabled || stopped.Failure is not null)
            {
                return new(null, attempts, StopFailed: true);
            }
        }
        return new(null, attempts);
    }
}
