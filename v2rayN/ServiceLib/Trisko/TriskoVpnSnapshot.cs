namespace ServiceLib.Trisko;

public sealed record TriskoVpnFailure(
    string Code,
    string Message,
    bool Retryable)
{
    public string? Details { get; init; }
}

public sealed record TriskoVpnSnapshot(
    TriskoVpnState State,
    bool IsCoreRunning,
    bool IsTunEnabled,
    string? ProfileId,
    string? ProfileName,
    ECoreType? CoreType,
    TriskoVpnFailure? Failure,
    DateTimeOffset UpdatedAtUtc)
{
    public RunningProfileInfo? RunningProfile { get; init; }

    public static TriskoVpnSnapshot Disconnected { get; } = new(
        TriskoVpnState.Disconnected,
        IsCoreRunning: false,
        IsTunEnabled: false,
        ProfileId: null,
        ProfileName: null,
        CoreType: null,
        Failure: null,
        UpdatedAtUtc: DateTimeOffset.UtcNow);
}
