namespace RatioMaster.Core.Tests;

using RatioMaster.Core.Engine;

using Xunit;

public class RatioSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static byte[] AnnounceResponse() =>
        TorrentBuilder.Bytes("d8:intervali900e8:completei9e10:incompletei5e5:peers0:e");

    private static string WriteTorrent(string tracker, long length = 10_485_760)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rm-{Guid.NewGuid():N}.torrent");
        File.WriteAllBytes(path, TorrentBuilder.SingleFile(tracker, "payload.bin", length));
        return path;
    }

    [Fact]
    public async Task AnnouncesStartedThenStoppedAcrossAFullRun()
    {
        using var tracker = new FakeTracker(_ => (AnnounceResponse(), string.Empty));
        var torrentPath = WriteTorrent($"{tracker.BaseUrl}/announce");

        try
        {
            await using var session = new RatioSession();
            session.Options.RequestScrape = false;
            session.Options.ListenForPeers = false;
            session.LoadTorrent(torrentPath);

            await session.StartAsync();
            var started = tracker.WaitForRequest(Timeout);

            await session.StopAsync();
            var stopped = tracker.WaitForRequest(Timeout);

            Assert.Contains("event=started", started);
            Assert.Contains("left=10485760", started);
            Assert.Contains("event=stopped", stopped);
            Assert.Equal(SessionState.Stopped, session.State);
        }
        finally
        {
            File.Delete(torrentPath);
        }
    }

    [Fact]
    public async Task AdoptsTheIntervalAndSwarmCountsTheTrackerReturns()
    {
        using var tracker = new FakeTracker(_ => (AnnounceResponse(), string.Empty));
        var torrentPath = WriteTorrent($"{tracker.BaseUrl}/announce");

        try
        {
            await using var session = new RatioSession();
            session.Options.RequestScrape = false;
            session.Options.ListenForPeers = false;
            session.Options.IntervalSeconds = 1800;
            session.LoadTorrent(torrentPath);

            await session.StartAsync();
            tracker.WaitForRequest(Timeout);
            await WaitUntilAsync(() => session.Stats.Seeders == 9, Timeout);

            Assert.Equal(900, session.Stats.IntervalSeconds);
            Assert.Equal(9, session.Stats.Seeders);
            Assert.Equal(5, session.Stats.Leechers);

            await session.StopAsync();
        }
        finally
        {
            File.Delete(torrentPath);
        }
    }

    [Fact]
    public async Task StartsInSeedModeWhenTheTorrentIsMarkedComplete()
    {
        using var tracker = new FakeTracker(_ => (AnnounceResponse(), string.Empty));
        var torrentPath = WriteTorrent($"{tracker.BaseUrl}/announce");

        try
        {
            await using var session = new RatioSession();
            session.Options.RequestScrape = false;
            session.Options.ListenForPeers = false;
            session.Options.FinishedPercent = 100;
            session.LoadTorrent(torrentPath);

            await session.StartAsync();
            var started = tracker.WaitForRequest(Timeout);

            Assert.Contains("left=0", started);
            Assert.True(session.Stats.IsSeeding);

            await session.StopAsync();
        }
        finally
        {
            File.Delete(torrentPath);
        }
    }

    [Fact]
    public async Task StopsItselfWhenTheTrackerReportsAFailure()
    {
        using var tracker = new FakeTracker(_ => (TorrentBuilder.Bytes("d14:failure reason7:go awaye"), string.Empty));
        var torrentPath = WriteTorrent($"{tracker.BaseUrl}/announce");

        try
        {
            await using var session = new RatioSession();
            session.Options.RequestScrape = false;
            session.Options.ListenForPeers = false;
            session.LoadTorrent(torrentPath);

            var messages = new List<string>();
            session.LogLine += line => { lock (messages) { messages.Add(line); } };

            await session.StartAsync();
            await WaitUntilAsync(() => session.State == SessionState.Stopped, Timeout);

            lock (messages)
            {
                Assert.Contains(messages, line => line.Contains("go away", StringComparison.Ordinal));
            }
        }
        finally
        {
            File.Delete(torrentPath);
        }
    }

    [Fact]
    public async Task KeepsGoingOnAFailureWhenAskedTo()
    {
        using var tracker = new FakeTracker(_ => (TorrentBuilder.Bytes("d14:failure reason7:go awaye"), string.Empty));
        var torrentPath = WriteTorrent($"{tracker.BaseUrl}/announce");

        try
        {
            await using var session = new RatioSession();
            session.Options.RequestScrape = false;
            session.Options.ListenForPeers = false;
            session.Options.IgnoreFailureReason = true;
            session.LoadTorrent(torrentPath);

            await session.StartAsync();
            tracker.WaitForRequest(Timeout);
            await Task.Delay(500);

            Assert.Equal(SessionState.Running, session.State);

            await session.StopAsync();
        }
        finally
        {
            File.Delete(torrentPath);
        }
    }

    [Fact]
    public async Task CountersClimbWhileTheSessionRuns()
    {
        using var tracker = new FakeTracker(_ => (AnnounceResponse(), string.Empty));
        var torrentPath = WriteTorrent($"{tracker.BaseUrl}/announce");

        try
        {
            await using var session = new RatioSession();
            session.Options.RequestScrape = false;
            session.Options.ListenForPeers = false;
            session.Options.UploadRateBytes = 50 * 1024;
            session.Options.DownloadRateBytes = 25 * 1024;
            session.Options.UploadJitter.Enabled = false;
            session.Options.DownloadJitter.Enabled = false;
            session.LoadTorrent(torrentPath);

            await session.StartAsync();
            await WaitUntilAsync(() => session.Stats.Uploaded > 0, Timeout);
            await WaitUntilAsync(() => session.Stats.Uploaded >= 100 * 1024, Timeout);

            Assert.True(session.Stats.Downloaded > 0);
            Assert.True(session.Stats.Left < 10_485_760);

            await session.StopAsync();
        }
        finally
        {
            File.Delete(torrentPath);
        }
    }

    /// <summary>The upload rate must drop to zero once the swarm has no leechers left.</summary>
    [Fact]
    public async Task StopsUploadingWhenNoLeechersRemain()
    {
        using var tracker = new FakeTracker(_ =>
            (TorrentBuilder.Bytes("d8:intervali900e8:completei9e10:incompletei0e5:peers0:e"), string.Empty));
        var torrentPath = WriteTorrent($"{tracker.BaseUrl}/announce");

        try
        {
            await using var session = new RatioSession();
            session.Options.RequestScrape = false;
            session.Options.ListenForPeers = false;
            session.Options.UploadRateBytes = 50 * 1024;
            session.LoadTorrent(torrentPath);

            await session.StartAsync();
            await WaitUntilAsync(() => session.Stats.Leechers == 0, Timeout);
            await Task.Delay(300);

            var before = session.Stats.Uploaded;
            await Task.Delay(1500);

            Assert.Equal(before, session.Stats.Uploaded);

            await session.StopAsync();
        }
        finally
        {
            File.Delete(torrentPath);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The expected session state was never reached.");
    }
}
