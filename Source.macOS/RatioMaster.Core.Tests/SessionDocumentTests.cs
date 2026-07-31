namespace RatioMaster.Core.Tests;

using RatioMaster.Core.Config;
using RatioMaster.Core.Engine;
using RatioMaster.Core.Net;

using Xunit;

public class SessionDocumentTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"rm-{Guid.NewGuid():N}.session");

    public void Dispose()
    {
        if (File.Exists(this.path))
        {
            File.Delete(this.path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RoundTripsASession()
    {
        var options = new SessionOptions
        {
            TorrentPath = "/Users/someone/Downloads/example.torrent",
            TrackerUrl = "https://tracker.example/announce?passkey=abc",
            ClientName = "Transmission 2.92 (14714)",
            UploadRateBytes = 120 * 1024,
            DownloadRateBytes = 45 * 1024,
            FinishedPercent = 37.5,
            Port = "51413",
            ListenForPeers = false,
            RequestScrape = true,
            IgnoreFailureReason = true,
            StopCondition = StopCondition.WhenUploadedAbove,
            StopValue = 2048,
            UploadJitter = new RandomRange { Enabled = true, MinKiB = 3, MaxKiB = 9 },
            RerollDownload = new RandomRange { Enabled = true, MinKiB = 20, MaxKiB = 60 },
            Proxy = new ProxySettings { Kind = ProxyKind.Socks5, Host = "127.0.0.1", Port = 1080, User = "u", Password = "p" },
        };

        SessionDocument.Save(this.path, [new SessionEntry("Tab A", options)]);
        var loaded = SessionDocument.Load(this.path);

        var entry = Assert.Single(loaded);
        Assert.Equal("Tab A", entry.Name);
        Assert.Equal(options.TorrentPath, entry.Options.TorrentPath);
        Assert.Equal(options.TrackerUrl, entry.Options.TrackerUrl);
        Assert.Equal(options.ClientName, entry.Options.ClientName);
        Assert.Equal(120 * 1024, entry.Options.UploadRateBytes);
        Assert.Equal(45 * 1024, entry.Options.DownloadRateBytes);
        Assert.Equal(37.5, entry.Options.FinishedPercent);
        Assert.Equal("51413", entry.Options.Port);
        Assert.False(entry.Options.ListenForPeers);
        Assert.True(entry.Options.IgnoreFailureReason);
        Assert.Equal(StopCondition.WhenUploadedAbove, entry.Options.StopCondition);
        Assert.Equal(2048, entry.Options.StopValue);
        Assert.True(entry.Options.UploadJitter.Enabled);
        Assert.Equal(3, entry.Options.UploadJitter.MinKiB);
        Assert.Equal(9, entry.Options.UploadJitter.MaxKiB);
        Assert.Equal(ProxyKind.Socks5, entry.Options.Proxy.Kind);
        Assert.Equal(1080, entry.Options.Proxy.Port);
        Assert.Equal("u", entry.Options.Proxy.User);
    }

    /// <summary>Sessions written on Windows carry the labels of the old combo boxes.</summary>
    [Fact]
    public void ReadsASessionWrittenByTheWindowsBuild()
    {
        File.WriteAllText(this.path, """
            <?xml version="1.0" encoding="utf-8"?>
            <main>
              <RatioMaster>
                <Name>RM 1</Name>
                <Address>C:\torrents\x.torrent</Address>
                <Tracker>http://tracker.example/announce</Tracker>
                <UploadSpeed>60</UploadSpeed>
                <UploadRandom>True</UploadRandom>
                <UploadRandMin>1</UploadRandMin>
                <UploadRandMax>10</UploadRandMax>
                <DownloadSpeed>30</DownloadSpeed>
                <DownloadRandom>False</DownloadRandom>
                <DownloadRandMin>1</DownloadRandMin>
                <DownloadRandMax>10</DownloadRandMax>
                <Client>uTorrent</Client>
                <Version>3.3.2</Version>
                <Finished>12,5</Finished>
                <StopType>When leechers &lt;</StopType>
                <StopValue>10</StopValue>
                <Port>12345</Port>
                <UseTCP>True</UseTCP>
                <UseScrape>True</UseScrape>
                <ProxyType>SOCKS4a</ProxyType>
                <ProxyUser></ProxyUser>
                <ProxyPass></ProxyPass>
                <ProxyHost>proxy.example</ProxyHost>
                <ProxyPort>1080</ProxyPort>
                <NextUpdateUpload>False</NextUpdateUpload>
                <NextUpdateUploadFrom>10</NextUpdateUploadFrom>
                <NextUpdateUploadTo>50</NextUpdateUploadTo>
                <NextUpdateDownload>False</NextUpdateDownload>
                <NextUpdateDownloadFrom>10</NextUpdateDownloadFrom>
                <NextUpdateDownloadTo>100</NextUpdateDownloadTo>
                <IgnoreFailureReason>False</IgnoreFailureReason>
              </RatioMaster>
            </main>
            """);

        var entry = Assert.Single(SessionDocument.Load(this.path));

        Assert.Equal("RM 1", entry.Name);
        Assert.Equal("uTorrent 3.3.2", entry.Options.ClientName);

        // The comma decimal separator of a French/German Windows install must parse.
        Assert.Equal(12.5, entry.Options.FinishedPercent);
        Assert.Equal(StopCondition.WhenLeechersBelow, entry.Options.StopCondition);
        Assert.Equal(ProxyKind.Socks4a, entry.Options.Proxy.Kind);
        Assert.Equal(1080, entry.Options.Proxy.Port);
    }

    [Theory]
    [InlineData("uTorrent 3.3.2", "uTorrent", "3.3.2")]
    [InlineData("uTorrent 2.0.1 (build 19078)", "uTorrent", "2.0.1 (build 19078)")]
    [InlineData("Gnome BT 0.0.28-1", "Gnome BT", "0.0.28-1")]
    [InlineData("Transmission 2.92 (14714)", "Transmission", "2.92 (14714)")]
    public void SplitsClientNamesWithSpacesInBothHalves(string full, string client, string version)
    {
        Assert.Equal((client, version), SessionDocument.SplitClientName(full));
        Assert.Equal(full, SessionDocument.JoinClientName(client, version));
    }
}
