namespace RatioMaster.Core.Engine;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Accepts inbound BitTorrent connections and answers the handshake.
/// </summary>
/// <remarks>
/// Trackers and peers that probe the advertised port see an apparently live peer.
/// Nothing beyond the handshake is served: no piece ever changes hands.
/// </remarks>
public sealed class PeerListener : IAsyncDisposable
{
    private static readonly byte[] ProtocolName = Encoding.ASCII.GetBytes("BitTorrent protocol");

    private readonly byte[] infoHash;
    private readonly byte[] peerId;
    private readonly Action<string> log;
    private TcpListener? listener;
    private CancellationTokenSource? cancellation;
    private Task? acceptLoop;

    public PeerListener(byte[] infoHash, string percentEncodedPeerId, Action<string> log)
    {
        this.infoHash = infoHash;

        // The wire protocol wants exactly 20 bytes even if the profile or a
        // hand-typed override disagrees.
        var decoded = DecodePeerId(percentEncodedPeerId);
        this.peerId = new byte[20];
        decoded.AsSpan(0, Math.Min(20, decoded.Length)).CopyTo(this.peerId);

        this.log = log;
    }

    public int Port { get; private set; }

    public bool IsListening => this.listener is not null;

    public bool TryStart(int port)
    {
        if (this.listener is not null)
        {
            return true;
        }

        try
        {
            this.listener = new TcpListener(IPAddress.Any, port);
            this.listener.Start();
            this.Port = ((IPEndPoint)this.listener.LocalEndpoint).Port;
            this.cancellation = new CancellationTokenSource();
            this.acceptLoop = Task.Run(() => this.AcceptLoopAsync(this.cancellation.Token));
            this.log($"Started TCP listener on port {this.Port}");
            return true;
        }
        catch (SocketException ex)
        {
            this.listener = null;
            this.log($"Could not listen on port {port}: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (this.listener is null)
        {
            return;
        }

        if (this.cancellation is not null)
        {
            await this.cancellation.CancelAsync().ConfigureAwait(false);
        }

        this.listener.Stop();
        this.listener = null;

        if (this.acceptLoop is not null)
        {
            try
            {
                await this.acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            this.acceptLoop = null;
        }

        this.cancellation?.Dispose();
        this.cancellation = null;
        this.log("TCP listener closed");
    }

    public async ValueTask DisposeAsync() => await this.StopAsync().ConfigureAwait(false);

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        var current = this.listener;
        while (current is not null && !token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await current.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = this.HandleAsync(client, token);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();

                var request = new byte[68];
                await stream.ReadExactlyAsync(request, token).ConfigureAwait(false);

                var wantsThisTorrent =
                    request[0] == ProtocolName.Length &&
                    request.AsSpan(1, ProtocolName.Length).SequenceEqual(ProtocolName) &&
                    request.AsSpan(28, 20).SequenceEqual(this.infoHash);

                if (!wantsThisTorrent)
                {
                    return;
                }

                await stream.WriteAsync(this.BuildHandshake(), token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or EndOfStreamException)
            {
                // A peer that hangs up mid-handshake is routine; nothing to report.
            }
        }
    }

    private byte[] BuildHandshake()
    {
        var handshake = new byte[68];
        handshake[0] = (byte)ProtocolName.Length;
        ProtocolName.CopyTo(handshake, 1);

        // Bytes 20-27 stay zero: no protocol extensions are advertised.
        this.infoHash.CopyTo(handshake, 28);
        this.peerId.CopyTo(handshake, 48);
        return handshake;
    }

    /// <summary>
    /// Turns the percent-encoded peer id used in tracker URLs back into raw bytes.
    /// A well-formed profile yields exactly 20.
    /// </summary>
    public static byte[] DecodePeerId(string encoded)
    {
        var bytes = new List<byte>(20);
        for (var i = 0; i < encoded.Length; i++)
        {
            if (encoded[i] == '%' && i + 2 < encoded.Length &&
                byte.TryParse(encoded.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var decoded))
            {
                bytes.Add(decoded);
                i += 2;
            }
            else
            {
                bytes.Add((byte)encoded[i]);
            }
        }

        return [.. bytes];
    }
}
