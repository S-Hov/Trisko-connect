namespace ServiceLib.Geksa;

public enum GeksaSmbDriveState
{
    Disconnected,
    Connected,
    DriveLetterConflict,
    Error
}

public sealed record GeksaSmbDriveStatus(
    GeksaSmbDriveState State,
    string? DriveLetter = null,
    string? RemotePath = null,
    string? Message = null);

public interface IGeksaSmbDriveService
{
    Task<GeksaSmbDriveStatus> GetStatusAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default);

    Task<GeksaSmbDriveStatus> ConnectAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default);

    Task OpenAsync(
        GeksaResourceProfile resource,
        CancellationToken cancellationToken = default);
}
