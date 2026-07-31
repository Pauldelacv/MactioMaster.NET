namespace RatioMaster.Core.Engine;

using RatioMaster.Core.Net;

public enum StopCondition
{
    Never,
    AfterTime,
    WhenSeedersBelow,
    WhenLeechersBelow,
    WhenUploadedAbove,
    WhenDownloadedAbove,
    WhenLeecherSeederRatioBelow,
}

public enum SessionState
{
    Stopped,
    Running,
    Stopping,
}

public sealed class RandomRange
{
    public bool Enabled { get; set; }

    public int MinKiB { get; set; } = 1;

    public int MaxKiB { get; set; } = 10;

    public int NextBytes()
    {
        if (!this.Enabled)
        {
            return 0;
        }

        var low = Math.Min(this.MinKiB, this.MaxKiB);
        var high = Math.Max(this.MinKiB, this.MaxKiB);
        return high <= low ? low * 1024 : System.Random.Shared.Next(low, high) * 1024;
    }
}

/// <summary>Everything one RatioMaster tab needs in order to run.</summary>
public sealed class SessionOptions
{
    public string TorrentPath { get; set; } = string.Empty;

    public string TrackerUrl { get; set; } = string.Empty;

    public string ClientName { get; set; } = Emulation.ClientCatalog.DefaultClientName;

    /// <summary>Base upload rate in bytes per announce interval second.</summary>
    public long UploadRateBytes { get; set; } = 60 * 1024;

    public long DownloadRateBytes { get; set; } = 30 * 1024;

    /// <summary>Announce interval in seconds; the tracker may lower or raise it.</summary>
    public int IntervalSeconds { get; set; } = 1800;

    /// <summary>How much of the torrent is already "downloaded", 0-100.</summary>
    public double FinishedPercent { get; set; }

    public RandomRange UploadJitter { get; set; } = new() { Enabled = true };

    public RandomRange DownloadJitter { get; set; } = new() { Enabled = true };

    /// <summary>When set, the base rate is re-rolled inside this range before each announce.</summary>
    public RandomRange RerollUpload { get; set; } = new() { MinKiB = 10, MaxKiB = 50 };

    public RandomRange RerollDownload { get; set; } = new() { MinKiB = 10, MaxKiB = 100 };

    public string Port { get; set; } = string.Empty;

    public string NumWant { get; set; } = string.Empty;

    public string CustomKey { get; set; } = string.Empty;

    public string CustomPeerId { get; set; } = string.Empty;

    public bool ListenForPeers { get; set; } = true;

    public bool RequestScrape { get; set; } = true;

    public bool IgnoreFailureReason { get; set; }

    public bool LogEnabled { get; set; } = true;

    public StopCondition StopCondition { get; set; } = StopCondition.Never;

    public double StopValue { get; set; }

    public ProxySettings Proxy { get; set; } = ProxySettings.Direct;

    public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
