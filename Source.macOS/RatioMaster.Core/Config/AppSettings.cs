namespace RatioMaster.Core.Config;

using System.Text.Json;
using System.Text.Json.Serialization;

using RatioMaster.Core.Emulation;
using RatioMaster.Core.Engine;
using RatioMaster.Core.Net;

/// <summary>Persisted defaults, the JSON replacement for the Windows registry key.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ClientName { get; set; } = ClientCatalog.DefaultClientName;

    public int UploadRateKiB { get; set; } = 60;

    public int DownloadRateKiB { get; set; } = 30;

    public int IntervalSeconds { get; set; } = 1800;

    public double FinishedPercent { get; set; }

    public string LastTorrentDirectory { get; set; } = string.Empty;

    public bool ListenForPeers { get; set; } = true;

    public bool RequestScrape { get; set; } = true;

    public bool IgnoreFailureReason { get; set; }

    public bool LogEnabled { get; set; } = true;

    public bool GenerateNewIdentifiers { get; set; } = true;

    public bool RandomiseUpload { get; set; } = true;

    public int RandomUploadMinKiB { get; set; } = 1;

    public int RandomUploadMaxKiB { get; set; } = 10;

    public bool RandomiseDownload { get; set; } = true;

    public int RandomDownloadMinKiB { get; set; } = 1;

    public int RandomDownloadMaxKiB { get; set; } = 10;

    public bool RerollUpload { get; set; }

    public int RerollUploadMinKiB { get; set; } = 10;

    public int RerollUploadMaxKiB { get; set; } = 50;

    public bool RerollDownload { get; set; }

    public int RerollDownloadMinKiB { get; set; } = 10;

    public int RerollDownloadMaxKiB { get; set; } = 100;

    public string CustomKey { get; set; } = string.Empty;

    public string CustomPeerId { get; set; } = string.Empty;

    public string Port { get; set; } = string.Empty;

    public string NumWant { get; set; } = string.Empty;

    public StopCondition StopCondition { get; set; } = StopCondition.Never;

    public double StopValue { get; set; }

    public ProxyKind ProxyKind { get; set; } = ProxyKind.None;

    public string ProxyHost { get; set; } = string.Empty;

    public string ProxyPort { get; set; } = string.Empty;

    public string ProxyUser { get; set; } = string.Empty;

    public string ProxyPassword { get; set; } = string.Empty;

    public bool Use24HourClock { get; set; } = true;

    public bool ShowInMenuBar { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable settings file must never block start-up.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            AppPaths.EnsureDataDirectory();
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public ProxySettings ToProxySettings() => new()
    {
        Kind = this.ProxyKind,
        Host = this.ProxyHost,
        Port = int.TryParse(this.ProxyPort, out var port) ? port : 0,
        User = this.ProxyUser,
        Password = this.ProxyPassword,
    };

    /// <summary>Projects the stored defaults onto a fresh session.</summary>
    public void ApplyTo(SessionOptions options)
    {
        options.ClientName = this.ClientName;
        options.UploadRateBytes = (long)this.UploadRateKiB * 1024;
        options.DownloadRateBytes = (long)this.DownloadRateKiB * 1024;
        options.IntervalSeconds = this.IntervalSeconds;
        options.FinishedPercent = this.FinishedPercent;
        options.ListenForPeers = this.ListenForPeers;
        options.RequestScrape = this.RequestScrape;
        options.IgnoreFailureReason = this.IgnoreFailureReason;
        options.LogEnabled = this.LogEnabled;
        options.Port = this.Port;
        options.NumWant = this.NumWant;
        options.StopCondition = this.StopCondition;
        options.StopValue = this.StopValue;
        options.Proxy = this.ToProxySettings();

        options.UploadJitter = new RandomRange { Enabled = this.RandomiseUpload, MinKiB = this.RandomUploadMinKiB, MaxKiB = this.RandomUploadMaxKiB };
        options.DownloadJitter = new RandomRange { Enabled = this.RandomiseDownload, MinKiB = this.RandomDownloadMinKiB, MaxKiB = this.RandomDownloadMaxKiB };
        options.RerollUpload = new RandomRange { Enabled = this.RerollUpload, MinKiB = this.RerollUploadMinKiB, MaxKiB = this.RerollUploadMaxKiB };
        options.RerollDownload = new RandomRange { Enabled = this.RerollDownload, MinKiB = this.RerollDownloadMinKiB, MaxKiB = this.RerollDownloadMaxKiB };

        if (!this.GenerateNewIdentifiers)
        {
            options.CustomKey = this.CustomKey;
            options.CustomPeerId = this.CustomPeerId;
        }
    }
}
