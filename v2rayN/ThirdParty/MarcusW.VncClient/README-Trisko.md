# MarcusW.VncClient — pinned source

Upstream: https://github.com/MarcusWichelmann/MarcusW.VncClient

Commit: `9d9fbf7b97b390ca9f0dff68ba52d2b9a85aed09` (MIT, see LICENSE.txt).

Only the protocol core is included. The old Avalonia package is not used.
The local project targets the repository's .NET version and current logging abstractions.

Local changes:

- `Utils/BackgroundThread.cs`: catch normal cancellation inside raw worker threads instead of allowing an unhandled OperationCanceledException to terminate the application; remove unsupported Thread.Abort.

- Preserve upstream unchecked pixel codec arithmetic when building inside this repository, which otherwise enables checked arithmetic globally.
- Close the transport before joining worker threads so idle connections do not hang during shutdown.
- Replace obsolete DES provider construction and make the ZRLE palette switch exhaustive.

- Disable Nagle buffering for VNC input and request a full frame again if the initial update contains no pixel data.

The original NuGet alpha4 reproduced a process crash on a normal CloseAsync against UltraVNC 1.8.3.0. Keep this regression covered when updating the core.
