# Third-party notices

Trisko connect is a modified distribution of [v2rayN](https://github.com/2dust/v2rayN) by 2dust and contributors, based on the Geksa-A Connection work by S-Hov. The source code is distributed under GPL-3.0; see [`LICENSE`](LICENSE).

The release bundle can include independently licensed networking components:

- [Xray-core](https://github.com/XTLS/Xray-core) — Mozilla Public License 2.0;
- [sing-box](https://github.com/SagerNet/sing-box) — GNU General Public License v3.0 or later;
- [Wintun](https://www.wintun.net/) — WireGuard project distribution terms;
- [VirtualHere USB Client](https://www.virtualhere.com/node/316) — proprietary client software; the Windows client may be installed and used on an unlimited number of computers without payment, subject to the VirtualHere USB Client License;
- GeoIP and GeoSite data included by the selected upstream v2rayN core bundle.
- [UltraVNC Server](https://github.com/ultravnc/UltraVNC) 1.8.3.0 — GNU GPL v3; unmodified Windows x64 server binaries, license and provenance are in `v2rayN/v2rayN.Desktop/Tools/UltraVNC`. Distributions must also provide the corresponding UltraVNC source; it is separate from this application's source archive.
- [MarcusW.VncClient](https://github.com/MarcusWichelmann/MarcusW.VncClient) pinned at commit `9d9fbf7b97b390ca9f0dff68ba52d2b9a85aed09` — MIT; the client core is included as source in `v2rayN/ThirdParty/MarcusW.VncClient`, with documented .NET compatibility and shutdown fixes. Keyboard and mouse adapters were adapted from upstream commit `9d9fbf7b97b390ca9f0dff68ba52d2b9a85aed09`; their license is included in `v2rayN/v2rayN.Desktop/Geksa/Vnc/LICENSE-MarcusW.txt` and copied into application builds.

Networking core binary hashes are recorded in [`eng/core-lock.json`](eng/core-lock.json). When upgrading a bundled component, review its current license and include its license file in the release package before distribution.

AnyDesk is proprietary software. The bundled client is unmodified; see `v2rayN/v2rayN.Desktop/Tools/AnyDesk/README.txt`. Trisko branding is by S-Hov.
