namespace RatioMaster.Core.Tests;

using RatioMaster.Core.Emulation;
using RatioMaster.Core.Tracker;

using Xunit;

public class TrackerRequestTests
{
    private static readonly byte[] InfoHash =
    [
        0x12, 0x34, 0xAB, 0xCD, 0x00, 0xFF, 0x41, 0x42, 0x43, 0x44,
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A,
    ];

    private static AnnounceParameters Parameters(string tracker = "http://tracker.example/announce") => new()
    {
        TrackerUrl = tracker,
        InfoHash = InfoHash,
        Uploaded = 1_000_000,
        Downloaded = 500_009,
        Left = 2_000_000,
        Port = "51413",
        NumWant = "200",
    };

    [Fact]
    public void FillsEveryPlaceholderInTheQueryTemplate()
    {
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var url = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.Started);

        Assert.DoesNotContain('{', url);
        Assert.DoesNotContain('}', url);
        Assert.Contains("port=51413", url);
        Assert.Contains("numwant=200", url);
        Assert.Contains("&event=started", url);
    }

    [Fact]
    public void RoundsReportedTotalsTheWayRealClientsDo()
    {
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var url = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.None);

        // 1_000_000 rounded down to a multiple of 16384, 500_009 to a multiple of 16.
        Assert.Contains("uploaded=999424", url);
        Assert.Contains("downloaded=500000", url);
    }

    [Fact]
    public void EncodesTheInfoHashPerByte()
    {
        var upper = ClientCatalog.Create("BitComet 1.20");
        var lower = ClientCatalog.Create("uTorrent 3.3.2");

        var upperUrl = TrackerRequestBuilder.BuildAnnounceUrl(upper, Parameters(), AnnounceEvent.None);
        var lowerUrl = TrackerRequestBuilder.BuildAnnounceUrl(lower, Parameters(), AnnounceEvent.None);

        // Bytes that happen to be ASCII letters or digits travel unescaped, exactly
        // as real clients send them: 0x34 is '4', 0x41-0x44 are 'A'-'D'.
        Assert.Contains("info_hash=%124%AB%CD%00%FFABCD", upperUrl);
        Assert.Contains("info_hash=%124%ab%cd%00%ffABCD", lowerUrl);
    }

    [Fact]
    public void AppendsTheQueryWithTheRightSeparator()
    {
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var plain = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters("http://t.example/announce"), AnnounceEvent.None);
        var withPasskey = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters("http://t.example/announce?passkey=abc"), AnnounceEvent.None);

        Assert.Contains("/announce?info_hash=", plain);
        Assert.Contains("?passkey=abc&info_hash=", withPasskey);
    }

    [Fact]
    public void DropsTheLocalIpBlockOnTheFirstAnnounceOnly()
    {
        var client = ClientCatalog.Create("BitComet 1.20");

        var started = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.Started);
        var periodic = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.None);

        Assert.DoesNotContain("natmapped", started);
        Assert.Contains("natmapped=1", periodic);
    }

    [Fact]
    public void SendsAbcsTrackerIdOnlyWithTheStopEvent()
    {
        var client = ClientCatalog.Create("ABC 3.1");

        var periodic = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.None);
        var stopped = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.Stopped);

        Assert.DoesNotContain("trackerid=48", periodic);
        Assert.Contains("trackerid=48", stopped);
    }

    /// <summary>
    /// Deluge's template embeds only the event value; the original fed it the whole
    /// "&amp;event=started" fragment and emitted "event=&amp;event=started".
    /// </summary>
    [Fact]
    public void UsesABareEventValueForTemplatesThatSpellOutEventEquals()
    {
        var client = ClientCatalog.Create("Deluge 1.2.0");

        var url = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.Started);

        Assert.Contains("&event=started&", url);
        Assert.DoesNotContain("event=&event=", url);
    }

    [Fact]
    public void OmitsTheEventParameterOnPeriodicAnnounces()
    {
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var url = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.None);

        Assert.DoesNotContain("event=", url);
    }

    [Theory]
    [InlineData("http://t.example/announce", "http://t.example/scrape?info_hash=")]
    [InlineData("http://t.example/announce.php", "http://t.example/scrape.php?info_hash=")]
    [InlineData("http://t.example/abc/announce?passkey=k", "http://t.example/abc/scrape?passkey=k&info_hash=")]
    public void DerivesTheScrapeUrlFromTheAnnounceUrl(string announce, string expectedPrefix)
    {
        var url = TrackerRequestBuilder.BuildScrapeUrl(announce, InfoHash, hashUpperCase: true);

        Assert.NotNull(url);
        Assert.StartsWith(expectedPrefix, url);
    }

    [Fact]
    public void ReturnsNullWhenTheTrackerPathIsNotAnAnnounceEndpoint()
    {
        Assert.Null(TrackerRequestBuilder.BuildScrapeUrl("http://t.example/track", InfoHash, hashUpperCase: true));
    }

    [Fact]
    public void EveryCatalogEntryProducesACompleteRequest()
    {
        foreach (var name in ClientCatalog.AllClientNames)
        {
            var client = ClientCatalog.Create(name);

            Assert.Equal(name, client.Name);
            Assert.NotEmpty(client.PeerId);
            Assert.NotEmpty(client.Key);
            Assert.Contains("{host}", client.Headers);

            // A header block that does not end in CRLF produced a request the
            // tracker never answered in the original.
            Assert.EndsWith("\r\n", client.Headers);
            Assert.DoesNotContain("\n\r", client.Headers);

            var url = TrackerRequestBuilder.BuildAnnounceUrl(client, Parameters(), AnnounceEvent.Started);
            Assert.DoesNotContain('{', url);
            Assert.Contains("info_hash=", url);
            Assert.Contains("peer_id=", url);
        }
    }

    [Fact]
    public void PeerIdDecodesToExactlyTwentyBytes()
    {
        foreach (var name in ClientCatalog.AllClientNames)
        {
            var client = ClientCatalog.Create(name);

            var decoded = Engine.PeerListener.DecodePeerId(client.PeerId);

            Assert.Equal(20, decoded.Length);
        }
    }
}
