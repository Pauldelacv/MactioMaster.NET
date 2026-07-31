namespace RatioMaster.Core.Engine;

using System.Globalization;

using RatioMaster.Core.Emulation;
using RatioMaster.Core.Net;
using RatioMaster.Core.Torrents;
using RatioMaster.Core.Tracker;

/// <summary>
/// Drives one torrent: announce loop, counters and stop conditions.
/// </summary>
/// <remarks>
/// UI-free by design — the Windows original interleaved this logic with WinForms
/// controls, which is why none of it could be reused as-is. Every observable
/// change is published as an event; the caller is responsible for marshalling to
/// its own thread.
/// </remarks>
public sealed class RatioSession : IAsyncDisposable
{
    private readonly SemaphoreSlim announceGate = new(1, 1);
    private CancellationTokenSource? cancellation;
    private Task? loop;
    private PeerListener? listener;
    private TorrentClient? client;
    private TrackerClient? tracker;
    private long uploadRateBytes;
    private long downloadRateBytes;

    public SessionOptions Options { get; } = new();

    public SessionStats Stats { get; } = new();

    public TorrentMetadata? Torrent { get; private set; }

    public SessionState State { get; private set; } = SessionState.Stopped;

    /// <summary>Identifiers actually in use, after custom overrides are applied.</summary>
    public TorrentClient? ActiveClient => this.client;

    public event Action<string>? LogLine;

    public event Action? StatsUpdated;

    public event Action<SessionState>? StateChanged;

    public TorrentMetadata LoadTorrent(string path)
    {
        var metadata = TorrentMetadata.Load(path);
        this.Torrent = metadata;
        this.Options.TorrentPath = path;
        this.Options.TrackerUrl = metadata.Announce;
        return metadata;
    }

    public async Task StartAsync()
    {
        if (this.State != SessionState.Stopped)
        {
            return;
        }

        if (this.Torrent is null)
        {
            this.Log("Please select a valid torrent file first.");
            return;
        }

        this.client = this.BuildClient();
        this.tracker = new TrackerClient(this.Options.Proxy, this.Options.NetworkTimeout);
        this.uploadRateBytes = this.Options.UploadRateBytes;
        this.downloadRateBytes = this.Options.DownloadRateBytes;

        this.ResetCounters();
        this.LogClientInfo();

        this.cancellation = new CancellationTokenSource();
        this.SetState(SessionState.Running);

        this.StartListener();

        var token = this.cancellation.Token;
        this.loop = Task.Run(() => this.RunAsync(token), CancellationToken.None);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (this.State != SessionState.Running)
        {
            return;
        }

        this.SetState(SessionState.Stopping);

        if (this.cancellation is not null)
        {
            await this.cancellation.CancelAsync().ConfigureAwait(false);
        }

        if (this.loop is not null)
        {
            try
            {
                await this.loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            this.loop = null;
        }

        // The stop announce must survive the cancellation that ended the loop.
        await this.AnnounceAsync(AnnounceEvent.Stopped, CancellationToken.None).ConfigureAwait(false);

        if (this.listener is not null)
        {
            await this.listener.DisposeAsync().ConfigureAwait(false);
            this.listener = null;
        }

        this.cancellation?.Dispose();
        this.cancellation = null;

        this.Stats.SecondsToNextAnnounce = 0;
        this.Stats.ElapsedSeconds = 0;
        this.Stats.Seeders = -1;
        this.Stats.Leechers = -1;
        this.SetState(SessionState.Stopped);
        this.StatsUpdated?.Invoke();
    }

    /// <summary>Forces the next announce to happen immediately.</summary>
    public void AnnounceNow()
    {
        if (this.State == SessionState.Running)
        {
            this.Stats.SecondsToNextAnnounce = 0;
            this.StartListener();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await this.StopAsync().ConfigureAwait(false);
        this.announceGate.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            await this.AnnounceAsync(AnnounceEvent.Started, token).ConfigureAwait(false);
            await this.ScrapeAsync(token).ConfigureAwait(false);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                this.Tick();
                this.StatsUpdated?.Invoke();

                if (this.ShouldStop())
                {
                    _ = Task.Run(this.StopAsync, CancellationToken.None);
                    return;
                }

                if (this.Stats.Left <= 0 && !this.Stats.IsSeeding && this.Stats.TotalSize > 0)
                {
                    this.Stats.IsSeeding = true;
                    this.downloadRateBytes = 0;
                    this.Log("Download complete — switching to seed mode.");
                    await this.AnnounceAsync(AnnounceEvent.Completed, token).ConfigureAwait(false);
                    await this.ScrapeAsync(token).ConfigureAwait(false);
                    this.Stats.SecondsToNextAnnounce = this.Stats.IntervalSeconds;
                    continue;
                }

                if (this.Stats.SecondsToNextAnnounce <= 0)
                {
                    this.RerollRates();
                    this.StartListener();
                    await this.AnnounceAsync(AnnounceEvent.None, token).ConfigureAwait(false);
                    await this.ScrapeAsync(token).ConfigureAwait(false);
                    this.Stats.SecondsToNextAnnounce = this.Stats.IntervalSeconds;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            this.Log($"Session stopped after an unexpected error: {ex.Message}");
            _ = Task.Run(this.StopAsync, CancellationToken.None);
        }
    }

    private void Tick()
    {
        this.Stats.ElapsedSeconds++;
        this.Stats.SecondsToNextAnnounce = Math.Max(0, this.Stats.SecondsToNextAnnounce - 1);

        var upload = this.uploadRateBytes + this.Options.UploadJitter.NextBytes();
        this.Stats.Uploaded += Math.Max(0, upload);

        if (!this.Stats.IsSeeding && this.downloadRateBytes > 0)
        {
            var download = this.downloadRateBytes + this.Options.DownloadJitter.NextBytes();
            this.Stats.Downloaded += Math.Max(0, download);
            this.Stats.Left = Math.Max(0, this.Stats.TotalSize - this.Stats.Downloaded);
        }

        if (this.Stats.Left <= 0 && this.Stats.TotalSize > 0)
        {
            this.Stats.Downloaded = this.Stats.TotalSize;
            this.Stats.Left = 0;
        }
    }

    private void RerollRates()
    {
        if (this.Options.RerollUpload.Enabled)
        {
            this.uploadRateBytes = this.Options.RerollUpload.NextBytes();
            this.Log($"Upload rate re-rolled to {this.uploadRateBytes / 1024} KiB/s");
        }

        if (this.Options.RerollDownload.Enabled && !this.Stats.IsSeeding)
        {
            this.downloadRateBytes = this.Options.RerollDownload.NextBytes();
            this.Log($"Download rate re-rolled to {this.downloadRateBytes / 1024} KiB/s");
        }
    }

    private async Task AnnounceAsync(AnnounceEvent announceEvent, CancellationToken token)
    {
        if (this.client is null || this.tracker is null || this.Torrent is null)
        {
            return;
        }

        await this.announceGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var parameters = new AnnounceParameters
            {
                TrackerUrl = this.Options.TrackerUrl,
                InfoHash = this.Torrent.InfoHash,
                Uploaded = this.Stats.Uploaded,
                Downloaded = this.Stats.Downloaded,
                Left = this.Stats.Left,
                Port = this.ResolvePort(),
                NumWant = announceEvent == AnnounceEvent.Stopped ? "0" : this.ResolveNumWant(),
            };

            var result = await this.tracker.AnnounceAsync(this.client, parameters, announceEvent, token).ConfigureAwait(false);
            this.ReportExchange(announceEvent == AnnounceEvent.None ? "announce" : announceEvent.ToString().ToLowerInvariant(), result);

            if (result.FailureReason is { } failure)
            {
                this.Log("Tracker error: " + failure);
                if (!this.Options.IgnoreFailureReason && announceEvent != AnnounceEvent.Stopped)
                {
                    this.Log("Stopped because of a tracker error.");
                    _ = Task.Run(this.StopAsync, CancellationToken.None);
                }

                return;
            }

            if (result.WarningMessage is { } warning)
            {
                this.Log("Tracker warning: " + warning);
            }

            this.ApplyTrackerStats(result);

            if (result.Peers.Count > 0)
            {
                var preview = string.Join("; ", result.Peers.Take(5));
                this.Log($"peers: ({result.Peers.Count}) {preview}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            this.announceGate.Release();
        }
    }

    private async Task ScrapeAsync(CancellationToken token)
    {
        if (!this.Options.RequestScrape || this.client is null || this.tracker is null || this.Torrent is null)
        {
            return;
        }

        try
        {
            var result = await this.tracker
                .ScrapeAsync(this.client, this.Options.TrackerUrl, this.Torrent.InfoHash, token)
                .ConfigureAwait(false);

            if (result.TransportError is { } error)
            {
                this.Log("Scrape: " + error);
                return;
            }

            this.Log("---------- Scrape info ----------");
            this.ApplyTrackerStats(result);
            if (result.Downloaded is { } downloaded)
            {
                this.Stats.FinishedCount = downloaded;
                this.Log($"downloaded: {downloaded}");
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyTrackerStats(TrackerResult result)
    {
        if (result.Interval is { } interval)
        {
            // Trackers occasionally ask for absurd intervals; keep it sane.
            var clamped = Math.Clamp(interval, 60, 3600);
            if (clamped != this.Stats.IntervalSeconds)
            {
                this.Stats.IntervalSeconds = clamped;
                this.Options.IntervalSeconds = clamped;
                this.Log($"Tracker set the announce interval to {clamped} s");
            }
        }

        if (result.Complete is { } seeders)
        {
            this.Stats.Seeders = seeders;
            this.Log($"complete: {seeders}");
        }

        if (result.Incomplete is { } leechers)
        {
            this.Stats.Leechers = leechers;
            this.Log($"incomplete: {leechers}");

            if (leechers == 0 && this.uploadRateBytes > 0)
            {
                this.Log("No leechers left — upload rate set to 0.");
                this.uploadRateBytes = 0;
                this.Options.UploadJitter.Enabled = false;
            }
        }

        this.StatsUpdated?.Invoke();
    }

    private bool ShouldStop() => this.Options.StopCondition switch
    {
        StopCondition.AfterTime => this.Options.StopValue > 0 && this.Stats.ElapsedSeconds >= this.Options.StopValue,
        StopCondition.WhenSeedersBelow => this.Stats.Seeders > -1 && this.Stats.Seeders < this.Options.StopValue,
        StopCondition.WhenLeechersBelow => this.Stats.Leechers > -1 && this.Stats.Leechers < this.Options.StopValue,
        StopCondition.WhenUploadedAbove => this.Stats.Uploaded > this.Options.StopValue * 1024 * 1024,
        StopCondition.WhenDownloadedAbove => this.Stats.Downloaded > this.Options.StopValue * 1024 * 1024,
        StopCondition.WhenLeecherSeederRatioBelow =>
            this.Stats.Seeders > 0 && this.Stats.Leechers > -1 &&
            this.Stats.Leechers / (double)this.Stats.Seeders < this.Options.StopValue,
        _ => false,
    };

    private TorrentClient BuildClient()
    {
        var built = ClientCatalog.Create(this.Options.ClientName);
        if (this.Options.CustomKey.Length > 0)
        {
            built.Key = this.Options.CustomKey;
        }

        if (this.Options.CustomPeerId.Length > 0)
        {
            built.PeerId = this.Options.CustomPeerId;
        }

        return built;
    }

    private void ResetCounters()
    {
        var total = (long)(this.Torrent?.TotalLength ?? 0);
        var finished = Math.Clamp(this.Options.FinishedPercent, 0, 100);

        this.Stats.TotalSize = total;
        this.Stats.Downloaded = (long)(total * finished / 100.0);
        this.Stats.Left = Math.Max(0, total - this.Stats.Downloaded);
        this.Stats.Uploaded = 0;
        this.Stats.IsSeeding = finished >= 100 || this.Stats.Left == 0;
        this.Stats.IntervalSeconds = Math.Clamp(this.Options.IntervalSeconds, 60, 3600);
        this.Stats.SecondsToNextAnnounce = this.Stats.IntervalSeconds;
        this.Stats.ElapsedSeconds = 0;
        this.Stats.Seeders = -1;
        this.Stats.Leechers = -1;
        this.Stats.FinishedCount = -1;

        if (this.Stats.IsSeeding)
        {
            this.downloadRateBytes = 0;
        }
    }

    private void StartListener()
    {
        if (!this.Options.ListenForPeers || this.client is null || this.Torrent is null)
        {
            return;
        }

        if (this.Options.Proxy.IsEnabled)
        {
            // Inbound connections cannot reach us through an outbound-only proxy.
            return;
        }

        if (this.listener is { IsListening: true })
        {
            return;
        }

        this.listener ??= new PeerListener(this.Torrent.InfoHash, this.client.PeerId, this.Log);
        this.listener.TryStart(int.TryParse(this.ResolvePort(), out var port) ? port : 0);
    }

    private string ResolvePort()
    {
        if (this.Options.Port.Length > 0 && int.TryParse(this.Options.Port, out var configured) && configured is > 0 and <= 65535)
        {
            return configured.ToString(CultureInfo.InvariantCulture);
        }

        this.Options.Port = IdGenerator.RandomPort().ToString(CultureInfo.InvariantCulture);
        return this.Options.Port;
    }

    private string ResolveNumWant()
    {
        if (this.Options.NumWant.Length > 0 && int.TryParse(this.Options.NumWant, out var wanted) && wanted > 0)
        {
            return wanted.ToString(CultureInfo.InvariantCulture);
        }

        return (this.client?.DefaultNumWant ?? 200).ToString(CultureInfo.InvariantCulture);
    }

    private void ReportExchange(string label, TrackerResult result)
    {
        this.Log($"======== {label} request ========");
        this.Log(result.RequestText);

        if (result.TransportError is { } error)
        {
            this.Log("Error: " + error);
            return;
        }

        this.Log("======== tracker response ========");
        this.Log(result.ResponseHeaders);

        if (result.Dictionary is null && result.ResponseBody.Length > 0)
        {
            this.Log("*** Failed to decode the tracker response:");
            this.Log(result.ResponseBody);
        }
    }

    private void LogClientInfo()
    {
        if (this.client is null || this.Torrent is null)
        {
            return;
        }

        this.Log("CLIENT EMULATION INFO:");
        this.Log("Name: " + this.client.Name);
        this.Log("HttpProtocol: " + this.client.HttpProtocol);
        this.Log("HashUpperCase: " + this.client.HashUpperCase);
        this.Log("Key: " + this.client.Key);
        this.Log("PeerID: " + this.client.PeerId);
        this.Log("Headers:");
        this.Log(this.client.Headers);
        this.Log("Query: " + this.client.Query);
        this.Log(string.Empty);
        this.Log("TORRENT INFO:");
        this.Log("Torrent name: " + this.Torrent.Name);
        this.Log("Tracker address: " + this.Options.TrackerUrl);
        this.Log("Hash code: " + this.Torrent.InfoHashHex);
        this.Log($"Size: {this.Stats.TotalSize / 1024} KiB");
        this.Log($"Left: {this.Stats.Left / 1024} KiB");
        this.Log($"Finished: {this.Options.FinishedPercent:0.##} %");
        this.Log($"Upload rate: {this.uploadRateBytes / 1024} KiB/s");
        this.Log($"Download rate: {this.downloadRateBytes / 1024} KiB/s");
        this.Log($"Update interval: {this.Stats.IntervalSeconds} s");
        this.Log("Port: " + this.ResolvePort());
        this.Log("Proxy: " + this.Options.Proxy);
        this.Log(string.Empty);
    }

    private void SetState(SessionState state)
    {
        this.State = state;
        this.StateChanged?.Invoke(state);
    }

    private void Log(string line)
    {
        if (this.Options.LogEnabled)
        {
            this.LogLine?.Invoke(line);
        }
    }
}
