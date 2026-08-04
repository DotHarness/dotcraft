using System.IO.Pipelines;
using System.Net.WebSockets;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Shared test utilities for WebSocket-based transport tests.
/// </summary>
internal static class WebSocketTestHelper
{
    /// <summary>
    /// Creates a pair of connected <see cref="WebSocket"/> instances backed by in-process
    /// <see cref="Pipe"/>s. No network or OS resources are used.
    /// </summary>
    internal static (WebSocket Server, WebSocket Client) CreateWebSocketPair()
    {
        var serverToClient = new Pipe();
        var clientToServer = new Pipe();

        // Server stream: reads from clientToServer pipe, writes to serverToClient pipe
        var serverStream = new DuplexPipeStream(clientToServer.Reader, serverToClient.Writer);
        // Client stream: reads from serverToClient pipe, writes to clientToServer pipe
        var clientStream = new DuplexPipeStream(serverToClient.Reader, clientToServer.Writer);

        var serverWs = WebSocket.CreateFromStream(serverStream, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = Timeout.InfiniteTimeSpan
        });
        var clientWs = WebSocket.CreateFromStream(clientStream, new WebSocketCreationOptions
        {
            IsServer = false,
            KeepAliveInterval = Timeout.InfiniteTimeSpan
        });

        return (serverWs, clientWs);
    }
}

/// <summary>
/// Bidirectional <see cref="Stream"/> backed by a read <see cref="PipeReader"/> and a write
/// <see cref="PipeWriter"/>. Required because <see cref="WebSocket.CreateFromStream"/> needs a
/// single stream that supports both reads and writes.
/// </summary>
internal sealed class DuplexPipeStream : Stream
{
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;

    public DuplexPipeStream(PipeReader reader, PipeWriter writer)
    {
        _reader = reader;
        _writer = writer;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _reader.AsStream().ReadAsync(buffer, ct);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        => _writer.AsStream().WriteAsync(buffer, ct);

    public override Task FlushAsync(CancellationToken ct)
        => _writer.FlushAsync(ct).AsTask();

    // Sync overrides required by abstract class but not used in async WebSocket paths
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => _reader.AsStream().Read(buffer, offset, count);
    public override void Write(byte[] buffer, int offset, int count) => _writer.AsStream().Write(buffer, offset, count);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
}
