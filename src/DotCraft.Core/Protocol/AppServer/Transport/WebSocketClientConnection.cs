using System.Buffers;
using System.IO.Pipelines;
using System.Net.WebSockets;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Manages the lifetime of a client-side WebSocket connection to a DotCraft AppServer.
/// Bridges a <see cref="System.Net.WebSockets.WebSocket"/> to the line-oriented
/// <see cref="AppServerWireClient"/> by translating between WebSocket frames and
/// newline-delimited JSON streams.
///
/// Usage (production):
/// <code>
/// await using var conn = await WebSocketClientConnection.ConnectAsync(new Uri("ws://127.0.0.1:9100/ws"), token);
/// await conn.Wire.InitializeAsync(...);
/// </code>
///
/// Usage (testing — provide a pre-created WebSocket):
/// <code>
/// await using var conn = WebSocketClientConnection.FromWebSocket(fakeWebSocket);
/// </code>
/// </summary>
public sealed class WebSocketClientConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Task _sendLoop;
    private readonly Pipe _inboundPipe;
    private readonly Pipe _outboundPipe;
    private bool _disposed;

    /// <summary>
    /// The JSON-RPC 2.0 wire client. Call <see cref="AppServerWireClient.InitializeAsync"/> before
    /// making any other requests.
    /// </summary>
    public AppServerWireClient Wire { get; }

    private WebSocketClientConnection(WebSocket socket)
    {
        _socket = socket;

        // inboundPipe: receives → becomes input stream for AppServerWireClient
        _inboundPipe = new Pipe();
        // outboundPipe: AppServerWireClient writes → becomes output to WebSocket
        _outboundPipe = new Pipe();

        var ct = _cts.Token;
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(ct));
        _sendLoop = Task.Run(() => SendLoopAsync(ct));

        Wire = new AppServerWireClient(
            _inboundPipe.Reader.AsStream(),
            _outboundPipe.Writer.AsStream());
        Wire.Start();
    }

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Connects to a DotCraft AppServer WebSocket endpoint and returns a ready-to-use connection.
    /// The caller must perform the <c>initialize</c> / <c>initialized</c> handshake separately.
    /// </summary>
    public static async Task<WebSocketClientConnection> ConnectAsync(
        Uri serverUri,
        string? token = null,
        CancellationToken ct = default)
    {
        var uri = token is not null
            ? new UriBuilder(serverUri) { Query = $"token={Uri.EscapeDataString(token)}" }.Uri
            : serverUri;

        var ws = new ClientWebSocket();
        await ws.ConnectAsync(uri, ct);
        return new WebSocketClientConnection(ws);
    }

    /// <summary>
    /// Creates a <see cref="WebSocketClientConnection"/> from an already-connected or
    /// stream-backed <see cref="WebSocket"/>. Intended for integration tests.
    /// </summary>
    internal static WebSocketClientConnection FromWebSocket(WebSocket socket)
        => new(socket);

    // -------------------------------------------------------------------------
    // Bridge loops
    // -------------------------------------------------------------------------

    /// <summary>
    /// Receive loop: WebSocket frames → newline-delimited lines written to inbound pipe.
    /// AppServerWireClient reads from the inbound pipe using StreamReader.ReadLineAsync().
    /// </summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        const int MaxMessageBytes = 4 * 1024 * 1024;
        var buffer = new byte[MaxMessageBytes];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int totalBytes = 0;
                WebSocketReceiveResult result;

                do
                {
                    var remaining = MaxMessageBytes - totalBytes;
                    if (remaining <= 0) return;

                    try
                    {
                        result = await _socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer, totalBytes, remaining), ct);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (WebSocketException) { return; }

                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType == WebSocketMessageType.Binary) continue;

                    totalBytes += result.Count;
                } while (!result.EndOfMessage);

                if (totalBytes == 0) continue;

                // Write JSON + newline so StreamReader.ReadLineAsync() reads one complete message
                var memory = _inboundPipe.Writer.GetMemory(totalBytes + 1);
                buffer.AsSpan(0, totalBytes).CopyTo(memory.Span);
                memory.Span[totalBytes] = (byte)'\n';
                _inboundPipe.Writer.Advance(totalBytes + 1);

                var flushResult = await _inboundPipe.Writer.FlushAsync(ct);
                if (flushResult.IsCompleted) return;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _inboundPipe.Writer.CompleteAsync();
        }
    }

    /// <summary>
    /// Send loop: reads lines from outbound pipe (written by AppServerWireClient.WriteLineAsync)
    /// and sends each line as a WebSocket text frame.
    /// </summary>
    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var readResult = await _outboundPipe.Reader.ReadAsync(ct);
                var buffer = readResult.Buffer;

                // Process complete newline-terminated JSON messages from the pipe
                while (TryReadLine(ref buffer, out var line))
                {
                    if (line.Length > 0)
                    {
                        var bytes = line.ToArray();
                        await _socket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            ct);
                    }
                }

                _outboundPipe.Reader.AdvanceTo(buffer.Start, buffer.End);

                if (readResult.IsCompleted) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            await _outboundPipe.Reader.CompleteAsync();
        }
    }

    /// <summary>
    /// Reads one newline-terminated line from the buffer.
    /// Strips the trailing "\n" and optional preceding "\r".
    /// </summary>
    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryReadTo(out line, (byte)'\n'))
        {
            line = default;
            return false;
        }

        // Strip trailing "\r" (from "\r\n" line endings produced by StreamWriter on Windows)
        if (line.Length > 0 && line.Slice(line.Length - 1).FirstSpan[0] == (byte)'\r')
            line = line.Slice(0, line.Length - 1);

        buffer = buffer.Slice(reader.Position);
        return true;
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _cts.CancelAsync();
        try { _inboundPipe.Writer.Complete(); } catch { }
        try { _outboundPipe.Writer.Complete(); } catch { }

        try { await Task.WhenAll(_receiveLoop, _sendLoop); } catch { }

        await Wire.DisposeAsync();

        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
        }
        catch { }

        _socket.Dispose();
        _cts.Dispose();
    }
}
