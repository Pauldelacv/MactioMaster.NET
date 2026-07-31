namespace RatioMaster.Core.Engine;

public sealed class SessionStats
{
    public long Uploaded { get; internal set; }

    public long Downloaded { get; internal set; }

    public long Left { get; internal set; }

    public long TotalSize { get; internal set; }

    public int Seeders { get; internal set; } = -1;

    public int Leechers { get; internal set; } = -1;

    public int FinishedCount { get; internal set; } = -1;

    /// <summary>Seconds until the next announce.</summary>
    public int SecondsToNextAnnounce { get; internal set; }

    public int ElapsedSeconds { get; internal set; }

    public int IntervalSeconds { get; internal set; }

    public bool IsSeeding { get; internal set; }

    public double PercentComplete => this.TotalSize <= 0
        ? 100
        : Math.Clamp((this.TotalSize - this.Left) * 100.0 / this.TotalSize, 0, 100);

    public double? Ratio => this.Downloaded < 100 * 1024 ? null : this.Uploaded / (double)this.Downloaded;
}
