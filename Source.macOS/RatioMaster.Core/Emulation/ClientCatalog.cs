namespace RatioMaster.Core.Emulation;

/// <summary>
/// The hard-coded client fingerprints, ported one-for-one from the Windows
/// <c>TorrentClientFactory</c>. Header blocks, header ordering and query parameter
/// ordering are reproduced verbatim — they are the fingerprint.
/// </summary>
public static class ClientCatalog
{
    private const string AzureusQuery =
        "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3";

    private const string BitCometQuery =
        "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&localip={localip}&port_type=wan&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&compact=1&no_peer_id=1&key={key}{event}";

    private const string UTorrentQuery =
        "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&corrupt=0&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";

    private const string UTorrentLegacyQuery =
        "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&key={key}{event}&numwant={numwant}&compact=1&no_peer_id=1";

    private const string DelugeQuery =
        "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&event={event}&key={key}&compact=1&numwant={numwant}&supportcrypto=1&no_peer_id=1";

    /// <summary>Client families in the order the original combo box listed them.</summary>
    public static IReadOnlyList<ClientFamily> Families { get; } =
    [
        new("uTorrent", ["3.3.2", "3.3.0", "3.2.0", "2.0.1 (build 19078)", "1.8.5 (build 17414)", "1.8.1-beta(11903)", "1.8.0", "1.7.7", "1.7.6", "1.7.5", "1.6.1", "1.6"]),
        new("BitComet", ["1.20", "1.03", "0.98", "0.96", "0.93", "0.92"]),
        new("Azureus", ["3.1.1.0", "3.0.5.0", "3.0.4.2", "3.0.3.4", "3.0.2.2", "2.5.0.4"]),
        new("Vuze", ["4.2.0.8"]),
        new("BitTorrent", ["6.0.3 (8642)"]),
        new("Transmission", ["2.92 (14714)", "2.82 (14160)"]),
        new("ABC", ["3.1"]),
        new("BitLord", ["1.1"]),
        new("BTuga", ["2.1.8"]),
        new("BitTornado", ["0.3.17"]),
        new("Burst", ["3.1.0b"]),
        new("BitTyrant", ["1.1"]),
        new("BitSpirit", ["3.6.0.200", "3.1.0.077"]),
        new("Deluge", ["1.2.0", "0.5.8.7", "0.5.8.6"]),
        new("KTorrent", ["2.2.1"]),
        new("Gnome BT", ["0.0.28-1"]),
    ];

    public const string DefaultClientName = "uTorrent 3.3.2";

    public static IEnumerable<string> AllClientNames =>
        Families.SelectMany(family => family.Versions.Select(version => $"{family.Name} {version}"));

    /// <summary>
    /// Builds a client profile with freshly generated identifiers. Unknown names
    /// fall back to the default profile, matching the original behaviour.
    /// </summary>
    public static TorrentClient Create(string name) => name switch
    {
        // ---------- BitComet ----------
        "BitComet 1.20" => BitComet("1.20", "BC0120", "BitComet/1.20.3.25"),
        "BitComet 1.03" => BitComet("1.03", "BC0103", "BitComet/1.3.7.17"),
        "BitComet 0.98" => BitCometLegacy("0.98", "BC0098"),
        "BitComet 0.96" => BitCometLegacy("0.96", "BC0096"),
        "BitComet 0.93" => BitCometLegacy("0.93", "BC0093"),
        "BitComet 0.92" => BitCometLegacy("0.92", "BC0092"),

        // ---------- Vuze / Azureus ----------
        "Vuze 4.2.0.8" => Azureus("Vuze 4.2.0.8", "AZ4208", "User-Agent: Azureus 4.2.0.8;Windows XP;Java 1.6.0_05\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n"),
        "Azureus 3.1.1.0" => Azureus("Azureus 3.1.1.0", "AZ3110", "User-Agent: Azureus 3.1.1.0;Windows XP;Java 1.6.0_07\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n"),
        "Azureus 3.0.5.0" => Azureus("Azureus 3.0.5.0", "AZ3050", "User-Agent: Azureus 3.0.5.0;Windows XP;Java 1.6.0_05\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nContent-type: application/x-www-form-urlencoded\r\n"),
        "Azureus 3.0.4.2" => Azureus("Azureus 3.0.4.2", "AZ3042", "User-Agent: Azureus 3.0.4.2;Windows XP;Java 1.5.0_07\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nContent-type: application/x-www-form-urlencoded\r\n"),
        "Azureus 3.0.3.4" => Azureus("Azureus 3.0.3.4", "AZ3034", "User-Agent: Azureus 3.0.3.4;Windows XP;Java 1.6.0_03\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n"),
        "Azureus 3.0.2.2" => Azureus("Azureus 3.0.2.2", "AZ3022", "User-Agent: Azureus 3.0.2.2;Windows XP;Java 1.6.0_01\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\n"),
        "Azureus 2.5.0.4" => new TorrentClient
        {
            Name = "Azureus 2.5.0.4",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = true,
            Headers = "User-Agent: Azureus 2.5.0.4;Windows XP;Java 1.5.0_10\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nContent-type: application/x-www-form-urlencoded\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}&azver=3",
            DefaultNumWant = 50,
            Key = IdGenerator.Random(8),
            PeerId = "-AZ2504-" + IdGenerator.Random(12),
        },

        // ---------- uTorrent ----------
        "uTorrent 3.3.2" => UTorrent("3.3.2", "UT3320", "uTorrent/3320", "%18w", 10),
        "uTorrent 3.3.0" => UTorrent("3.3.0", "UT3300", "uTorrent/3300", "%b9s", 10),
        "uTorrent 3.2.0" => UTorrent("3.2.0", "UT3200", "uTorrent/3200", "z8\0.", 10),
        "uTorrent 2.0.1 (build 19078)" => UTorrent("2.0.1 (build 19078)", "UT2010", "uTorrent/2010(19078)", "%86J", 10),
        "uTorrent 1.8.5 (build 17414)" => UTorrent("1.8.5 (build 17414)", "UT1850", "uTorrent/1850(17414)", "%06D", 10),
        "uTorrent 1.8.1-beta(11903)" => UTorrent("1.8.1-beta(11903)", "UT181B", "uTorrent/181B(11903)", "%7f.", 10),
        "uTorrent 1.8.0" => UTorrent("1.8.0", "UT1800", "uTorrent/1800", "%25.", 10),
        "uTorrent 1.7.7" => UTorrent("1.7.7", "UT1770", "uTorrent/1770", "%f3%9f", 10, UTorrentLegacyQuery),
        "uTorrent 1.7.6" => UTorrent("1.7.6", "UT1760", "uTorrent/1760", "%b3%9e", 10, UTorrentLegacyQuery),
        "uTorrent 1.7.5" => UTorrent("1.7.5", "UT1750", "uTorrent/1750", "%fa%91", 10, UTorrentLegacyQuery),
        "uTorrent 1.6.1" => UTorrent("1.6.1", "UT1610", "uTorrent/1610", "%ea%81", 10, UTorrentLegacyQuery),
        "uTorrent 1.6" => UTorrent("1.6", "UT1600", "uTorrent/1600", "%d9%81", 10, UTorrentLegacyQuery),

        // ---------- BitTorrent ----------
        "BitTorrent 6.0.3 (8642)" => new TorrentClient
        {
            Name = "BitTorrent 6.0.3 (8642)",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = false,
            Headers = "Host: {host}\r\nUser-Agent: BitTorrent/6030\r\nAccept-Encoding: gzip\r\n",
            Query = UTorrentLegacyQuery,
            DefaultNumWant = 200,
            Key = IdGenerator.RandomHex(8),
            PeerId = "M6-0-3--" + IdGenerator.PercentEncode(IdGenerator.RandomBytes(12), false),
        },

        // ---------- Transmission ----------
        "Transmission 2.92 (14714)" => Transmission("2.92 (14714)", "TR2920", "Transmission/2.92"),
        "Transmission 2.82 (14160)" => Transmission("2.82 (14160)", "TR2500", "Transmission/2.82"),

        // ---------- Everything else ----------
        "ABC 3.1" => new TorrentClient
        {
            Name = "ABC 3.1",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = true,
            Headers = "Host: {host}\r\nUser-Agent: ABC/ABC-3.1.0\r\nAccept-Encoding: gzip\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&trackerid=48&no_peer_id=1&compact=1{event}&key={key}",
            Key = IdGenerator.Random(6),
            PeerId = "A310--" + IdGenerator.Random(14),
        },
        "BitLord 1.1" => new TorrentClient
        {
            Name = "BitLord 1.1",
            HttpProtocol = "HTTP/1.0",
            HashUpperCase = true,
            Headers = "User-Agent: BitTorrent/3.4.2\r\nConnection: close\r\nAccept-Encoding: gzip, deflate\r\nHost: {host}\r\nCache-Control: no-cache\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&natmapped=1&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant=200&compact=1&no_peer_id=1&key={key}{event}",
            Key = IdGenerator.RandomNumeric(4),
            PeerId = "exbc%01%01LORDCz%03%92" + IdGenerator.PercentEncode(IdGenerator.RandomBytes(6), true),
        },
        "BTuga 2.1.8" => new TorrentClient
        {
            Name = "BTuga 2.1.8",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = true,
            Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: BTuga/Revolution-2.6\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&no_peer_id=1&compact=1{event}&key={key}",
            Key = IdGenerator.Random(6),
            PeerId = "R26---" + IdGenerator.Random(14),
        },
        "BitTornado 0.3.17" => new TorrentClient
        {
            Name = "BitTornado 0.3.17",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = true,
            Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: BitTornado/T-0.3.17\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&no_peer_id=1&compact=1{event}&key={key}",
            Key = IdGenerator.Random(6),
            PeerId = "T03H-----" + IdGenerator.Random(11),
        },
        "Burst 3.1.0b" => new TorrentClient
        {
            Name = "Burst 3.1.0b",
            HttpProtocol = "HTTP/1.0",
            HashUpperCase = true,
            Headers = "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: BitTorrent/brst1.1.3\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&key={key}&uploaded={uploaded}&downloaded={downloaded}&left={left}&compact=1{event}",
            Key = IdGenerator.RandomHex(8),
            PeerId = "Mbrst1-1-3" + IdGenerator.RandomHex(10),
        },
        "BitTyrant 1.1" => new TorrentClient
        {
            Name = "BitTyrant 1.1",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = true,
            Headers = "User-Agent: AzureusBitTyrant 2.5.0.0BitTyrant;Windows XP;Java 1.5.0_10\r\nConnection: close\r\nAccept-Encoding: gzip\r\nHost: {host}\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nProxy-Connection: keep-alive\r\nContent-type: application/x-www-form-urlencoded\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&supportcrypto=1&port={port}&azudp={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&numwant={numwant}&no_peer_id=1&compact=1&key={key}",
            DefaultNumWant = 50,
            Key = IdGenerator.Random(8),
            PeerId = "AZ2500BT" + IdGenerator.Random(12),
        },
        "BitSpirit 3.6.0.200" => new TorrentClient
        {
            Name = "BitSpirit 3.6.0.200",
            HttpProtocol = "HTTP/1.0",
            HashUpperCase = false,
            Headers = "User-Agent: BTSP/3602\r\nHost: {host}\r\nAccept_Encoding: gzip\r\nConnection: close\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&key={key}&compact=1&numwant={numwant}&no_peer_id=1",
            Key = IdGenerator.RandomHex(8),
            PeerId = "%2dSP3602" + IdGenerator.PercentEncode(IdGenerator.RandomBytes(13), false),
        },
        "BitSpirit 3.1.0.077" => new TorrentClient
        {
            Name = "BitSpirit 3.1.0.077",
            HttpProtocol = "HTTP/1.0",
            HashUpperCase = false,
            Headers = "User-Agent: BitTorrent/4.1.2\r\nHost: {host}\r\nAccept-Encoding: gzip\r\nConnection: close\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}{event}&key={key}&compact=1&numwant={numwant}",
            Key = IdGenerator.RandomNumeric(3),
            PeerId = "%00%03BS" + IdGenerator.PercentEncode(IdGenerator.RandomBytes(16), false),
        },
        "Deluge 1.2.0" => Deluge("1.2.0", "DE1200", "Host: {host}\r\nUser-Agent: Deluge 1.2.0\r\nConnection: close\r\nAccept-Encoding: gzip\r\n", upperCaseKey: false),
        "Deluge 0.5.8.7" => Deluge("0.5.8.7", "DE0587", "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: Deluge 0.5.8.7\r\n", upperCaseKey: true),
        "Deluge 0.5.8.6" => Deluge("0.5.8.6", "DE0586", "Host: {host}\r\nAccept-Encoding: gzip\r\nUser-Agent: Deluge 0.5.8.6\r\n", upperCaseKey: true),
        "KTorrent 2.2.1" => new TorrentClient
        {
            Name = "KTorrent 2.2.1",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = false,
            Headers = "User-Agent: ktorrent/2.2.1\r\nAccept: text/html, image/gif, image/jpeg, *; q=.2, */*; q=.2\r\nAccept-Encoding: x-gzip, x-deflate, gzip, deflate\r\nHost: {host}\r\nConnection: Keep-Alive\r\n",
            Query = "peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&compact=1&numwant={numwant}&key={key}{event}&info_hash={infohash}",
            DefaultNumWant = 100,
            Key = IdGenerator.RandomNumeric(10),
            PeerId = "-KT2210-" + IdGenerator.RandomNumeric(12),
        },
        "Gnome BT 0.0.28-1" => new TorrentClient
        {
            Name = "Gnome BT 0.0.28-1",
            HttpProtocol = "HTTP/1.1",
            HashUpperCase = true,
            Headers = "Host: {host}\r\nUser-Agent: Python-urllib/2.5\r\nConnection: close\r\nAccept-Encoding: gzip\r\n",
            Query = "info_hash={infohash}&peer_id={peerid}&port={port}&key={key}&uploaded={uploaded}&downloaded={downloaded}&left={left}&compact=1{event}",
            DefaultNumWant = 100,
            Key = IdGenerator.Random(8),
            PeerId = "M3-4-2--" + IdGenerator.Random(12),
        },

        _ => Create(DefaultClientName),
    };

    private static TorrentClient BitComet(string version, string tag, string userAgent) => new()
    {
        Name = "BitComet " + version,
        HttpProtocol = "HTTP/1.1",
        HashUpperCase = true,
        Headers = $"Host: {{host}}\r\nConnection: close\r\nAccept: */*\r\nAccept-Encoding: gzip\r\nUser-Agent: {userAgent}\r\nPragma: no-cache\r\nCache-Control: no-cache\r\n",
        Query = BitCometQuery,
        DefaultNumWant = 200,
        Key = IdGenerator.RandomNumeric(5),
        PeerId = $"-{tag}-" + IdGenerator.PercentEncode(IdGenerator.RandomBytes(12), true),
    };

    private static TorrentClient BitCometLegacy(string version, string tag) => new()
    {
        Name = "BitComet " + version,
        HttpProtocol = "HTTP/1.1",
        HashUpperCase = true,
        Headers = "Accept: */*\r\nAccept-Encoding: gzip\r\nConnection: close\r\nHost: {host}\r\nUser-Agent: Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.0; .NET CLR 1.1.4322)\r\n",
        Query = BitCometQuery,
        DefaultNumWant = 200,
        Key = IdGenerator.RandomNumeric(5),
        PeerId = $"-{tag}-" + IdGenerator.PercentEncode(IdGenerator.RandomBytes(12), true),
    };

    private static TorrentClient Azureus(string name, string tag, string headers) => new()
    {
        Name = name,
        HttpProtocol = "HTTP/1.1",
        HashUpperCase = true,
        Headers = headers,
        Query = AzureusQuery,
        DefaultNumWant = 50,
        Key = IdGenerator.Random(8),
        PeerId = $"-{tag}-" + IdGenerator.Random(12),
    };

    private static TorrentClient UTorrent(string version, string tag, string userAgent, string peerIdPrefix, int randomTailLength, string? query = null) => new()
    {
        Name = "uTorrent " + version,
        HttpProtocol = "HTTP/1.1",
        HashUpperCase = false,
        Headers = $"Host: {{host}}\r\nUser-Agent: {userAgent}\r\nAccept-Encoding: gzip\r\n",
        Query = query ?? UTorrentQuery,
        DefaultNumWant = 200,
        Key = IdGenerator.RandomHex(8),
        PeerId = $"-{tag}-{peerIdPrefix}" + IdGenerator.PercentEncode(IdGenerator.RandomBytes(randomTailLength), false),
    };

    private static TorrentClient Transmission(string version, string tag, string userAgent) => new()
    {
        Name = "Transmission " + version,
        HttpProtocol = "HTTP/1.1",
        HashUpperCase = false,
        Headers = $"User-Agent: {userAgent}\r\nHost: {{host}}\r\nAccept: */*\r\nAccept-Encoding: gzip;q=1.0, deflate, identity\r\n",
        Query = "info_hash={infohash}&peer_id={peerid}&port={port}&uploaded={uploaded}&downloaded={downloaded}&left={left}&numwant={numwant}&key={key}&compact=1&supportcrypto=1{event}",
        DefaultNumWant = 80,
        Key = IdGenerator.RandomHex(8),
        PeerId = $"-{tag}-" + IdGenerator.Random(12),
    };

    private static TorrentClient Deluge(string version, string tag, string headers, bool upperCaseKey) => new()
    {
        Name = "Deluge " + version,
        HttpProtocol = "HTTP/1.0",
        HashUpperCase = false,
        Headers = headers,
        Query = DelugeQuery,
        DefaultNumWant = 200,
        Key = upperCaseKey ? IdGenerator.Random(8).ToUpperInvariant() : IdGenerator.Random(8),
        PeerId = $"-{tag}-" + IdGenerator.Random(12),
    };
}

public sealed record ClientFamily(string Name, IReadOnlyList<string> Versions);
