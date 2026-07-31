RatioMaster.NET for macOS
=========================

A native macOS port of RatioMaster.NET. Same purpose as the Windows original:
it reports upload and download figures to a BitTorrent tracker while emulating a
real client's HTTP fingerprint, without transferring any torrent data.

The Windows build is .NET Framework 4.0 + WinForms and cannot run on macOS in
any form. This is a rewrite of the application layer onto .NET 8 and Avalonia,
keeping the tracker protocol behaviour identical.

Requirements
------------

* macOS 11 Big Sur or newer, Apple Silicon or Intel
* [.NET 8 SDK](https://dotnet.microsoft.com/download) — to build only; the
  produced `.app` is self-contained and needs no runtime installed

Building
--------

```bash
cd Source.macOS/build
chmod +x build-macos.sh
./build-macos.sh universal --dmg
```

The bundle lands in `Source.macOS/artifacts/RatioMaster.app`. Pass `arm64` or
`x86_64` instead of `universal` for a single-architecture build; drop `--dmg` to
skip the disk image.

The bundle is signed ad-hoc, so the first launch needs a right-click → **Open**
(or `xattr -dr com.apple.quarantine RatioMaster.app`).

Running the tests:

```bash
cd Source.macOS
dotnet test
```

Layout
------

| Project                  | What it holds                                                        |
| ------------------------ | -------------------------------------------------------------------- |
| `RatioMaster.Core`       | Bencode, torrent parsing, client emulation, tracker I/O, announce engine. No UI dependency. |
| `RatioMaster.App`        | Avalonia interface, native menu bar, tabs, session files.            |
| `RatioMaster.Core.Tests` | xUnit suite over the protocol-critical paths.                        |

`RatioMaster.Core` targets plain `net8.0` and runs anywhere .NET does, so a CLI
or a Linux front end can be added on top of it without touching the engine.

Where things are stored
-----------------------

The Windows build kept its settings in `HKCU\Software\RatioMaster.NET`. This one
follows macOS conventions:

```
~/Library/Application Support/RatioMaster.NET/settings.json
```

`.session` files keep the same XML schema as the Windows version, so sessions
move between the two builds unchanged.

What changed relative to the Windows build
------------------------------------------

### Removed

* **Reading identifiers out of a running torrent client's memory.** The original
  scanned the address space of uTorrent/BitComet/Azureus for a live `peer_id`,
  `key` and port. On macOS that requires `task_for_pid`, which needs either root
  or a `com.apple.security.cs.debugger` entitlement and a provisioning profile.
  Identifiers are now always generated, which is what the Windows build fell back
  to whenever the target client was not running.
* **The 8,000-line `BytesRoads` socket library** (a 2004-era SOCKS
  implementation). Replaced by `Net/ProxyConnector.cs`, ~250 lines covering the
  same four proxy modes.
* **The version check against `ratiomaster.net`**, which blocked start-up behind
  a modal dialog against a host that no longer answers.
* **The tray icon and "minimise to tray"**, which have no macOS equivalent worth
  reproducing. Closing the window quits after announcing `stopped`.

### Added

* **HTTPS trackers.** The original spoke plain HTTP only; most private trackers
  have been HTTPS-only for years.
* **`announce-list` support** (BEP 12) as a fallback when `announce` is absent.
* **IPv6 peers** from the `peers6` key (BEP 7).
* **Chunked and `deflate`/`br` responses**, alongside the `gzip` the original handled.
* **Redirect following** with loop detection.

### Fixed

Bugs carried by the Windows build that this port corrects — each has a
regression test:

| Bug | Effect |
| --- | ------ |
| Bencode round-tripped through Windows-1252 | Torrents whose piece hashes contain `0x81`, `0x8D`, `0x8F`, `0x90` or `0x9D` got a corrupted info hash, so the tracker rejected every announce. Roughly 1 torrent in 50. |
| The info hash was recomputed by re-encoding the `info` dictionary | Torrents with non-canonical key ordering hashed to the wrong value. The raw byte range is now hashed instead. |
| Deluge's query template got the whole `&event=started` fragment | Produced `event=&event=started`. |
| `BitSpirit`, `KTorrent`, `Gnome BT` header blocks lacked a closing CRLF, and `BitTyrant`/`KTorrent` used `\n\r` instead of `\r\n` | Malformed requests that trackers either ignored or answered slowly. |
| uTorrent 3.2.0's peer id was 22 bytes | The protocol mandates exactly 20. |
| The HTTP response body was located by `Headers.Length` | Off by one byte per header line on servers using bare LF. |
| `float.Parse(text.Replace(".", ","))` for speed fields | Speeds were unparseable on any locale using `.` as the decimal separator. All parsing is now invariant. |
| `RandomSP` threw when min > max | Swapping the jitter bounds stopped the session. |

Notes
-----

* Announcing a `stopped` event on quit is done before the process exits; give it
  a second on a slow tracker.
* Inbound peer handshakes are answered on the announced port when no proxy is
  configured. Nothing beyond the handshake is served — no piece ever changes hands.
* Using this against a private tracker violates the rules of essentially all of
  them.

Licence
-------

Same as the parent project — see `LICENSE` at the repository root.
