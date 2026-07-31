namespace RatioMaster.Core.Tests;

using RatioMaster.Core.BEncode;
using RatioMaster.Core.Torrents;

using Xunit;

public class TorrentMetadataTests
{
    [Fact]
    public void ParsesSingleFileTorrent()
    {
        var data = TorrentBuilder.SingleFile("http://tracker.example/announce", "example.iso", 1_048_576);

        var torrent = TorrentMetadata.Parse(data);

        Assert.Equal("http://tracker.example/announce", torrent.Announce);
        Assert.Equal("example.iso", torrent.Name);
        Assert.Equal(1_048_576UL, torrent.TotalLength);
        Assert.True(torrent.IsSingleFile);
        Assert.Equal(1, torrent.PieceCount);
    }

    [Fact]
    public void SumsTheLengthOfEveryFileInAMultiFileTorrent()
    {
        var data = TorrentBuilder.MultiFile(
            "http://tracker.example/announce",
            "album",
            ("track1.flac", 30_000_000),
            ("track2.flac", 25_000_000));

        var torrent = TorrentMetadata.Parse(data);

        Assert.False(torrent.IsSingleFile);
        Assert.Equal(55_000_000UL, torrent.TotalLength);
        Assert.Equal(["track1.flac", "track2.flac"], torrent.Files.Select(f => f.Path));
    }

    [Fact]
    public void InfoHashMatchesTheRawInfoDictionaryBytes()
    {
        var data = TorrentBuilder.SingleFile("http://tracker.example/announce", "example.iso", 4096);

        var torrent = TorrentMetadata.Parse(data);

        Assert.Equal(TorrentBuilder.ExpectedInfoHash(data), torrent.InfoHash);
    }

    /// <summary>
    /// The Windows build decoded piece hashes through Windows-1252, which maps
    /// 0x81/0x8D/0x8F/0x90/0x9D to U+FFFD and therefore produced a wrong info hash
    /// for any torrent containing them. Roughly one torrent in fifty is affected.
    /// </summary>
    [Fact]
    public void InfoHashSurvivesPieceHashBytesUndefinedInWindows1252()
    {
        var hostile = new byte[20];
        hostile[0] = 0x81;
        hostile[1] = 0x8D;
        hostile[2] = 0x8F;
        hostile[3] = 0x90;
        hostile[4] = 0x9D;

        var data = TorrentBuilder.SingleFile("http://tracker.example/announce", "example.iso", 4096, hostile);

        var torrent = TorrentMetadata.Parse(data);

        Assert.Equal(TorrentBuilder.ExpectedInfoHash(data), torrent.InfoHash);
    }

    [Fact]
    public void FallsBackToAnnounceListWhenAnnounceIsMissing()
    {
        var payload = TorrentBuilder.Bytes("d13:announce-listll30:http://backup.example/announceee4:infod6:lengthi10e4:name1:a12:piece lengthi262144e6:pieces20:")
            .Concat(new byte[20])
            .Concat(TorrentBuilder.Bytes("ee"))
            .ToArray();

        var torrent = TorrentMetadata.Parse(payload);

        Assert.Equal("http://backup.example/announce", torrent.Announce);
    }

    [Fact]
    public void RejectsAPayloadWithoutAnInfoDictionary()
    {
        var payload = TorrentBuilder.Bytes("d8:announce30:http://tracker.example/announcee");

        Assert.Throws<BEncodeException>(() => TorrentMetadata.Parse(payload));
    }
}
