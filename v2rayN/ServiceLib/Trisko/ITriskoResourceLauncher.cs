namespace ServiceLib.Trisko;

public interface ITriskoResourceLauncher
{
    Task LaunchAsync(
        TriskoResourceProfile resource,
        TriskoVpnSnapshot vpnSnapshot,
        CancellationToken cancellationToken = default);
}
