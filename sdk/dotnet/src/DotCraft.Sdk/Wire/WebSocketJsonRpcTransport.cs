using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DotCraft.Sdk.Wire;

/// <summary>
/// WebSocket transport for DotCraft AppServer JSON-RPC messages.
/// </summary>
public sealed class WebSocketJsonRpcTransport : IReconnectableJsonRpcTransport
{
    private const int MaxMessageBytes = 4 * 1024 * 1024;
    private WebSocket _socket;
    private readonly Uri? _reconnectUri;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    private WebSocketJsonRpcTransport(WebSocket socket, Uri? reconnectUri = null)
    {
        _socket = socket;
        _reconnectUri = reconnectUri;
    }

    /// <summary>
    /// Opens a WebSocket connection to an AppServer endpoint.
    /// </summary>
    public static async Task<WebSocketJsonRpcTransport> ConnectAsync(
        string appServerUrl,
        string? token = null,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(appServerUrl, token);
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(uri, cancellationToken);
        return new WebSocketJsonRpcTransport(socket, uri);
    }

    /// <summary>
    /// Creates a transport from an already connected WebSocket.
    /// </summary>
    public static WebSocketJsonRpcTransport FromWebSocket(WebSocket socket) => new(socket);

    /// <inheritdoc />
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_reconnectUri is null)
        {
            throw new InvalidOperationException("A transport created from an external WebSocket cannot reconnect itself.");
        }

        var replacement = new ClientWebSocket();
        await replacement.ConnectAsync(_reconnectUri, cancellationToken);
        var previous = Interlocked.Exchange(ref _socket, replacement);
        previous.Dispose();
    }

    /// <inheritdoc />
    public async Task<JsonDocument?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                continue;
            }

            stream.Write(buffer, 0, result.Count);
            if (stream.Length > MaxMessageBytes)
            {
                throw new InvalidOperationException("DotCraft AppServer message exceeds 4 MB.");
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(stream.ToArray());
            return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task WriteAsync(object message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, DotCraftJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "DotCraft SDK client disconnecting.", cts.Token);
            }
        }
        catch
        {
            // Best-effort close only.
        }
        finally
        {
            _socket.Dispose();
            _writeLock.Dispose();
        }
    }

    private static Uri BuildUri(string rawUrl, string? token)
    {
        var uri = new Uri(rawUrl);
        if (string.IsNullOrWhiteSpace(token))
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var query = builder.Query.TrimStart('?');
        var tokenPair = $"token={Uri.EscapeDataString(token)}";
        builder.Query = string.IsNullOrWhiteSpace(query) ? tokenPair : $"{query}&{tokenPair}";
        return builder.Uri;
    }
}
