using System.Text.Json;
using System.Threading.Channels;

namespace DotCraft.CodexGateway.Relay;

internal sealed class RelaySessions(RemoteControlSessionFactory factory, RelayOutbox outbox) : IAsyncDisposable
{
    private readonly Dictionary<RemoteControlPeer, Session> _sessions = [];
    private readonly RelayReassembler _chunks = new();

    public string? Cursor { get; private set; }

    public async Task ReceiveAsync(JsonElement envelope, CancellationToken ct)
    {
        if (envelope.ValueKind != JsonValueKind.Object
            || !envelope.TryGetProperty("stream_id", out var stream) || stream.ValueKind != JsonValueKind.String
            || !envelope.TryGetProperty("client_id", out var client) || client.ValueKind != JsonValueKind.String)
        {
            RelayDiagnostics.Failure(null, "envelope", new InvalidDataException());
            return;
        }
        var peer = new RemoteControlPeer(client.GetString()!, stream.GetString()!);
        try
        {
            if (outbox.HasFailed(peer))
                await CloseAsync(peer);
            var type = envelope.GetProperty("type").GetString();
            var sequence = envelope.TryGetProperty("seq_id", out var seq) && seq.ValueKind != JsonValueKind.Null
                ? seq.GetUInt64() : (ulong?)null;
            switch (type)
            {
                case "ack":
                    if (sequence is { } acknowledged)
                        outbox.Acknowledge(peer, acknowledged,
                            envelope.TryGetProperty("segment_id", out var segment) && segment.ValueKind != JsonValueKind.Null
                                ? segment.GetInt32() : null);
                    break;
                case "ping":
                    var active = _sessions.TryGetValue(peer, out var existing) && !existing.Completion.IsCompleted;
                    if (active)
                        existing!.LastActivity = Environment.TickCount64;
                    outbox.Pong(peer, active);
                    break;
                case "client_closed":
                    await CloseAsync(peer);
                    break;
                case "client_message":
                    await DeliverAsync(peer, envelope.GetProperty("message"), sequence, ct);
                    break;
                case "client_message_chunk":
                    if (sequence is null)
                        throw new InvalidDataException("codex_remote_control.invalid_chunk: Missing sequence.");
                    var message = _chunks.Observe(peer, envelope);
                    if (message is null)
                        break;
                    await DeliverAsync(peer, message.Value, sequence, ct);
                    _chunks.Delivered(peer, sequence.Value);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            RelayDiagnostics.Failure(peer, "receive", error);
            await CloseAsync(peer);
        }
        finally
        {
            if (envelope.TryGetProperty("cursor", out var cursor) && cursor.ValueKind == JsonValueKind.String)
                Cursor = cursor.GetString();
        }
    }

    private async Task DeliverAsync(RemoteControlPeer peer, JsonElement message, ulong? sequence, CancellationToken ct)
    {
        var initialize = message.TryGetProperty("method", out var method) && method.GetString() == "initialize";
        _sessions.TryGetValue(peer, out var session);
        if (initialize && session is not null)
        {
            await CloseAsync(peer);
            session = null;
        }
        if (session is null)
        {
            if (!initialize)
                return;
            session = new Session(peer, factory, outbox);
            _sessions.Add(peer, session);
        }
        else if (sequence is { } value && session.LastSequence is { } last && value <= last)
        {
            return;
        }
        if (session.Completion.IsCompleted)
        {
            await CloseAsync(peer);
            return;
        }
        if (!message.TryGetProperty("method", out _) && message.TryGetProperty("id", out _))
            await session.ReceiveResponseAsync(message, ct);
        else if (!session.Input.Writer.TryWrite(message.Clone()))
            throw new IOException("codex_remote_control.client_backpressure: Virtual stream input is full.");
        session.LastSequence = sequence ?? session.LastSequence;
        session.LastActivity = Environment.TickCount64;
    }

    public async Task SweepAsync()
    {
        var expired = _sessions.Where(entry => entry.Value.Completion.IsCompleted || outbox.HasFailed(entry.Key)
                || Environment.TickCount64 - entry.Value.LastActivity >= 10 * 60 * 1000)
            .Select(entry => entry.Key).ToArray();
        foreach (var peer in expired)
            await CloseAsync(peer);
        _chunks.Sweep();
    }

    private async Task CloseAsync(RemoteControlPeer peer)
    {
        _chunks.Close(peer);
        try
        {
            if (_sessions.Remove(peer, out var session))
                await session.DisposeAsync();
        }
        catch (Exception error) { RelayDiagnostics.Failure(peer, "dispose", error); }
        finally { outbox.Close(peer); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var peer in _sessions.Keys.ToArray())
            await CloseAsync(peer);
        _chunks.Clear();
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();
        private readonly IRemoteControlRpcSession _rpc;
        private readonly RemoteControlPeer _peer;
        public Channel<JsonElement> Input { get; } = Channel.CreateBounded<JsonElement>(128);
        public ulong? LastSequence { get; set; }
        public long LastActivity { get; set; } = Environment.TickCount64;
        public Task Completion { get; }

        public Session(RemoteControlPeer peer, RemoteControlSessionFactory factory, RelayOutbox outbox)
        {
            _peer = peer;
            _rpc = factory(peer, async (message, ct) =>
            {
                using var send = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
                await outbox.SendAsync(peer, message, send.Token);
            });
            Completion = ProcessAsync();
        }

        public Task ReceiveResponseAsync(JsonElement message, CancellationToken ct) => _rpc.ReceiveAsync(message, ct);

        private async Task ProcessAsync()
        {
            try
            {
                await foreach (var message in Input.Reader.ReadAllAsync(_lifetime.Token))
                    await _rpc.ReceiveAsync(message, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error) { RelayDiagnostics.Failure(_peer, "worker", error); }
            finally { Input.Writer.TryComplete(); }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                try { await _lifetime.CancelAsync(); }
                finally { await Completion; }
            }
            finally
            {
                try { await _rpc.DisposeAsync(); }
                finally { _lifetime.Dispose(); }
            }
        }
    }
}
