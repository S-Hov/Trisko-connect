namespace ServiceLib.Geksa;

public sealed record GeksaVpnFailure(
    string Code,
    string Message,
    bool Retryable)
{
    public string? Details { get; init; }
}

public sealed record GeksaVpnSnapshot(
    GeksaVpnState State,
    bool IsCoreRunning,
    bool IsTunEnabled,
    string? ProfileId,
    string? ProfileName,
    ECoreType? CoreType,
    GeksaVpnFailure? Failure,
    DateTimeOffset UpdatedAtUtc)
{
    public RunningProfileInfo? RunningProfile { get; init; }

    public static GeksaVpnSnapshot Disconnected { get; } = new(
        GeksaVpnState.Disconnected,
        IsCoreRunning: false,
        IsTunEnabled: false,
        ProfileId: null,
        ProfileName: null,
        CoreType: null,
        Failure: null,
        UpdatedAtUtc: DateTimeOffset.UtcNow);
}
