RatioMaster.NET
===============

RatioMaster.NET is a small standalone application which fakes upload and download stats of a torrent to almost all bittorrent trackers.

This means that it does NOT rely on your bittorrent client (uTorrent, Azureus, BitComet, ABC and etc.) and it will NOT download/upload the files on a torrent - it only can fake download/upload.

RatioMaster.NET has hardcoded emulations for the most commonly used BitTorrent clients: uTorrent, BitComet, Azureus, ABC, BitLord, BTuga, BitTornado, Burst, BitTyrant, BitSpirit.

## Platforms

| Platform | Source | Stack |
| -------- | ------ | ----- |
| Windows  | [`Source/`](Source) | .NET Framework 4.0, WinForms |
| macOS 11+ (Apple Silicon and Intel) | [`Source.macOS/`](Source.macOS) | .NET 8, Avalonia |

The macOS port shares no code with the Windows build — WinForms, the registry and
the process-memory reader have no macOS equivalent — but reproduces the tracker
protocol behaviour exactly. See [`Source.macOS/README.md`](Source.macOS/README.md)
for build instructions and the list of differences.

## Build status

[![Build status](https://ci.appveyor.com/api/projects/status/16e65svfw87xdolo?svg=true)](https://ci.appveyor.com/project/NikolayIT/ratiomaster-net)