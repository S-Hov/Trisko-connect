UltraVNC Server 1.8.3.0, Windows x64

Official release: https://uvnc.com/downloads/ultravnc/172-ultravnc-1-8-3-0.html
Original archive: https://uvnc.eu/download/1800/UltraVNC_1830.zip
Archive SHA-256: 11163a0b9b86321bf6403eb0cf84b81f7230ce32e6291377e1fe5beb987f7e4f
Source project: https://github.com/ultravnc/UltraVNC
License: GNU GPL v3; see LICENSE-UltraVNC.txt.

Unmodified server files selected from the official x64 archive:
  server-x64/winvnc.exe
  server-x64/vnchooks.dll
  server-x64/ddengine64.dll
Individual hashes are pinned in GeksaVncServerService.cs.

The VNC client is built into Geksa-A Connection using MarcusW.VncClient.
An external vncviewer.exe is not required. Its Avalonia input adapter is
adapted under MIT; see Geksa/Vnc/LICENSE-MarcusW.txt.

The server is installed only when the user enables access in the VNC panel.
It uses a separate GeksaVnc service and GeksaVnc.ini in portable mode.
Virtual display drivers, repeaters, HTTP access and file transfer are not installed/enabled.

Windows 7 is listed by upstream as the minimum OS for this server release.
Geksa-A Connection itself requires Windows 10/11; use the official standalone
server installer on Windows 7. Windows 7 and macOS interoperability have not
been tested in this repository.

When distributing these GPL binaries, provide their corresponding source
under the GPL alongside the release. The application's own source archive
alone does not contain the UltraVNC server source.
