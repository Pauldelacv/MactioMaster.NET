namespace RatioMaster.Core.Tracker;

using System.Globalization;
using System.Net;
using System.Net.Sockets;

using RatioMaster.Core.Emulation;

public enum AnnounceEvent
{
    /// <summary>Periodic re-announce: no event parameter is sent.</summary>
    None,
    Started,
    Completed,
    Stopped,
}

public sealed class AnnounceParameters
{
    public required string TrackerUrl { get; init; }

    public required byte[] InfoHash { get; init; }

    public required long Uploaded { get; init; }

    public required long Downloaded { get; init; }

    public required long Left { get; init; }

    public required string Port { get; init; }

    public required string NumWant { get; init; }
}

public static class TrackerRequestBuilder
{
    /// <summary>Reported totals are rounded the way real clients report them.</summary>
    private const long UploadedGranularity = 0x4000;

    private const long DownloadedGranularity = 0x10;

    public static string BuildAnnounceUrl(TorrentClient client, AnnounceParameters parameters, AnnounceEvent announceEvent)
    {
        var uploaded = parameters.Uploaded > 0 ? RoundDown(parameters.Uploaded, UploadedGranularity) : 0;
        var downloaded = parameters.Downloaded > 0 ? RoundDown(parameters.Downloaded, DownloadedGranularity) : 0;
        var left = Math.Max(0, parameters.Left);

        var url = parameters.TrackerUrl;
        url += url.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        var query = client.Query;

        // Two quirks carried over from the original: the local IP block is dropped
        // from the very first announce, and ABC's fixed tracker id is only sent
        // together with the stop event.
        if (announceEvent == AnnounceEvent.Started)
        {
            query = query.Replace("&natmapped=1&localip={localip}", string.Empty, StringComparison.Ordinal);
        }

        if (announceEvent != AnnounceEvent.Stopped)
        {
            query = query.Replace("&trackerid=48", string.Empty, StringComparison.Ordinal);
        }

        var numWant = parameters.NumWant;
        if ((numWant.Length == 0 || numWant == "0") && announceEvent != AnnounceEvent.Stopped)
        {
            numWant = client.DefaultNumWant.ToString(CultureInfo.InvariantCulture);
        }

        // Templates come in two shapes: most embed the whole "&event=x" fragment,
        // Deluge's embeds only the value after "event=". Feeding the fragment into
        // the second shape produced "event=&event=started" in the original.
        var expectsBareValue = query.Contains("event={event}", StringComparison.Ordinal);
        var eventText = announceEvent switch
        {
            AnnounceEvent.Started => expectsBareValue ? "started" : "&event=started",
            AnnounceEvent.Completed => expectsBareValue ? "completed" : "&event=completed",
            AnnounceEvent.Stopped => expectsBareValue ? "stopped" : "&event=stopped",
            _ => string.Empty,
        };

        return url + query
            .Replace("{infohash}", IdGenerator.PercentEncode(parameters.InfoHash, client.HashUpperCase), StringComparison.Ordinal)
            .Replace("{peerid}", client.PeerId, StringComparison.Ordinal)
            .Replace("{port}", parameters.Port, StringComparison.Ordinal)
            .Replace("{uploaded}", uploaded.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{downloaded}", downloaded.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{left}", left.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{event}", eventText, StringComparison.Ordinal)
            .Replace("{numwant}", numWant, StringComparison.Ordinal)
            .Replace("{key}", client.Key, StringComparison.Ordinal)
            .Replace("{localip}", GetLocalIPv4(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Turns an announce URL into its scrape counterpart, or returns <c>null</c>
    /// when the tracker does not follow the conventional naming.
    /// </summary>
    public static string? BuildScrapeUrl(string trackerUrl, byte[] infoHash, bool hashUpperCase)
    {
        var queryStart = trackerUrl.IndexOf('?', StringComparison.Ordinal);
        var path = queryStart < 0 ? trackerUrl : trackerUrl[..queryStart];
        var suffix = queryStart < 0 ? string.Empty : trackerUrl[queryStart..];

        var lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return null;
        }

        var lastSegment = path[(lastSlash + 1)..];
        if (!lastSegment.StartsWith("announce", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var scrapeUrl = path[..(lastSlash + 1)] + "scrape" + lastSegment["announce".Length..] + suffix;
        scrapeUrl += scrapeUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return scrapeUrl + "info_hash=" + IdGenerator.PercentEncode(infoHash, hashUpperCase);
    }

    private static long RoundDown(long value, long granularity) => granularity * (value / granularity);

    private static string GetLocalIPv4()
    {
        try
        {
            // Opening a UDP socket toward a public address picks the interface the
            // OS would actually route through, without sending anything.
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect("8.8.8.8", 65530);
            if (probe.LocalEndPoint is IPEndPoint endpoint)
            {
                return endpoint.Address.ToString();
            }
        }
        catch (SocketException)
        {
        }

        return "127.0.0.1";
    }
}
