namespace RatioMaster.Core.Tracker;

using System.Net;

using RatioMaster.Core.BEncode;
using RatioMaster.Core.Emulation;
using RatioMaster.Core.Net;

public sealed record PeerEndPoint(string Address, int Port, string PeerId = "")
{
    public override string ToString() =>
        this.PeerId.Length > 0 ? $"{this.Address}:{this.Port}(PeerID={this.PeerId})" : $"{this.Address}:{this.Port}";
}

public sealed class TrackerResult
{
    public bool Success => this.FailureReason is null && this.Dictionary is not null;

    public string? FailureReason { get; init; }

    public string? WarningMessage { get; init; }

    public BDictionary? Dictionary { get; init; }

    public int? Interval { get; init; }

    public int? Complete { get; init; }

    public int? Incomplete { get; init; }

    public int? Downloaded { get; init; }

    public IReadOnlyList<PeerEndPoint> Peers { get; init; } = [];

    /// <summary>Raw HTTP exchange, surfaced in the log window.</summary>
    public string RequestText { get; init; } = string.Empty;

    public string ResponseHeaders { get; init; } = string.Empty;

    public string ResponseBody { get; init; } = string.Empty;

    public string? TransportError { get; init; }

    public static TrackerResult FromError(string message, string requestText = "") =>
        new() { TransportError = message, RequestText = requestText };
}

public sealed class TrackerClient(ProxySettings proxy, TimeSpan timeout)
{
    private const int MaxRedirects = 5;

    public async Task<TrackerResult> AnnounceAsync(TorrentClient client, AnnounceParameters parameters, AnnounceEvent announceEvent, CancellationToken cancellationToken)
    {
        var url = TrackerRequestBuilder.BuildAnnounceUrl(client, parameters, announceEvent);
        return await this.RequestAsync(client, url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackerResult> ScrapeAsync(TorrentClient client, string trackerUrl, byte[] infoHash, CancellationToken cancellationToken)
    {
        var url = TrackerRequestBuilder.BuildScrapeUrl(trackerUrl, infoHash, client.HashUpperCase);
        if (url is null)
        {
            return TrackerResult.FromError("This tracker does not seem to support scrape.");
        }

        var result = await this.RequestAsync(client, url, cancellationToken).ConfigureAwait(false);
        return result.Dictionary is null ? result : MergeScrapeStats(result, infoHash);
    }

    private async Task<TrackerResult> RequestAsync(TorrentClient client, string url, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return TrackerResult.FromError($"'{url}' is not a valid tracker URL.");
            }

            if (uri.Scheme is not ("http" or "https"))
            {
                return TrackerResult.FromError(
                    $"Unsupported tracker protocol '{uri.Scheme}'. Only HTTP and HTTPS trackers can be emulated.");
            }

            if (!visited.Add(url))
            {
                return TrackerResult.FromError("The tracker is redirecting in a loop.");
            }

            var headers = client.Headers.Replace("{host}", uri.Host, StringComparison.Ordinal);
            var requestText = $"GET {uri.PathAndQuery} {client.HttpProtocol}\r\n{headers}";

            RawHttpResponse response;
            try
            {
                response = await RawHttpClient
                    .SendAsync(uri, client.HttpProtocol, headers, proxy, timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return TrackerResult.FromError($"Timed out after {timeout.TotalSeconds:0} s talking to {uri.Host}.", requestText);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException)
            {
                return TrackerResult.FromError($"{ex.GetType().Name}: {ex.Message}", requestText);
            }

            if (response.IsRedirect && response.Location is { Length: > 0 } location)
            {
                url = Uri.TryCreate(uri, location, out var absolute) ? absolute.ToString() : location;
                continue;
            }

            return Interpret(response, requestText);
        }

        return TrackerResult.FromError($"Gave up after {MaxRedirects} redirects.");
    }

    private static TrackerResult Interpret(RawHttpResponse response, string requestText)
    {
        var bodyText = BValue.Binary.GetString(response.Body);

        if (response.Body.Length == 0)
        {
            return new TrackerResult
            {
                TransportError = "Tracker response is empty.",
                RequestText = requestText,
                ResponseHeaders = response.RawHeaders,
            };
        }

        BDictionary? dictionary = null;
        try
        {
            dictionary = BEncodeParser.Parse(response.Body) as BDictionary;
        }
        catch (BEncodeException)
        {
            // Left null: the caller reports the undecodable body.
        }

        if (dictionary is null)
        {
            return new TrackerResult
            {
                TransportError = response.StatusCode is >= 400
                    ? $"Tracker answered {response.StatusLine}"
                    : "Failed to decode the tracker response.",
                RequestText = requestText,
                ResponseHeaders = response.RawHeaders,
                ResponseBody = bodyText,
            };
        }

        var failure = dictionary.GetString("failure reason");
        var warning = dictionary.GetString("warning message");

        return new TrackerResult
        {
            Dictionary = dictionary,
            FailureReason = failure.Length > 0 ? failure : null,
            WarningMessage = warning.Length > 0 ? warning : null,
            Interval = dictionary.Contains("interval") ? (int)dictionary.GetInt64("interval") : null,
            Complete = dictionary.Contains("complete") ? (int)dictionary.GetInt64("complete") : null,
            Incomplete = dictionary.Contains("incomplete") ? (int)dictionary.GetInt64("incomplete") : null,
            Downloaded = dictionary.Contains("downloaded") ? (int)dictionary.GetInt64("downloaded") : null,
            Peers = ParsePeers(dictionary),
            RequestText = requestText,
            ResponseHeaders = response.RawHeaders,
            ResponseBody = bodyText,
        };
    }

    /// <summary>Lifts the per-torrent counters out of a scrape response's "files" map.</summary>
    private static TrackerResult MergeScrapeStats(TrackerResult result, byte[] infoHash)
    {
        if (result.Dictionary?["files"] is not BDictionary files)
        {
            return result;
        }

        var key = BValue.Binary.GetString(infoHash);
        if (files[key] is not BDictionary stats)
        {
            return result;
        }

        return new TrackerResult
        {
            Dictionary = result.Dictionary,
            FailureReason = result.FailureReason,
            WarningMessage = result.WarningMessage,
            Complete = stats.Contains("complete") ? (int)stats.GetInt64("complete") : null,
            Incomplete = stats.Contains("incomplete") ? (int)stats.GetInt64("incomplete") : null,
            Downloaded = stats.Contains("downloaded") ? (int)stats.GetInt64("downloaded") : null,
            Interval = result.Interval,
            Peers = result.Peers,
            RequestText = result.RequestText,
            ResponseHeaders = result.ResponseHeaders,
            ResponseBody = result.ResponseBody,
        };
    }

    private static IReadOnlyList<PeerEndPoint> ParsePeers(BDictionary dictionary)
    {
        var peers = new List<PeerEndPoint>();

        switch (dictionary["peers"])
        {
            // Compact form (BEP 23): 6 bytes per peer, 4 for IPv4 + 2 for the port.
            case BString compact:
                for (var offset = 0; offset + 6 <= compact.Bytes.Length; offset += 6)
                {
                    var address = new IPAddress(compact.Bytes.AsSpan(offset, 4).ToArray());
                    var port = (compact.Bytes[offset + 4] << 8) | compact.Bytes[offset + 5];
                    peers.Add(new PeerEndPoint(address.ToString(), port));
                }

                break;

            case BList list:
                foreach (var item in list.Items)
                {
                    if (item is BDictionary peer)
                    {
                        peers.Add(new PeerEndPoint(
                            peer.GetString("ip"),
                            (int)peer.GetInt64("port"),
                            peer.GetString("peer id")));
                    }
                }

                break;
        }

        // IPv6 peers arrive in a separate compact key (BEP 7).
        if (dictionary["peers6"] is BString compact6)
        {
            for (var offset = 0; offset + 18 <= compact6.Bytes.Length; offset += 18)
            {
                var address = new IPAddress(compact6.Bytes.AsSpan(offset, 16).ToArray());
                var port = (compact6.Bytes[offset + 16] << 8) | compact6.Bytes[offset + 17];
                peers.Add(new PeerEndPoint($"[{address}]", port));
            }
        }

        return peers;
    }
}
