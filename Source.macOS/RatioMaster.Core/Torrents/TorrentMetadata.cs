namespace RatioMaster.Core.Torrents;

using System.Security.Cryptography;

using RatioMaster.Core.BEncode;

public sealed record TorrentEntry(string Path, long Length);

/// <summary>
/// A parsed .torrent file: everything RatioMaster needs to talk to a tracker.
/// </summary>
public sealed class TorrentMetadata
{
    private TorrentMetadata(string announce, string name, byte[] infoHash, ulong totalLength, IReadOnlyList<TorrentEntry> files, IReadOnlyList<string> announceList, int pieceCount)
    {
        this.Announce = announce;
        this.Name = name;
        this.InfoHash = infoHash;
        this.TotalLength = totalLength;
        this.Files = files;
        this.AnnounceList = announceList;
        this.PieceCount = pieceCount;
    }

    public string Announce { get; }

    public string Name { get; }

    /// <summary>Raw 20-byte SHA-1 of the <c>info</c> dictionary.</summary>
    public byte[] InfoHash { get; }

    public string InfoHashHex => Convert.ToHexString(this.InfoHash);

    public ulong TotalLength { get; }

    public IReadOnlyList<TorrentEntry> Files { get; }

    /// <summary>Flattened <c>announce-list</c>, empty when the torrent has none.</summary>
    public IReadOnlyList<string> AnnounceList { get; }

    public int PieceCount { get; }

    public bool IsSingleFile => this.Files.Count == 1;

    public static TorrentMetadata Load(string path) => Parse(File.ReadAllBytes(path));

    public static TorrentMetadata Parse(byte[] data)
    {
        if (BEncodeParser.Parse(data, out var ranges) is not BDictionary root)
        {
            throw new BEncodeException("The torrent file does not contain a bencoded dictionary.");
        }

        if (root["info"] is not BDictionary info)
        {
            throw new BEncodeException("No internal torrent information ('info' dictionary missing).");
        }

        // Hash the bytes exactly as they appeared in the file; re-encoding would
        // silently change the hash for torrents with non-canonical key ordering.
        var infoBytes = ranges.TryGetValue("info", out var infoRange) ? data[infoRange] : info.Encode();
        var infoHash = SHA1.HashData(infoBytes);

        if (info["pieces"] is not BString pieces || pieces.Length % 20 != 0)
        {
            throw new BEncodeException("Missing or damaged piece hash codes.");
        }

        var announce = root.GetString("announce");
        var announceList = ReadAnnounceList(root);
        if (announce.Length == 0)
        {
            announce = announceList.FirstOrDefault() ?? string.Empty;
        }

        if (announce.Length == 0)
        {
            throw new BEncodeException("No tracker URL.");
        }

        var name = info.GetString("name");
        var (files, totalLength) = ReadFiles(info, name);

        return new TorrentMetadata(announce, name, infoHash, totalLength, files, announceList, pieces.Length / 20);
    }

    private static (IReadOnlyList<TorrentEntry> Files, ulong TotalLength) ReadFiles(BDictionary info, string name)
    {
        if (info.Contains("length"))
        {
            var length = info.GetInt64("length");
            return ([new TorrentEntry(name, length)], (ulong)Math.Max(0, length));
        }

        if (info["files"] is not BList list)
        {
            throw new BEncodeException("Torrent declares neither 'length' nor 'files'.");
        }

        var files = new List<TorrentEntry>(list.Count);
        ulong total = 0;
        foreach (var item in list.Items)
        {
            if (item is not BDictionary entry)
            {
                continue;
            }

            var segments = entry["path"] is BList pathParts
                ? pathParts.Items.Select(BValue.AsString).Where(s => !string.IsNullOrEmpty(s))
                : [];
            var length = entry.GetInt64("length");
            files.Add(new TorrentEntry(string.Join('/', segments), length));
            total += (ulong)Math.Max(0, length);
        }

        return (files, total);
    }

    private static IReadOnlyList<string> ReadAnnounceList(BDictionary root)
    {
        if (root["announce-list"] is not BList tiers)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var tier in tiers.Items)
        {
            switch (tier)
            {
                case BList urls:
                    result.AddRange(urls.Items.Select(BValue.AsString).Where(u => !string.IsNullOrEmpty(u))!);
                    break;
                case BString single:
                    result.Add(single.Text);
                    break;
            }
        }

        return result;
    }
}
