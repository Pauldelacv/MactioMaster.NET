namespace RatioMaster.Core.Tests;

using RatioMaster.Core.Emulation;
using RatioMaster.Core.Net;
using RatioMaster.Core.Tracker;

using Xunit;

public class TrackerClientTests
{
    private static readonly byte[] InfoHash = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static byte[] AnnounceResponse(int interval = 900, int complete = 12, int incomplete = 3) =>
        TorrentBuilder.Bytes($"d8:intervali{interval}e8:completei{complete}e10:incompletei{incomplete}e5:peers6:")
            .Concat(new byte[] { 10, 0, 0, 1, 0x1A, 0xE1 }) // 10.0.0.1:6881
            .Concat(TorrentBuilder.Bytes("e"))
            .ToArray();

    private static AnnounceParameters Parameters(string trackerUrl) => new()
    {
        TrackerUrl = trackerUrl,
        InfoHash = InfoHash,
        Uploaded = 65_536,
        Downloaded = 32_768,
        Left = 1_000_000,
        Port = "51413",
        NumWant = "200",
    };

    [Fact]
    public async Task SendsTheEmulatedClientsHeadersVerbatim()
    {
        using var tracker = new FakeTracker(_ => (AnnounceResponse(), string.Empty));
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var result = await new TrackerClient(ProxySettings.Direct, Timeout)
            .AnnounceAsync(client, Parameters($"{tracker.BaseUrl}/announce"), AnnounceEvent.Started, CancellationToken.None);

        Assert.True(result.Success);

        var request = tracker.WaitForRequest(Timeout);
        Assert.StartsWith("GET /announce?info_hash=", request);
        Assert.Contains(" HTTP/1.1\r\n", request);
        Assert.Contains($"Host: 127.0.0.1\r\n", request);
        Assert.Contains("User-Agent: uTorrent/3320\r\n", request);
        Assert.Contains("Accept-Encoding: gzip\r\n", request);
        Assert.EndsWith("\r\n\r\n", request);

        // uTorrent puts Host first; header order is part of the fingerprint.
        Assert.True(request.IndexOf("Host:", StringComparison.Ordinal) < request.IndexOf("User-Agent:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ParsesIntervalPeersAndSwarmCounts()
    {
        using var tracker = new FakeTracker(_ => (AnnounceResponse(interval: 1200, complete: 42, incomplete: 7), string.Empty));
        var client = ClientCatalog.Create("Transmission 2.92 (14714)");

        var result = await new TrackerClient(ProxySettings.Direct, Timeout)
            .AnnounceAsync(client, Parameters($"{tracker.BaseUrl}/announce"), AnnounceEvent.None, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1200, result.Interval);
        Assert.Equal(42, result.Complete);
        Assert.Equal(7, result.Incomplete);

        var peer = Assert.Single(result.Peers);
        Assert.Equal("10.0.0.1", peer.Address);
        Assert.Equal(6881, peer.Port);
    }

    [Fact]
    public async Task DecompressesGzippedResponses()
    {
        using var tracker = new FakeTracker(_ => (FakeTracker.Gzip(AnnounceResponse(interval: 777)), "Content-Encoding: gzip\r\n"));
        var client = ClientCatalog.Create("BitComet 1.20");

        var result = await new TrackerClient(ProxySettings.Direct, Timeout)
            .AnnounceAsync(client, Parameters($"{tracker.BaseUrl}/announce"), AnnounceEvent.None, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(777, result.Interval);
    }

    [Fact]
    public async Task SurfacesTheTrackersFailureReason()
    {
        using var tracker = new FakeTracker(_ => (TorrentBuilder.Bytes("d14:failure reason23:unregistered torrent!!!e"), string.Empty));
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var result = await new TrackerClient(ProxySettings.Direct, Timeout)
            .AnnounceAsync(client, Parameters($"{tracker.BaseUrl}/announce"), AnnounceEvent.Started, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("unregistered torrent!!!", result.FailureReason);
    }

    [Fact]
    public async Task ReadsTheScrapeCountersForThisTorrent()
    {
        var key = RatioMaster.Core.BEncode.BValue.Binary.GetString(InfoHash);
        var body = TorrentBuilder.Bytes("d5:filesd20:")
            .Concat(InfoHash)
            .Concat(TorrentBuilder.Bytes("d8:completei55e10:downloadedi999e10:incompletei4eeee"))
            .ToArray();

        using var tracker = new FakeTracker(_ => (body, string.Empty));
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var result = await new TrackerClient(ProxySettings.Direct, Timeout)
            .ScrapeAsync(client, $"{tracker.BaseUrl}/announce", InfoHash, CancellationToken.None);

        Assert.Equal(55, result.Complete);
        Assert.Equal(4, result.Incomplete);
        Assert.Equal(999, result.Downloaded);
        Assert.NotEmpty(key);

        var request = tracker.WaitForRequest(Timeout);
        Assert.StartsWith("GET /scrape?info_hash=", request);
    }

    [Fact]
    public async Task ReportsAConnectionFailureInsteadOfThrowing()
    {
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        // Port 1 on loopback refuses connections.
        var result = await new TrackerClient(ProxySettings.Direct, TimeSpan.FromSeconds(3))
            .AnnounceAsync(client, Parameters("http://127.0.0.1:1/announce"), AnnounceEvent.Started, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.TransportError);
    }

    [Fact]
    public async Task RefusesNonHttpTrackerProtocols()
    {
        var client = ClientCatalog.Create("uTorrent 3.3.2");

        var result = await new TrackerClient(ProxySettings.Direct, Timeout)
            .AnnounceAsync(client, Parameters("udp://tracker.example:6969/announce"), AnnounceEvent.Started, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("udp", result.TransportError, StringComparison.OrdinalIgnoreCase);
    }
}
