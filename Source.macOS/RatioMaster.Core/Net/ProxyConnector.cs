namespace RatioMaster.Core.Net;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Opens a TCP tunnel to the tracker, optionally through an HTTP CONNECT or
/// SOCKS 4/4a/5 proxy.
/// </summary>
/// <remarks>
/// Replaces the 8k-line BytesRoads socket library the Windows build carried. Only
/// the CONNECT path is needed here: RatioMaster never does anything but open one
/// outbound stream per announce.
/// </remarks>
public static class ProxyConnector
{
    public static async Task<Stream> ConnectAsync(string host, int port, ProxySettings proxy, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var token = timeoutSource.Token;

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            if (!proxy.IsEnabled)
            {
                await socket.ConnectAsync(host, port, token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }

            await socket.ConnectAsync(proxy.Host, proxy.Port, token).ConfigureAwait(false);
            var stream = new NetworkStream(socket, ownsSocket: true);
            try
            {
                switch (proxy.Kind)
                {
                    case ProxyKind.Http:
                        await HttpConnectAsync(stream, host, port, proxy, token).ConfigureAwait(false);
                        break;
                    case ProxyKind.Socks4:
                    case ProxyKind.Socks4a:
                        await Socks4Async(stream, host, port, proxy, token).ConfigureAwait(false);
                        break;
                    case ProxyKind.Socks5:
                        await Socks5Async(stream, host, port, proxy, token).ConfigureAwait(false);
                        break;
                }

                return stream;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task HttpConnectAsync(Stream stream, string host, int port, ProxySettings proxy, CancellationToken token)
    {
        var request = new StringBuilder()
            .Append($"CONNECT {host}:{port} HTTP/1.1\r\n")
            .Append($"Host: {host}:{port}\r\n")
            .Append("Proxy-Connection: Keep-Alive\r\n");

        if (proxy.User.Length > 0)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{proxy.User}:{proxy.Password}"));
            request.Append($"Proxy-Authorization: Basic {credentials}\r\n");
        }

        request.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(request.ToString());
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);

        var statusLine = await ReadLineAsync(stream, token).ConfigureAwait(false);
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || parts[1] != "200")
        {
            throw new IOException($"HTTP proxy refused the tunnel: {statusLine}");
        }

        // Drain the remaining response headers.
        while ((await ReadLineAsync(stream, token).ConfigureAwait(false)).Length > 0)
        {
        }
    }

    private static async Task Socks4Async(Stream stream, string host, int port, ProxySettings proxy, CancellationToken token)
    {
        var user = Encoding.ASCII.GetBytes(proxy.User);
        byte[] address;
        byte[]? hostName = null;

        if (proxy.Kind == ProxyKind.Socks4a)
        {
            // 0.0.0.x signals "resolve this hostname for me".
            address = [0, 0, 0, 1];
            hostName = Encoding.ASCII.GetBytes(host);
        }
        else if (IPAddress.TryParse(host, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            address = parsed.GetAddressBytes();
        }
        else
        {
            var resolved = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, token).ConfigureAwait(false);
            if (resolved.Length == 0)
            {
                throw new IOException($"SOCKS4 needs an IPv4 address and '{host}' did not resolve to one.");
            }

            address = resolved[0].GetAddressBytes();
        }

        var request = new List<byte> { 0x04, 0x01, (byte)(port >> 8), (byte)(port & 0xFF) };
        request.AddRange(address);
        request.AddRange(user);
        request.Add(0x00);
        if (hostName is not null)
        {
            request.AddRange(hostName);
            request.Add(0x00);
        }

        await stream.WriteAsync(request.ToArray(), token).ConfigureAwait(false);

        var response = await ReadExactlyAsync(stream, 8, token).ConfigureAwait(false);
        if (response[1] != 0x5A)
        {
            throw new IOException($"SOCKS4 proxy rejected the request (code 0x{response[1]:X2}).");
        }
    }

    private static async Task Socks5Async(Stream stream, string host, int port, ProxySettings proxy, CancellationToken token)
    {
        var wantsAuth = proxy.User.Length > 0;
        byte[] greeting = wantsAuth ? [0x05, 0x02, 0x00, 0x02] : [0x05, 0x01, 0x00];
        await stream.WriteAsync(greeting, token).ConfigureAwait(false);

        var choice = await ReadExactlyAsync(stream, 2, token).ConfigureAwait(false);
        if (choice[0] != 0x05)
        {
            throw new IOException("Proxy did not answer with SOCKS5.");
        }

        switch (choice[1])
        {
            case 0x00:
                break;
            case 0x02:
                await Socks5AuthenticateAsync(stream, proxy, token).ConfigureAwait(false);
                break;
            case 0xFF:
                throw new IOException("SOCKS5 proxy rejected every offered authentication method.");
            default:
                throw new IOException($"SOCKS5 proxy asked for unsupported authentication method 0x{choice[1]:X2}.");
        }

        var request = new List<byte> { 0x05, 0x01, 0x00 };
        if (IPAddress.TryParse(host, out var ip))
        {
            request.Add(ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x04 : (byte)0x01);
            request.AddRange(ip.GetAddressBytes());
        }
        else
        {
            var name = Encoding.ASCII.GetBytes(host);
            if (name.Length > 255)
            {
                throw new IOException("Host name is too long for SOCKS5.");
            }

            request.Add(0x03);
            request.Add((byte)name.Length);
            request.AddRange(name);
        }

        request.Add((byte)(port >> 8));
        request.Add((byte)(port & 0xFF));
        await stream.WriteAsync(request.ToArray(), token).ConfigureAwait(false);

        var reply = await ReadExactlyAsync(stream, 4, token).ConfigureAwait(false);
        if (reply[1] != 0x00)
        {
            throw new IOException($"SOCKS5 proxy rejected the request (code 0x{reply[1]:X2}: {DescribeSocks5Error(reply[1])}).");
        }

        // Consume the bound address so the stream is positioned at the payload.
        var toSkip = reply[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => (await ReadExactlyAsync(stream, 1, token).ConfigureAwait(false))[0],
            _ => throw new IOException($"SOCKS5 proxy returned unknown address type 0x{reply[3]:X2}."),
        };

        await ReadExactlyAsync(stream, toSkip + 2, token).ConfigureAwait(false);
    }

    private static async Task Socks5AuthenticateAsync(Stream stream, ProxySettings proxy, CancellationToken token)
    {
        var user = Encoding.UTF8.GetBytes(proxy.User);
        var password = Encoding.UTF8.GetBytes(proxy.Password);
        if (user.Length > 255 || password.Length > 255)
        {
            throw new IOException("SOCKS5 credentials exceed 255 bytes.");
        }

        var packet = new List<byte> { 0x01, (byte)user.Length };
        packet.AddRange(user);
        packet.Add((byte)password.Length);
        packet.AddRange(password);
        await stream.WriteAsync(packet.ToArray(), token).ConfigureAwait(false);

        var result = await ReadExactlyAsync(stream, 2, token).ConfigureAwait(false);
        if (result[1] != 0x00)
        {
            throw new IOException("SOCKS5 proxy rejected the credentials.");
        }
    }

    private static string DescribeSocks5Error(byte code) => code switch
    {
        0x01 => "general failure",
        0x02 => "connection not allowed by ruleset",
        0x03 => "network unreachable",
        0x04 => "host unreachable",
        0x05 => "connection refused",
        0x06 => "TTL expired",
        0x07 => "command not supported",
        0x08 => "address type not supported",
        _ => "unknown",
    };

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken token)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
        return buffer;
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var builder = new StringBuilder();
        var single = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(single, token).ConfigureAwait(false) == 0)
            {
                break;
            }

            if (single[0] == (byte)'\n')
            {
                break;
            }

            if (single[0] != (byte)'\r')
            {
                builder.Append((char)single[0]);
            }
        }

        return builder.ToString();
    }
}
