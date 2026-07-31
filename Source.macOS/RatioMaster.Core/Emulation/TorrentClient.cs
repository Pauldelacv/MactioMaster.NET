namespace RatioMaster.Core.Emulation;

/// <summary>
/// One emulated BitTorrent client: the exact HTTP request shape a tracker expects
/// from it (protocol version, header block and ordering, query template) plus the
/// per-session random identifiers.
/// </summary>
public sealed class TorrentClient
{
    public required string Name { get; init; }

    /// <summary>"HTTP/1.0" or "HTTP/1.1" — part of the fingerprint.</summary>
    public required string HttpProtocol { get; init; }

    /// <summary>Whether percent escapes in the info hash are upper-case.</summary>
    public required bool HashUpperCase { get; init; }

    /// <summary>Raw header block, CRLF separated, with a <c>{host}</c> placeholder.</summary>
    public required string Headers { get; init; }

    /// <summary>Announce query template with <c>{infohash}</c>, <c>{peerid}</c>, … placeholders.</summary>
    public required string Query { get; init; }

    public int DefaultNumWant { get; init; } = 200;

    public string PeerId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;
}
