# Didanihaa · دیدنی‌ها

<img src="src/Didanihaa.WebUI/assets/brand/logomark.svg" alt="Didanihaa mark" width="72" height="72">

A portable Windows media library for your local network. Select files from your own folders and play them in a TV, desktop or mobile browser.

[فارسی](README.md) · [Tool guide](tools/README.md) · [Contributing](CONTRIBUTING.md)

**Add a folder → scan → review → explicitly publish → play the original file.**

Scanning never publishes new files automatically. Changed files return to review. Each folder is a playlist of its direct files; nested folders are independent.

## Features

- Persian, right-to-left administration for sources, scans, publication and service settings.
- Folder playlists, shared household favorites and locally computed popular folders.
- HTML5 playback with natural filename ordering, previous/next, repeat and fullscreen controls.
- A folder queue beside the player and optional real video thumbnails.
- A server-rendered simple TV view with browsing and native playback even when JavaScript is disabled.
- Original-file streaming with HTTP Range and HEAD, without transcoding or remuxing.
- SQLite and configuration beneath the selected portable root; optional Windows Media service.

## Download and run

Extract the Windows x64 ZIP from [Releases](https://github.com/Dariuosh/Didanihaa/releases) to a writable folder and open `Didanihaa.exe`. The v0.1.0 ZIP starts through `tools/Start-Didanihaa.ps1` with PowerShell 7. See the [portable package guide](tools/Portable-README.md).

The Didanihaa console displays startup and runtime status. Close the console, press `Ctrl+C`, or enter `Q` to stop the instance it started.

## Build from source

Build requirements: Windows x64, PowerShell 7, the [.NET SDK](https://dotnet.microsoft.com/download) specified in [global.json](global.json), and Node.js 22.18+ with npm. The stack is C# 15 / .NET 11, TypeScript, Lit and Vite. The default self-contained published package needs neither the SDK nor Node to run.

From the repository root:

```powershell
.\tools\Build.ps1             # Build and run tests
.\tools\Build-Publish.ps1     # Build publish/; does not start the app
.\tools\Start-Didanihaa.ps1   # Run the package and open the default browser
```

The first build copies didanihaa.example.json to local configuration if needed. Build/start does not install a Windows Service or change firewall rules.

| Purpose | Default address |
| --- | --- |
| Viewer | `http://<LAN-IP>:13521/` |
| Administration | `http://<LAN-IP>:13521/admin/` |
| Simple TV | `http://<LAN-IP>:13521/tv/simple` |
| Vite preview | `http://127.0.0.1:5211/admin/` and `/tv/` |
| Internal Media | Loopback only, port `13522` |

Stop script-owned published processes with `tools/Stop-Didanihaa.ps1`. Stop them before replacing published executables. Service installation and scoped LAN firewall configuration are separate, interactive operations described in [the tool guide](tools/README.md).

## Prepare a library

1. Add a source folder in administration and wait for scanning to finish.
2. Review candidates and explicitly publish selected files or the direct contents of a folder.
3. Open the viewer on your TV or another device on the trusted LAN.

FFprobe adds advanced metadata, and FFmpeg generates video thumbnails. Administration shows each tool's status and version. Didanihaa looks in ffmpeg/bin for bundled tools. To select a separate installation, open administration on localhost from the host computer. Playback serves the original file, and codec support depends on the device and browser.

## Development

```powershell
.\tools\Build.ps1
.\tools\Dev.ps1
# When finished:
.\tools\Stop-Dev.ps1
```

Vite binds only to `127.0.0.1:5211` and proxies the LAN gateway. Rebuild and restart after C# changes. `Dev.ps1 -WebOnly` previews current web source against an already running published backend.

Development data/ and published publish/data/ are separate catalogs. Switching between them does not delete previous publication choices. Publishing a build preserves existing destination configuration and data.

## Access and privacy

Run on a trusted LAN. Administration is accessible on the LAN and currently has no login. Host/IP, Origin and mutation-token checks do not authenticate individual LAN users. Do not expose the service directly to the Internet.

Favorites and playback counts belong to one shared household profile and remain in local SQLite. The app uses no cloud service, external telemetry or CDN. Public media endpoints serve published, available files only. See [SECURITY.md](SECURITY.md).

Local settings, catalogs, media, logs, caches and published output are excluded from Git. Distribute binaries from a fresh checkout and example configuration; an existing personal publish/ directory may contain private data. See [Contributing](CONTRIBUTING.md) for source checks.

## Project layout and contribution

Application projects are under src/, C# tests under tests/, PowerShell operations under tools/ and dependency license texts under docs/licenses/.

[Contributing](CONTRIBUTING.md) · [Tool guide](tools/README.md) · [Color tokens](src/Didanihaa.WebUI/admin/theme.css)

DLNA, Chromecast, transcoding/remuxing, selectable subtitles, scheduled scanning and filesystem watching are not implemented. Codec and playback-control support depend on the device and browser. Windows Service access to media depends on the service account's NTFS permissions.

## License

Didanihaa source code is licensed under the [MIT License](LICENSE). Vazirmatn is distributed under SIL OFL 1.1, with its license preserved beside the font. See [third-party notices](THIRD_PARTY_NOTICES.md).
