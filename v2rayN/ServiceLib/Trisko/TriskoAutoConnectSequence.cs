namespace ServiceLib.Trisko;

public sealed record TriskoAutoConnectResult(
    TriskoVpnTestCandidate? Connected,
    int Attempts,
    bool StopFailed = false);

/// <summary>Bounded failover over the existing v2rayN select/connect/stop operations.</summary>
public static class TriskoAutoConnectSequence
{
    public static async Task<TriskoAutoConnectResult> RunAsync(
        IReadOnlyList<TriskoVpnTestCandidate> ranked,
        Func<TriskoVpnTestCandidate, int, Task<bool>> select,
        Func<Task<TriskoVpnSnapshot>> connect,
        Func<Task<TriskoVpnSnapshot>> disconnect,
        Action<TriskoVpnTestCandidate, TriskoVpnSnapshot> failed)
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
            if (snapshot.State == TriskoVpnState.Connected &&
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
