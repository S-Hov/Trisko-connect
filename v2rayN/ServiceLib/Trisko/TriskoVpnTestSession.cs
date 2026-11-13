namespace ServiceLib.Trisko;

/// <summary>A single test's immutable targets and results. Never accepts unrelated profile IDs.</summary>
public sealed class TriskoVpnTestSession(IEnumerable<TriskoVpnProfileOption> targets)
{
    private readonly Dictionary<string, TriskoVpnProfileOption> _targets = targets
        .Where(p => !p.IsAutomatic)
        .DistinctBy(p => p.IndexId)
        .ToDictionary(p => p.IndexId, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _delays = new(StringComparer.OrdinalIgnoreCase);
    private bool _completed;

    public void Start()
    {
        foreach (var target in _targets.Values)
        {
            target.MarkTesting();
        }
    }

    // Called on the UI thread, just like ProfilesViewModel.SetSpeedTestResult.
    public TriskoVpnProfileOption? Apply(string indexId, string? delay)
    {
        if (_completed || !_targets.TryGetValue(indexId, out var target) ||
            !int.TryParse(delay, out var milliseconds))
        {
            return null;
        }
        _delays[indexId] = milliseconds;
        target.ApplyDelay(milliseconds);
        return target;
    }

    public IReadOnlyList<TriskoVpnTestCandidate> Complete()
    {
        _completed = true;
        foreach (var target in _targets.Values)
        {
            if (!_delays.ContainsKey(target.IndexId))
            {
                target.ApplyDelay(0);
            }
        }
        return _delays.Where(p => p.Value > 0)
            .OrderBy(p => p.Value)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new TriskoVpnTestCandidate(_targets[p.Key], p.Value))
            .ToList();
    }
}

public sealed record TriskoVpnTestCandidate(TriskoVpnProfileOption Profile, int DelayMilliseconds);
