using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace DotCraft.CodexGateway.Relay;

internal sealed class RelayOutbox
{
    private const int MaxPendingMessagesPerPeer = 128;
    private readonly object _gate = new();
    private readonly Dictionary<RemoteControlPeer, PeerQueue> _peers = [];
    private readonly Dictionary<RemoteControlPeer, ulong> _sequences = [];
    private readonly Channel<bool> _ready = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public Task SendAsync(RemoteControlPeer peer, JsonElement message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Enqueue(peer, sequence => RelayFrames.Message(peer, sequence, message));
        return Task.CompletedTask;
    }

    public void Pong(RemoteControlPeer peer, bool active)
    {
        lock (_gate)
        {
            if (_peers.TryGetValue(peer, out var queue) && queue.Failed)
                return;
        }
        Enqueue(peer, sequence => [RelayFrames.Pong(peer, sequence, active)]);
    }

    private void Enqueue(RemoteControlPeer peer, Func<ulong, IReadOnlyList<OutboundFrame>> create)
    {
        lock (_gate)
        {
            if (!_peers.TryGetValue(peer, out var queue))
                _peers.Add(peer, queue = new PeerQueue());
            if (queue.Failed)
                throw new IOException("codex_remote_control.stream_closed: Virtual stream output is closed.");
            try
            {
                if (queue.Pending.Select(frame => frame.Sequence).Distinct().Count() >= MaxPendingMessagesPerPeer)
                    throw new IOException("codex_remote_control.client_backpressure: Virtual stream output is full.");
                var sequence = _sequences.GetValueOrDefault(peer, 1UL);
                _sequences[peer] = sequence + 1;
                var frames = create(sequence);
                queue.Pending.AddRange(frames);
                foreach (var frame in frames)
                    queue.Unsent.Enqueue(frame);
            }
            catch (Exception error)
            {
                queue.Failed = true;
                queue.Pending.Clear();
                queue.Unsent.Clear();
                RelayDiagnostics.Failure(peer, "output", error);
                throw;
            }
        }
        _ready.Writer.TryWrite(true);
    }

    public bool HasFailed(RemoteControlPeer peer)
    {
        lock (_gate)
            return _peers.TryGetValue(peer, out var queue) && queue.Failed;
    }

    public void Close(RemoteControlPeer peer)
    {
        lock (_gate)
            _peers.Remove(peer);
    }

    public void Acknowledge(RemoteControlPeer peer, ulong sequence, int? segment)
    {
        lock (_gate)
        {
            if (_peers.TryGetValue(peer, out var queue))
                queue.Pending.RemoveAll(frame => frame.Sequence < sequence || frame.Sequence == sequence
                    && (frame.Segment ?? 0) <= (segment ?? int.MaxValue));
        }
    }

    public async Task WriteAsync(WebSocket socket, CancellationToken ct)
    {
        lock (_gate)
        {
            foreach (var queue in _peers.Values)
            {
                queue.Unsent.Clear();
                foreach (var frame in queue.Pending)
                    queue.Unsent.Enqueue(frame);
            }
        }
        while (true)
        {
            (RemoteControlPeer Peer, PeerQueue Queue)[] peers;
            lock (_gate)
                peers = _peers.Select(entry => (entry.Key, entry.Value)).ToArray();
            var sent = false;
            foreach (var (peer, queue) in peers)
            {
                OutboundFrame? frame = null;
                lock (_gate)
                {
                    if (_peers.TryGetValue(peer, out var current) && ReferenceEquals(current, queue)
                        && queue.Unsent.TryDequeue(out var next))
                        frame = next;
                }
                if (frame is null)
                    continue;
                await socket.SendAsync(frame.Payload.AsMemory(), WebSocketMessageType.Text, true, ct);
                sent = true;
            }
            if (!sent)
                await _ready.Reader.ReadAsync(ct);
        }
    }

    private sealed class PeerQueue
    {
        public bool Failed { get; set; }
        public List<OutboundFrame> Pending { get; } = [];
        public Queue<OutboundFrame> Unsent { get; } = [];
    }
}
