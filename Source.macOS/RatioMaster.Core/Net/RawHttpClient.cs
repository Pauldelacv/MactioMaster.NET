namespace RatioMaster.Core.Net;

using System.IO.Compression;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;

public sealed record RawHttpResponse(int StatusCode, string StatusLine, string RawHeaders, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public string? Location => this.Headers.TryGetValue("location", out var value) ? value : null;

    public bool IsRedirect => this.StatusCode is 301 or 302 or 303 or 307 or 308;
}

/// <summary>
/// Minimal HTTP/1.x client that writes a caller-supplied header block verbatim.
/// </summary>
/// <remarks>
/// <see cref="HttpClient"/> cannot be used here: it normalises, reorders and
/// supplements headers, and the exact byte layout of the request *is* the client
/// fingerprint being emulated. Unlike the Windows original this also speaks TLS,
/// so <c>https://</c> trackers work.
/// </remarks>
public static class RawHttpClient
{
    private const int MaxResponseBytes = 8 * 1024 * 1024;

    public static async Task<RawHttpResponse> SendAsync(
        Uri uri,
        string httpVersion,
        string headerBlock,
        ProxySettings proxy,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var isSecure = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        var port = uri.IsDefaultPort ? (isSecure ? 443 : 80) : uri.Port;

        var stream = await ProxyConnector.ConnectAsync(uri.Host, port, proxy, timeout, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            Stream transport = stream;
            SslStream? tls = null;
            try
            {
                if (isSecure)
                {
                    tls = new SslStream(stream, leaveInnerStreamOpen: true);
                    await tls.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = uri.Host,
                            EnabledSslProtocols = SslProtocols.None, // let the OS pick
                        },
                        cancellationToken).ConfigureAwait(false);
                    transport = tls;
                }

                // The header block comes from the emulated client profile and must end
                // with a blank line. Several profiles in the original catalog were missing
                // the trailing CRLF, which produced a request the tracker never answered.
                var normalisedHeaders = headerBlock.EndsWith("\r\n", StringComparison.Ordinal)
                    ? headerBlock
                    : headerBlock.TrimEnd('\r', '\n') + "\r\n";

                var request = $"GET {uri.PathAndQuery} {httpVersion}\r\n{normalisedHeaders}\r\n";
                var requestBytes = Encoding.Latin1.GetBytes(request);
                await transport.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
                await transport.FlushAsync(cancellationToken).ConfigureAwait(false);

                return await ReadResponseAsync(transport, timeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (tls is not null)
                {
                    await tls.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task<RawHttpResponse> ReadResponseAsync(Stream transport, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var token = timeoutSource.Token;

        var reader = new ByteReader(transport);
        var statusLine = await reader.ReadLineAsync(token).ConfigureAwait(false);
        var headerLines = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line.Length == 0)
            {
                break;
            }

            headerLines.Add(line);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in headerLines)
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        var statusCode = 0;
        var statusParts = statusLine.Split(' ', 3);
        if (statusParts.Length >= 2)
        {
            int.TryParse(statusParts[1], out statusCode);
        }

        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding) &&
            transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await reader.ReadChunkedAsync(token).ConfigureAwait(false);
        }
        else if (headers.TryGetValue("Content-Length", out var lengthHeader) && int.TryParse(lengthHeader, out var length))
        {
            body = await reader.ReadExactlyAsync(Math.Min(length, MaxResponseBytes), token).ConfigureAwait(false);
        }
        else
        {
            body = await reader.ReadToEndAsync(MaxResponseBytes, token).ConfigureAwait(false);
        }

        if (headers.TryGetValue("Content-Encoding", out var contentEncoding))
        {
            body = Decompress(body, contentEncoding);
        }

        var rawHeaders = statusLine + "\r\n" + string.Join("\r\n", headerLines);
        return new RawHttpResponse(statusCode, statusLine, rawHeaders, headers, body);
    }

    private static byte[] Decompress(byte[] body, string contentEncoding)
    {
        if (body.Length == 0)
        {
            return body;
        }

        try
        {
            using var source = new MemoryStream(body);
            using Stream decompressor = contentEncoding.ToLowerInvariant() switch
            {
                "gzip" or "x-gzip" => new GZipStream(source, CompressionMode.Decompress),
                "deflate" => new DeflateStream(source, CompressionMode.Decompress),
                "br" => new BrotliStream(source, CompressionMode.Decompress),
                _ => new MemoryStream(body),
            };

            using var output = new MemoryStream();
            decompressor.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            // Some trackers announce gzip but answer in plain text.
            return body;
        }
    }

    private sealed class ByteReader(Stream stream)
    {
        private readonly byte[] buffer = new byte[16 * 1024];
        private int start;
        private int end;

        internal async Task<string> ReadLineAsync(CancellationToken token)
        {
            var line = new List<byte>(128);
            while (true)
            {
                if (this.start == this.end && !await this.FillAsync(token).ConfigureAwait(false))
                {
                    break;
                }

                var current = this.buffer[this.start++];
                if (current == (byte)'\n')
                {
                    break;
                }

                if (current != (byte)'\r')
                {
                    line.Add(current);
                }
            }

            return Encoding.Latin1.GetString(line.ToArray());
        }

        internal async Task<byte[]> ReadExactlyAsync(int count, CancellationToken token)
        {
            var result = new byte[count];
            var filled = 0;
            while (filled < count)
            {
                if (this.start == this.end && !await this.FillAsync(token).ConfigureAwait(false))
                {
                    Array.Resize(ref result, filled);
                    break;
                }

                var available = Math.Min(this.end - this.start, count - filled);
                Buffer.BlockCopy(this.buffer, this.start, result, filled, available);
                this.start += available;
                filled += available;
            }

            return result;
        }

        internal async Task<byte[]> ReadToEndAsync(int limit, CancellationToken token)
        {
            using var output = new MemoryStream();
            while (output.Length < limit)
            {
                if (this.start == this.end && !await this.FillAsync(token).ConfigureAwait(false))
                {
                    break;
                }

                output.Write(this.buffer, this.start, this.end - this.start);
                this.start = this.end;
            }

            return output.ToArray();
        }

        internal async Task<byte[]> ReadChunkedAsync(CancellationToken token)
        {
            using var output = new MemoryStream();
            while (true)
            {
                var header = await this.ReadLineAsync(token).ConfigureAwait(false);
                var sizeText = header.Split(';', 2)[0].Trim();
                if (sizeText.Length == 0 ||
                    !int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var size) ||
                    size <= 0)
                {
                    break;
                }

                var chunk = await this.ReadExactlyAsync(size, token).ConfigureAwait(false);
                output.Write(chunk, 0, chunk.Length);
                await this.ReadLineAsync(token).ConfigureAwait(false); // trailing CRLF

                if (output.Length > MaxResponseBytes)
                {
                    break;
                }
            }

            return output.ToArray();
        }

        private async Task<bool> FillAsync(CancellationToken token)
        {
            this.start = 0;
            this.end = await stream.ReadAsync(this.buffer, token).ConfigureAwait(false);
            return this.end > 0;
        }
    }
}
