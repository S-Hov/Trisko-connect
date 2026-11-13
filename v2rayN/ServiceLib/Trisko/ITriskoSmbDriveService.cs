namespace ServiceLib.Trisko;

public enum TriskoSmbDriveState
{
    Disconnected,
    Connected,
    DriveLetterConflict,
    Error
}

public sealed record TriskoSmbDriveStatus(
    TriskoSmbDriveState State,
    string? DriveLetter = null,
    string? RemotePath = null,
    string? Message = null);

public interface ITriskoSmbDriveService
{
    Task<TriskoSmbDriveStatus> GetStatusAsync(
        TriskoResourceProfile resource,
        CancellationToken cancellationToken = default);

    Task<TriskoSmbDriveStatus> ConnectAsync(
        TriskoResourceProfile resource,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        TriskoResourceProfile resource,
        CancellationToken cancellationToken = default);

    Task OpenAsync(
        TriskoResourceProfile resource,
        CancellationToken cancellationToken = default);
}
