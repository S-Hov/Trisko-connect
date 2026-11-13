namespace ServiceLib.Geksa;

public interface IGeksaResourceLauncher
{
    Task LaunchAsync(
        GeksaResourceProfile resource,
        GeksaVpnSnapshot vpnSnapshot,
        CancellationToken cancellationToken = default);
}
