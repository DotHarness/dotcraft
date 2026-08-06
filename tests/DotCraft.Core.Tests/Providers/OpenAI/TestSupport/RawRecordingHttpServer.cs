using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DotCraft.Tests.Agents.TestSupport;

/// <summary>
/// Minimal loopback HTTP/1.1 server for wire-level provider tests. Unlike the older
/// string-only fixture, this preserves opaque request bytes and supports chunked SSE responses.
/// </summary>
internal sealed class RawRecordingHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<RawHttpResponse> _responses;
    private readonly Task _acceptLoop;
    private readonly object _requestsGate = new();
    private readonly List<RawHttpRequest> _requests = [];

    private RawRecordingHttpServer(
        TcpListener listener,
        string endpoint,
        IEnumerable<RawHttpResponse> responses)
    {
        _listener = listener;
        Endpoint = endpoint;
        _responses = new Queue<RawHttpResponse>(responses);
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string Endpoint { get; }

    public IReadOnlyList<RawHttpRequest> Requests
    {
        get
        {
            lock (_requestsGate)
                return [.. _requests];
        }
    }

    public static RawRecordingHttpServer Start(params RawHttpResponse[] responses)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        return new RawRecordingHttpServer(
            listener,
            $"http://{IPAddress.Loopback}:{endpoint.Port}",
            responses);
    }

    public async Task<IReadOnlyList<RawHttpRequest>> WaitForRequestsAsync(
        int count,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var snapshot = Requests;
                if (snapshot.Count >= count)
                    return snapshot;
                await Task.Delay(10, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Expected {count} requests but observed {Requests.Count}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (TimeoutException)
        {
        }
        finally
        {
            _stop.Dispose();
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, _stop.Token).ConfigureAwait(false);
                lock (_requestsGate)
                    _requests.Add(request);
                var response = _responses.Count > 0
                    ? _responses.Dequeue()
                    : RawHttpResponse.Json("{}", HttpStatusCode.InternalServerError);
                await WriteResponseAsync(stream, response, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task<RawHttpRequest> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var received = new List<byte>();
        var buffer = new byte[4096];
        var headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new IOException("HTTP request ended before its headers were complete.");
            received.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = FindHeaderEnd(received);
        }

        var headerText = Encoding.ASCII.GetString(received.Take(headerEnd).ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', 3);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
                headers[line[..separator]] = line[(separator + 1)..].Trim();
        }

        var bodyStart = headerEnd + 4;
        byte[] body;
        if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
            && transferEncoding.Split(',').Any(value =>
                string.Equals(value.Trim(), "chunked", StringComparison.OrdinalIgnoreCase)))
        {
            body = await ReadChunkedBodyAsync(received, bodyStart, stream, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var length = headers.TryGetValue("Content-Length", out var rawLength)
                         && int.TryParse(rawLength, out var parsed)
                ? parsed
                : 0;
            await ReadUntilAsync(received, bodyStart + length, stream, buffer, cancellationToken)
                .ConfigureAwait(false);
            body = received.Skip(bodyStart).Take(length).ToArray();
        }

        return new RawHttpRequest(
            requestLine.ElementAtOrDefault(0) ?? string.Empty,
            requestLine.ElementAtOrDefault(1) ?? string.Empty,
            headers,
            body);
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(
        List<byte> received,
        int offset,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var body = new List<byte>();
        var buffer = new byte[4096];
        while (true)
        {
            var lineEnd = await FindLineEndAsync(received, offset, stream, buffer, cancellationToken)
                .ConfigureAwait(false);
            var sizeText = Encoding.ASCII.GetString(received.Skip(offset).Take(lineEnd - offset).ToArray());
            var extension = sizeText.IndexOf(';');
            if (extension >= 0)
                sizeText = sizeText[..extension];
            if (!int.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber, null, out var size))
                throw new IOException($"Invalid chunk size '{sizeText}'.");
            offset = lineEnd + 2;
            if (size == 0)
                return [.. body];

            await ReadUntilAsync(received, offset + size + 2, stream, buffer, cancellationToken)
                .ConfigureAwait(false);
            body.AddRange(received.Skip(offset).Take(size));
            offset += size;
            if (received[offset] != '\r' || received[offset + 1] != '\n')
                throw new IOException("HTTP chunk did not end with CRLF.");
            offset += 2;
        }
    }

    private static async Task<int> FindLineEndAsync(
        List<byte> received,
        int offset,
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            for (var index = offset + 1; index < received.Count; index++)
            {
                if (received[index - 1] == '\r' && received[index] == '\n')
                    return index - 1;
            }
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new IOException("HTTP chunk-size line was incomplete.");
            received.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    private static async Task ReadUntilAsync(
        List<byte> received,
        int requiredCount,
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        while (received.Count < requiredCount)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new IOException("HTTP body ended before the declared length.");
            received.AddRange(buffer.AsSpan(0, read).ToArray());
        }
    }

    private static int FindHeaderEnd(IReadOnlyList<byte> bytes)
    {
        for (var index = 3; index < bytes.Count; index++)
        {
            if (bytes[index - 3] == '\r' && bytes[index - 2] == '\n'
                && bytes[index - 1] == '\r' && bytes[index] == '\n')
                return index - 3;
        }
        return -1;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        RawHttpResponse response,
        CancellationToken cancellationToken)
    {
        var extraHeaders = response.Headers == null
            ? string.Empty
            : string.Concat(response.Headers.Select(pair => $"{pair.Key}: {pair.Value}\r\n"));
        var framing = response.ChunkSizes == null
            ? $"Content-Length: {response.Body.Length}\r\n"
            : "Transfer-Encoding: chunked\r\n";
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)response.StatusCode} {ReasonPhrase(response.StatusCode)}\r\n"
            + $"Content-Type: {response.ContentType}\r\n"
            + extraHeaders + framing + "Connection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        if (response.DelayBeforeBody > TimeSpan.Zero)
            await Task.Delay(response.DelayBeforeBody, cancellationToken).ConfigureAwait(false);

        if (response.ChunkSizes == null)
        {
            await stream.WriteAsync(response.Body, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = 0;
        foreach (var requestedSize in response.ChunkSizes)
        {
            if (offset >= response.Body.Length)
                break;
            var size = Math.Min(Math.Max(1, requestedSize), response.Body.Length - offset);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"{size:X}\r\n"), cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync(response.Body.AsMemory(offset, size), cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync("\r\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            offset += size;
        }
        if (offset < response.Body.Length)
        {
            var remaining = response.Body.Length - offset;
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"{remaining:X}\r\n"), cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync(response.Body.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\r\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static string ReasonPhrase(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => "OK",
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.InternalServerError => "Internal Server Error",
        _ => status.ToString()
    };
}

internal sealed record RawHttpRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body)
{
    public string BodyText => Encoding.UTF8.GetString(Body);
}

internal sealed record RawHttpResponse(
    HttpStatusCode StatusCode,
    string ContentType,
    byte[] Body,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyList<int>? ChunkSizes = null,
    TimeSpan DelayBeforeBody = default)
{
    public static RawHttpResponse Json(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? headers = null) =>
        new(statusCode, "application/json", Encoding.UTF8.GetBytes(body), headers);

    public static RawHttpResponse Sse(
        string body,
        IReadOnlyList<int>? chunkSizes = null,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan delayBeforeBody = default) =>
        new(HttpStatusCode.OK, "text/event-stream", Encoding.UTF8.GetBytes(body), headers, chunkSizes, delayBeforeBody);
}
