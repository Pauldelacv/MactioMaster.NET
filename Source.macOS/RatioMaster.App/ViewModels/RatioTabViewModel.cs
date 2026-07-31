namespace RatioMaster.App.ViewModels;

using System.Collections.ObjectModel;
using System.Text;

using Avalonia.Threading;

using RatioMaster.Core.Config;
using RatioMaster.Core.Emulation;
using RatioMaster.Core.Engine;
using RatioMaster.Core.Net;
using RatioMaster.Core.Torrents;
using RatioMaster.Core.Util;

/// <summary>One tab: a single torrent being announced.</summary>
public sealed class RatioTabViewModel : ViewModelBase, IAsyncDisposable
{
    private const int MaxLogLines = 2000;

    private readonly RatioSession session = new();
    private readonly StringBuilder log = new();
    private readonly Queue<int> logLineLengths = new();
    private readonly bool use24HourClock;

    private string title = "RM 1";
    private string torrentPath = string.Empty;
    private string trackerUrl = string.Empty;
    private string infoHash = string.Empty;
    private string torrentSize = string.Empty;
    private string torrentName = string.Empty;
    private string clientFamily = "uTorrent";
    private string clientVersion = "3.3.2";
    private string identifierStatus = "Identifiers: not generated yet";
    private bool generateNewIdentifiers = true;
    private bool isRunning;

    public RatioTabViewModel(AppSettings settings)
    {
        this.use24HourClock = settings.Use24HourClock;
        settings.ApplyTo(this.session.Options);

        var (family, version) = SessionDocument.SplitClientName(settings.ClientName);
        this.clientFamily = this.Families.Contains(family) ? family : "uTorrent";
        this.RefreshVersions();
        this.clientVersion = this.Versions.Contains(version) ? version : this.Versions.FirstOrDefault() ?? string.Empty;

        this.generateNewIdentifiers = settings.GenerateNewIdentifiers;
        this.CustomKey = settings.CustomKey;
        this.CustomPeerId = settings.CustomPeerId;

        this.session.LogLine += this.OnLogLine;
        this.session.StatsUpdated += this.OnStatsUpdated;
        this.session.StateChanged += this.OnStateChanged;

        this.StartCommand = new AsyncRelayCommand(this.StartAsync, () => !this.IsRunning && this.HasTorrent);
        this.StopCommand = new AsyncRelayCommand(this.StopAsync, () => this.IsRunning);
        this.AnnounceNowCommand = new RelayCommand(this.session.AnnounceNow, () => this.IsRunning);
        this.GenerateIdentifiersCommand = new RelayCommand(this.GenerateIdentifiers);
        this.ClearLogCommand = new RelayCommand(this.ClearLog);

        this.GenerateIdentifiers();
    }

    public event Action? LogChanged;

    // ---------------- Identity ----------------

    public string Title
    {
        get => this.title;
        set => this.SetField(ref this.title, value);
    }

    public SessionOptions Options => this.session.Options;

    public bool HasTorrent => this.session.Torrent is not null;

    // ---------------- Torrent ----------------

    public string TorrentPath
    {
        get => this.torrentPath;
        private set => this.SetField(ref this.torrentPath, value);
    }

    public string TorrentName
    {
        get => this.torrentName;
        private set => this.SetField(ref this.torrentName, value);
    }

    public string TrackerUrl
    {
        get => this.trackerUrl;
        set
        {
            if (this.SetField(ref this.trackerUrl, value))
            {
                this.session.Options.TrackerUrl = value;
            }
        }
    }

    public string InfoHash
    {
        get => this.infoHash;
        private set => this.SetField(ref this.infoHash, value);
    }

    public string TorrentSize
    {
        get => this.torrentSize;
        private set => this.SetField(ref this.torrentSize, value);
    }

    // ---------------- Client emulation ----------------

    public IReadOnlyList<string> Families { get; } = [.. ClientCatalog.Families.Select(f => f.Name)];

    public ObservableCollection<string> Versions { get; } = [];

    public string ClientFamily
    {
        get => this.clientFamily;
        set
        {
            if (this.SetField(ref this.clientFamily, value))
            {
                this.RefreshVersions();
                this.ClientVersion = this.Versions.FirstOrDefault() ?? string.Empty;
            }
        }
    }

    public string ClientVersion
    {
        get => this.clientVersion;
        set
        {
            if (this.SetField(ref this.clientVersion, value) && value.Length > 0)
            {
                this.session.Options.ClientName = SessionDocument.JoinClientName(this.clientFamily, value);
                if (this.GenerateNewIdentifiers)
                {
                    this.GenerateIdentifiers();
                }
            }
        }
    }

    public bool GenerateNewIdentifiers
    {
        get => this.generateNewIdentifiers;
        set
        {
            if (this.SetField(ref this.generateNewIdentifiers, value) && value)
            {
                this.GenerateIdentifiers();
            }
        }
    }

    public string CustomKey
    {
        get => this.session.Options.CustomKey;
        set
        {
            this.session.Options.CustomKey = value;
            this.Raise();
        }
    }

    public string CustomPeerId
    {
        get => this.session.Options.CustomPeerId;
        set
        {
            this.session.Options.CustomPeerId = value;
            this.Raise();
        }
    }

    public string Port
    {
        get => this.session.Options.Port;
        set
        {
            this.session.Options.Port = value;
            this.Raise();
        }
    }

    public string NumWant
    {
        get => this.session.Options.NumWant;
        set
        {
            this.session.Options.NumWant = value;
            this.Raise();
        }
    }

    public string IdentifierStatus
    {
        get => this.identifierStatus;
        private set => this.SetField(ref this.identifierStatus, value);
    }

    // ---------------- Rates ----------------

    public decimal UploadRateKiB
    {
        get => this.session.Options.UploadRateBytes / 1024m;
        set
        {
            this.session.Options.UploadRateBytes = (long)(Math.Max(0, value) * 1024);
            this.Raise();
        }
    }

    public decimal DownloadRateKiB
    {
        get => this.session.Options.DownloadRateBytes / 1024m;
        set
        {
            this.session.Options.DownloadRateBytes = (long)(Math.Max(0, value) * 1024);
            this.Raise();
        }
    }

    public decimal IntervalSeconds
    {
        get => this.session.Options.IntervalSeconds;
        set
        {
            this.session.Options.IntervalSeconds = (int)Math.Clamp(value, 60, 3600);
            this.Raise();
        }
    }

    public decimal FinishedPercent
    {
        get => (decimal)this.session.Options.FinishedPercent;
        set
        {
            this.session.Options.FinishedPercent = (double)Math.Clamp(value, 0, 100);
            this.Raise();
        }
    }

    public bool RandomiseUpload
    {
        get => this.session.Options.UploadJitter.Enabled;
        set
        {
            this.session.Options.UploadJitter.Enabled = value;
            this.Raise();
        }
    }

    public decimal RandomUploadMin
    {
        get => this.session.Options.UploadJitter.MinKiB;
        set
        {
            this.session.Options.UploadJitter.MinKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    public decimal RandomUploadMax
    {
        get => this.session.Options.UploadJitter.MaxKiB;
        set
        {
            this.session.Options.UploadJitter.MaxKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    public bool RandomiseDownload
    {
        get => this.session.Options.DownloadJitter.Enabled;
        set
        {
            this.session.Options.DownloadJitter.Enabled = value;
            this.Raise();
        }
    }

    public decimal RandomDownloadMin
    {
        get => this.session.Options.DownloadJitter.MinKiB;
        set
        {
            this.session.Options.DownloadJitter.MinKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    public decimal RandomDownloadMax
    {
        get => this.session.Options.DownloadJitter.MaxKiB;
        set
        {
            this.session.Options.DownloadJitter.MaxKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    public bool RerollUpload
    {
        get => this.session.Options.RerollUpload.Enabled;
        set
        {
            this.session.Options.RerollUpload.Enabled = value;
            this.Raise();
        }
    }

    public decimal RerollUploadMin
    {
        get => this.session.Options.RerollUpload.MinKiB;
        set
        {
            this.session.Options.RerollUpload.MinKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    public decimal RerollUploadMax
    {
        get => this.session.Options.RerollUpload.MaxKiB;
        set
        {
            this.session.Options.RerollUpload.MaxKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    public bool RerollDownload
    {
        get => this.session.Options.RerollDownload.Enabled;
        set
        {
            this.session.Options.RerollDownload.Enabled = value;
            this.Raise();
        }
    }

    public decimal RerollDownloadMin
    {
        get => this.session.Options.RerollDownload.MinKiB;
        set
        {
            this.session.Options.RerollDownload.MinKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    public decimal RerollDownloadMax
    {
        get => this.session.Options.RerollDownload.MaxKiB;
        set
        {
            this.session.Options.RerollDownload.MaxKiB = (int)Math.Max(0, value);
            this.Raise();
        }
    }

    // ---------------- Options ----------------

    public bool ListenForPeers
    {
        get => this.session.Options.ListenForPeers;
        set
        {
            this.session.Options.ListenForPeers = value;
            this.Raise();
        }
    }

    public bool RequestScrape
    {
        get => this.session.Options.RequestScrape;
        set
        {
            this.session.Options.RequestScrape = value;
            this.Raise();
        }
    }

    public bool IgnoreFailureReason
    {
        get => this.session.Options.IgnoreFailureReason;
        set
        {
            this.session.Options.IgnoreFailureReason = value;
            this.Raise();
        }
    }

    public bool LogEnabled
    {
        get => this.session.Options.LogEnabled;
        set
        {
            this.session.Options.LogEnabled = value;
            this.Raise();
        }
    }

    public IReadOnlyList<string> StopConditions { get; } =
    [
        "Never",
        "After time (s)",
        "When seeders <",
        "When leechers <",
        "When uploaded > (MB)",
        "When downloaded > (MB)",
        "When leechers/seeders <",
    ];

    public int StopConditionIndex
    {
        get => (int)this.session.Options.StopCondition;
        set
        {
            this.session.Options.StopCondition = (StopCondition)Math.Clamp(value, 0, this.StopConditions.Count - 1);
            this.Raise();
            this.Raise(nameof(this.StopValueEnabled));
        }
    }

    public bool StopValueEnabled => this.session.Options.StopCondition != StopCondition.Never;

    public decimal StopValue
    {
        get => (decimal)this.session.Options.StopValue;
        set
        {
            this.session.Options.StopValue = (double)value;
            this.Raise();
        }
    }

    // ---------------- Proxy ----------------

    public IReadOnlyList<string> ProxyKinds { get; } = ["None", "HTTP", "SOCKS4", "SOCKS4a", "SOCKS5"];

    public int ProxyKindIndex
    {
        get => (int)this.session.Options.Proxy.Kind;
        set
        {
            this.UpdateProxy(p => p with { Kind = (ProxyKind)Math.Clamp(value, 0, 4) });
            this.Raise();
            this.Raise(nameof(this.ProxyEnabled));
        }
    }

    public bool ProxyEnabled => this.session.Options.Proxy.Kind != ProxyKind.None;

    public string ProxyHost
    {
        get => this.session.Options.Proxy.Host;
        set
        {
            this.UpdateProxy(p => p with { Host = value });
            this.Raise();
        }
    }

    public string ProxyPort
    {
        get => this.session.Options.Proxy.Port == 0 ? string.Empty : this.session.Options.Proxy.Port.ToString();
        set
        {
            this.UpdateProxy(p => p with { Port = int.TryParse(value, out var port) ? port : 0 });
            this.Raise();
        }
    }

    public string ProxyUser
    {
        get => this.session.Options.Proxy.User;
        set
        {
            this.UpdateProxy(p => p with { User = value });
            this.Raise();
        }
    }

    public string ProxyPassword
    {
        get => this.session.Options.Proxy.Password;
        set
        {
            this.UpdateProxy(p => p with { Password = value });
            this.Raise();
        }
    }

    // ---------------- Live stats ----------------

    public bool IsRunning
    {
        get => this.isRunning;
        private set
        {
            if (this.SetField(ref this.isRunning, value))
            {
                this.Raise(nameof(this.IsIdle));
                this.Raise(nameof(this.StatusText));
                this.StartCommand.RaiseCanExecuteChanged();
                this.StopCommand.RaiseCanExecuteChanged();
                this.AnnounceNowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !this.IsRunning;

    public string StatusText => this.session.State switch
    {
        SessionState.Running => this.session.Stats.IsSeeding ? "Seeding" : "Leeching",
        SessionState.Stopping => "Stopping…",
        _ => "Stopped",
    };

    public string UploadedText => Format.FileSize(this.session.Stats.Uploaded);

    public string DownloadedText => Format.FileSize(this.session.Stats.Downloaded);

    public string RatioText => Format.Ratio(this.session.Stats.Ratio);

    public string SeedersText => this.session.Stats.Seeders < 0 ? "—" : this.session.Stats.Seeders.ToString();

    public string LeechersText => this.session.Stats.Leechers < 0 ? "—" : this.session.Stats.Leechers.ToString();

    public string FinishedCountText => this.session.Stats.FinishedCount < 0 ? "—" : this.session.Stats.FinishedCount.ToString();

    public string NextAnnounceText => this.IsRunning ? Format.Duration(this.session.Stats.SecondsToNextAnnounce) : "—";

    public string ElapsedText => Format.Duration(this.session.Stats.ElapsedSeconds);

    public double ProgressPercent => this.session.Stats.PercentComplete;

    public string ProgressText => Format.Percent(this.session.Stats.PercentComplete) + " %";

    public string Log => this.log.ToString();

    // ---------------- Commands ----------------

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public RelayCommand AnnounceNowCommand { get; }

    public RelayCommand GenerateIdentifiersCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    // ---------------- Operations ----------------

    public void LoadTorrent(string path)
    {
        try
        {
            var metadata = this.session.LoadTorrent(path);
            this.TorrentPath = path;
            this.TorrentName = metadata.Name;
            this.TrackerUrl = metadata.Announce;
            this.InfoHash = metadata.InfoHashHex;
            this.TorrentSize = Format.FileSize((long)metadata.TotalLength);
            this.Title = metadata.Name.Length > 24 ? metadata.Name[..24] + "…" : metadata.Name;
            this.Raise(nameof(this.HasTorrent));
            this.StartCommand.RaiseCanExecuteChanged();
            this.AppendLog($"Loaded {Path.GetFileName(path)} — {metadata.Files.Count} file(s), {this.TorrentSize}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Core.BEncode.BEncodeException)
        {
            this.AppendLog("Could not open the torrent: " + ex.Message);
        }
    }

    /// <summary>Restores a tab from a .session entry.</summary>
    public void ApplyOptions(SessionOptions options)
    {
        var target = this.session.Options;
        target.TrackerUrl = options.TrackerUrl;
        target.ClientName = options.ClientName;
        target.UploadRateBytes = options.UploadRateBytes;
        target.DownloadRateBytes = options.DownloadRateBytes;
        target.FinishedPercent = options.FinishedPercent;
        target.Port = options.Port;
        target.ListenForPeers = options.ListenForPeers;
        target.RequestScrape = options.RequestScrape;
        target.IgnoreFailureReason = options.IgnoreFailureReason;
        target.StopCondition = options.StopCondition;
        target.StopValue = options.StopValue;
        target.UploadJitter = options.UploadJitter;
        target.DownloadJitter = options.DownloadJitter;
        target.RerollUpload = options.RerollUpload;
        target.RerollDownload = options.RerollDownload;
        target.Proxy = options.Proxy;

        var (family, version) = SessionDocument.SplitClientName(options.ClientName);
        if (this.Families.Contains(family))
        {
            this.ClientFamily = family;
            if (this.Versions.Contains(version))
            {
                this.ClientVersion = version;
            }
        }

        if (options.TorrentPath.Length > 0 && File.Exists(options.TorrentPath))
        {
            this.LoadTorrent(options.TorrentPath);
            this.TrackerUrl = options.TrackerUrl;
        }

        this.RaiseAll();
    }

    public void ApplyToSettings(AppSettings settings)
    {
        settings.ClientName = this.session.Options.ClientName;
        settings.UploadRateKiB = (int)this.UploadRateKiB;
        settings.DownloadRateKiB = (int)this.DownloadRateKiB;
        settings.IntervalSeconds = this.session.Options.IntervalSeconds;
        settings.FinishedPercent = this.session.Options.FinishedPercent;
        settings.ListenForPeers = this.ListenForPeers;
        settings.RequestScrape = this.RequestScrape;
        settings.IgnoreFailureReason = this.IgnoreFailureReason;
        settings.LogEnabled = this.LogEnabled;
        settings.GenerateNewIdentifiers = this.GenerateNewIdentifiers;
        settings.CustomKey = this.CustomKey;
        settings.CustomPeerId = this.CustomPeerId;
        settings.Port = this.Port;
        settings.NumWant = this.NumWant;
        settings.StopCondition = this.session.Options.StopCondition;
        settings.StopValue = this.session.Options.StopValue;

        settings.RandomiseUpload = this.RandomiseUpload;
        settings.RandomUploadMinKiB = (int)this.RandomUploadMin;
        settings.RandomUploadMaxKiB = (int)this.RandomUploadMax;
        settings.RandomiseDownload = this.RandomiseDownload;
        settings.RandomDownloadMinKiB = (int)this.RandomDownloadMin;
        settings.RandomDownloadMaxKiB = (int)this.RandomDownloadMax;
        settings.RerollUpload = this.RerollUpload;
        settings.RerollUploadMinKiB = (int)this.RerollUploadMin;
        settings.RerollUploadMaxKiB = (int)this.RerollUploadMax;
        settings.RerollDownload = this.RerollDownload;
        settings.RerollDownloadMinKiB = (int)this.RerollDownloadMin;
        settings.RerollDownloadMaxKiB = (int)this.RerollDownloadMax;

        var proxy = this.session.Options.Proxy;
        settings.ProxyKind = proxy.Kind;
        settings.ProxyHost = proxy.Host;
        settings.ProxyPort = proxy.Port == 0 ? string.Empty : proxy.Port.ToString();
        settings.ProxyUser = proxy.User;
        settings.ProxyPassword = proxy.Password;
    }

    public Task StartAsync() => this.session.StartAsync();

    public Task StopAsync() => this.session.StopAsync();

    public void SetUploadRate(int kib) => this.UploadRateKiB = kib;

    public void SetDownloadRate(int kib) => this.DownloadRateKiB = kib;

    public string GetLogText() => this.log.ToString();

    public async ValueTask DisposeAsync() => await this.session.DisposeAsync().ConfigureAwait(false);

    // ---------------- Internals ----------------

    private void UpdateProxy(Func<ProxySettings, ProxySettings> update) =>
        this.session.Options.Proxy = update(this.session.Options.Proxy);

    private void RefreshVersions()
    {
        this.Versions.Clear();
        var family = ClientCatalog.Families.FirstOrDefault(f => f.Name == this.clientFamily);
        foreach (var version in family?.Versions ?? [])
        {
            this.Versions.Add(version);
        }
    }

    private void GenerateIdentifiers()
    {
        var name = SessionDocument.JoinClientName(this.clientFamily, this.clientVersion);
        this.session.Options.ClientName = name;

        var probe = ClientCatalog.Create(name);
        this.CustomKey = probe.Key;
        this.CustomPeerId = probe.PeerId;

        if (this.Port.Length == 0)
        {
            this.Port = IdGenerator.RandomPort().ToString();
        }

        if (this.NumWant.Length == 0)
        {
            this.NumWant = probe.DefaultNumWant.ToString();
        }

        this.IdentifierStatus = $"Identifiers: generated for {name}";
    }

    private void ClearLog()
    {
        this.log.Clear();
        this.logLineLengths.Clear();
        this.Raise(nameof(this.Log));
        this.LogChanged?.Invoke();
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString(this.use24HourClock ? "HH:mm:ss" : "hh:mm:ss tt");
        var entry = $"[{stamp}] {line}{Environment.NewLine}";

        this.log.Append(entry);
        this.logLineLengths.Enqueue(entry.Length);
        while (this.logLineLengths.Count > MaxLogLines)
        {
            this.log.Remove(0, this.logLineLengths.Dequeue());
        }

        this.Raise(nameof(this.Log));
        this.LogChanged?.Invoke();
    }

    private void OnLogLine(string line) => Dispatcher.UIThread.Post(() => this.AppendLog(line));

    private void OnStatsUpdated() => Dispatcher.UIThread.Post(this.RaiseStats);

    private void OnStateChanged(SessionState state) => Dispatcher.UIThread.Post(() =>
    {
        this.IsRunning = state == SessionState.Running;
        this.Raise(nameof(this.StatusText));
    });

    private void RaiseStats()
    {
        this.Raise(nameof(this.UploadedText));
        this.Raise(nameof(this.DownloadedText));
        this.Raise(nameof(this.RatioText));
        this.Raise(nameof(this.SeedersText));
        this.Raise(nameof(this.LeechersText));
        this.Raise(nameof(this.FinishedCountText));
        this.Raise(nameof(this.NextAnnounceText));
        this.Raise(nameof(this.ElapsedText));
        this.Raise(nameof(this.ProgressPercent));
        this.Raise(nameof(this.ProgressText));
        this.Raise(nameof(this.StatusText));
    }

    private void RaiseAll()
    {
        this.Raise(nameof(this.UploadRateKiB));
        this.Raise(nameof(this.DownloadRateKiB));
        this.Raise(nameof(this.IntervalSeconds));
        this.Raise(nameof(this.FinishedPercent));
        this.Raise(nameof(this.Port));
        this.Raise(nameof(this.NumWant));
        this.Raise(nameof(this.ListenForPeers));
        this.Raise(nameof(this.RequestScrape));
        this.Raise(nameof(this.IgnoreFailureReason));
        this.Raise(nameof(this.StopConditionIndex));
        this.Raise(nameof(this.StopValue));
        this.Raise(nameof(this.StopValueEnabled));
        this.Raise(nameof(this.RandomiseUpload));
        this.Raise(nameof(this.RandomUploadMin));
        this.Raise(nameof(this.RandomUploadMax));
        this.Raise(nameof(this.RandomiseDownload));
        this.Raise(nameof(this.RandomDownloadMin));
        this.Raise(nameof(this.RandomDownloadMax));
        this.Raise(nameof(this.RerollUpload));
        this.Raise(nameof(this.RerollUploadMin));
        this.Raise(nameof(this.RerollUploadMax));
        this.Raise(nameof(this.RerollDownload));
        this.Raise(nameof(this.RerollDownloadMin));
        this.Raise(nameof(this.RerollDownloadMax));
        this.Raise(nameof(this.ProxyKindIndex));
        this.Raise(nameof(this.ProxyEnabled));
        this.Raise(nameof(this.ProxyHost));
        this.Raise(nameof(this.ProxyPort));
        this.Raise(nameof(this.ProxyUser));
        this.Raise(nameof(this.ProxyPassword));
        this.RaiseStats();
    }
}
