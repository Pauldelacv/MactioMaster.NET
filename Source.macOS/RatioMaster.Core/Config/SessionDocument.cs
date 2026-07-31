namespace RatioMaster.Core.Config;

using System.Globalization;
using System.Xml.Linq;

using RatioMaster.Core.Emulation;
using RatioMaster.Core.Engine;
using RatioMaster.Core.Net;

public sealed record SessionEntry(string Name, SessionOptions Options);

/// <summary>
/// Reads and writes <c>.session</c> files in the same XML schema the Windows
/// build used, so sessions move between the two versions unchanged.
/// </summary>
public static class SessionDocument
{
    private static readonly string[] StopConditionNames =
    [
        "Never",
        "After time:",
        "When seeders <",
        "When leechers <",
        "When uploaded >",
        "When downloaded >",
        "When leechers/seeders <",
    ];

    private static readonly string[] ProxyNames = ["None", "HTTP", "SOCKS4", "SOCKS4a", "SOCKS5"];

    public static void Save(string path, IEnumerable<SessionEntry> entries)
    {
        var root = new XElement("main");
        foreach (var (name, options) in entries)
        {
            var (client, version) = SplitClientName(options.ClientName);
            root.Add(new XElement(
                "RatioMaster",
                new XElement("Name", name),
                new XElement("Address", options.TorrentPath),
                new XElement("Tracker", options.TrackerUrl),
                new XElement("UploadSpeed", Kib(options.UploadRateBytes)),
                new XElement("UploadRandom", options.UploadJitter.Enabled),
                new XElement("UploadRandMin", options.UploadJitter.MinKiB),
                new XElement("UploadRandMax", options.UploadJitter.MaxKiB),
                new XElement("DownloadSpeed", Kib(options.DownloadRateBytes)),
                new XElement("DownloadRandom", options.DownloadJitter.Enabled),
                new XElement("DownloadRandMin", options.DownloadJitter.MinKiB),
                new XElement("DownloadRandMax", options.DownloadJitter.MaxKiB),
                new XElement("Client", client),
                new XElement("Version", version),
                new XElement("Finished", options.FinishedPercent.ToString(CultureInfo.InvariantCulture)),
                new XElement("StopType", StopConditionNames[(int)options.StopCondition]),
                new XElement("StopValue", options.StopValue.ToString(CultureInfo.InvariantCulture)),
                new XElement("Port", options.Port),
                new XElement("UseTCP", options.ListenForPeers),
                new XElement("UseScrape", options.RequestScrape),
                new XElement("ProxyType", ProxyNames[(int)options.Proxy.Kind]),
                new XElement("ProxyUser", options.Proxy.User),
                new XElement("ProxyPass", options.Proxy.Password),
                new XElement("ProxyHost", options.Proxy.Host),
                new XElement("ProxyPort", options.Proxy.Port == 0 ? string.Empty : options.Proxy.Port.ToString(CultureInfo.InvariantCulture)),
                new XElement("NextUpdateUpload", options.RerollUpload.Enabled),
                new XElement("NextUpdateUploadFrom", options.RerollUpload.MinKiB),
                new XElement("NextUpdateUploadTo", options.RerollUpload.MaxKiB),
                new XElement("NextUpdateDownload", options.RerollDownload.Enabled),
                new XElement("NextUpdateDownloadFrom", options.RerollDownload.MinKiB),
                new XElement("NextUpdateDownloadTo", options.RerollDownload.MaxKiB),
                new XElement("IgnoreFailureReason", options.IgnoreFailureReason)));
        }

        new XDocument(root).Save(path);
    }

    public static IReadOnlyList<SessionEntry> Load(string path)
    {
        var document = XDocument.Load(path);
        var entries = new List<SessionEntry>();

        foreach (var node in document.Root?.Elements("RatioMaster") ?? [])
        {
            var options = new SessionOptions
            {
                TorrentPath = Text(node, "Address"),
                TrackerUrl = Text(node, "Tracker"),
                ClientName = JoinClientName(Text(node, "Client"), Text(node, "Version")),
                UploadRateBytes = Number(node, "UploadSpeed", 60) * 1024,
                DownloadRateBytes = Number(node, "DownloadSpeed", 30) * 1024,
                FinishedPercent = Decimal(node, "Finished", 0),
                Port = Text(node, "Port"),
                ListenForPeers = Flag(node, "UseTCP", true),
                RequestScrape = Flag(node, "UseScrape", true),
                IgnoreFailureReason = Flag(node, "IgnoreFailureReason", false),
                StopCondition = ParseStopCondition(Text(node, "StopType")),
                StopValue = Decimal(node, "StopValue", 0),
                UploadJitter = Range(node, "UploadRandom", "UploadRandMin", "UploadRandMax", 1, 10),
                DownloadJitter = Range(node, "DownloadRandom", "DownloadRandMin", "DownloadRandMax", 1, 10),
                RerollUpload = Range(node, "NextUpdateUpload", "NextUpdateUploadFrom", "NextUpdateUploadTo", 10, 50),
                RerollDownload = Range(node, "NextUpdateDownload", "NextUpdateDownloadFrom", "NextUpdateDownloadTo", 10, 100),
                Proxy = new ProxySettings
                {
                    Kind = ParseProxyKind(Text(node, "ProxyType")),
                    Host = Text(node, "ProxyHost"),
                    Port = (int)Number(node, "ProxyPort", 0),
                    User = Text(node, "ProxyUser"),
                    Password = Text(node, "ProxyPass"),
                },
            };

            entries.Add(new SessionEntry(Text(node, "Name"), options));
        }

        return entries;
    }

    /// <summary>Splits "uTorrent 3.3.2" into its family and version parts.</summary>
    public static (string Client, string Version) SplitClientName(string fullName)
    {
        foreach (var family in ClientCatalog.Families)
        {
            if (fullName.StartsWith(family.Name + " ", StringComparison.Ordinal))
            {
                return (family.Name, fullName[(family.Name.Length + 1)..]);
            }
        }

        var separator = fullName.LastIndexOf(' ');
        return separator < 0 ? (fullName, string.Empty) : (fullName[..separator], fullName[(separator + 1)..]);
    }

    public static string JoinClientName(string client, string version) =>
        version.Length == 0 ? client : $"{client} {version}";

    private static long Kib(long bytes) => bytes / 1024;

    private static string Text(XElement node, string name) => node.Element(name)?.Value.Trim() ?? string.Empty;

    private static long Number(XElement node, string name, long fallback) =>
        long.TryParse(Text(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double Decimal(XElement node, string name, double fallback)
    {
        // Sessions written by the Windows build may carry a comma decimal separator.
        var text = Text(node, name).Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static bool Flag(XElement node, string name, bool fallback) =>
        bool.TryParse(Text(node, name), out var value) ? value : fallback;

    private static RandomRange Range(XElement node, string enabledKey, string minKey, string maxKey, int minFallback, int maxFallback) => new()
    {
        Enabled = Flag(node, enabledKey, false),
        MinKiB = (int)Number(node, minKey, minFallback),
        MaxKiB = (int)Number(node, maxKey, maxFallback),
    };

    private static StopCondition ParseStopCondition(string label)
    {
        var index = Array.IndexOf(StopConditionNames, label);
        return index < 0 ? StopCondition.Never : (StopCondition)index;
    }

    private static ProxyKind ParseProxyKind(string label)
    {
        var index = Array.FindIndex(ProxyNames, name => string.Equals(name, label, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? ProxyKind.None : (ProxyKind)index;
    }
}
