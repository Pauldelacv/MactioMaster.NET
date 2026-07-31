namespace RatioMaster.Core.Tests;

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// A minimal HTTP server that records the raw request bytes it receives.
/// </summary>
/// <remarks>
/// A raw <see cref="TcpListener"/> rather than <see cref="HttpListener"/>: the
/// point of these tests is to inspect the exact bytes on the wire, and
/// HttpListener needs a URL reservation on Windows.
/// </remarks>
internal sealed class FakeTracker : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task loop;

    internal FakeTracker(Func<string, (byte[] Body, string ExtraHeaders)> respond)
    {
        this.listener = new TcpListener(IPAddress.Loopback, 0);
        this.listener.Start();
        this.Port = ((IPEndPoint)this.listener.LocalEndpoint).Port;
        this.loop = Task.Run(() => this.AcceptAsync(respond, this.cancellation.Token));
    }

    internal int Port { get; }

    internal string BaseUrl => $"http://127.0.0.1:{this.Port}";

    internal ConcurrentQueue<string> Requests { get; } = new();

    internal string WaitForRequest(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (this.Requests.TryDequeue(out var request))
            {
                return request;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException("The tracker received no request.");
    }

    public void Dispose()
    {
        this.cancellation.Cancel();
        this.listener.Stop();
        try
        {
            this.loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        this.cancellation.Dispose();
    }

    internal static byte[] Gzip(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(payload, 0, payload.Length);
        }

        return output.ToArray();
    }

    private async Task AcceptAsync(Func<string, (byte[] Body, string ExtraHeaders)> respond, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await this.listener.AcceptTcpClientAsync(token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (client)
                {
                    var stream = client.GetStream();
                    var request = await ReadRequestHeadAsync(stream, token);
                    this.Requests.Enqueue(request);

                    var (body, extraHeaders) = respond(request);
                    var head = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: text/plain\r\n" +
                        extraHeaders +
                        $"Content-Length: {body.Length}\r\n" +
                        "Connection: close\r\n\r\n");

                    await stream.WriteAsync(head, token);
                    await stream.WriteAsync(body, token);
                    await stream.FlushAsync(token);
                }
            }, token);
        }
    }

    private static async Task<string> ReadRequestHeadAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), token);
            if (read == 0)
            {
                break;
            }

            total += read;
            var text = Encoding.Latin1.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return Encoding.Latin1.GetString(buffer, 0, total);
    }
}
