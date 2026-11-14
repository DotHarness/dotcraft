using System.Text.Json;

namespace DotCraft.CodexGateway.Relay;

internal static class RelayFrames
{
    public const int MaxEnvelopeBytes = 150 * 1024;
    public const int TargetChunkBytes = 100 * 1024;
    public const int MaxMessageBytes = 100 * 1024 * 1024;
    public const int MaxSegments = 1024;

    public static IReadOnlyList<OutboundFrame> Message(RemoteControlPeer peer, ulong sequence, JsonElement message)
    {
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "server_message", client_id = peer.ClientId, stream_id = peer.StreamId,
            seq_id = sequence, message
        });
        if (envelope.Length <= MaxEnvelopeBytes)
            return [new(sequence, null, envelope)];
        var raw = JsonSerializer.SerializeToUtf8Bytes(message);
        if (raw.Length > MaxMessageBytes)
            throw new InvalidDataException("codex_remote_control.message_too_large: Message exceeds 100 MiB.");
        var count = Math.Max(2, (raw.Length + TargetChunkBytes - 1) / TargetChunkBytes);
        while (count <= MaxSegments)
        {
            var chunkBytes = (raw.Length + count - 1) / count;
            var actualCount = (raw.Length + chunkBytes - 1) / chunkBytes;
            var frames = new List<OutboundFrame>(actualCount);
            for (var index = 0; index < actualCount; index++)
            {
                var offset = index * chunkBytes;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "server_message_chunk", client_id = peer.ClientId, stream_id = peer.StreamId,
                    seq_id = sequence, segment_id = index, segment_count = actualCount,
                    message_size_bytes = raw.Length,
                    message_chunk_base64 = Convert.ToBase64String(raw, offset, Math.Min(chunkBytes, raw.Length - offset))
                });
                frames.Add(new(sequence, index, bytes));
            }
            if (frames.All(frame => frame.Payload.Length <= MaxEnvelopeBytes))
                return frames;
            if (chunkBytes == 1)
                break;
            count++;
        }
        throw new InvalidDataException("codex_remote_control.message_too_large: Message cannot fit relay chunk limits.");
    }

    public static OutboundFrame Pong(RemoteControlPeer peer, ulong sequence, bool active) =>
        new(sequence, null, JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "pong", client_id = peer.ClientId, stream_id = peer.StreamId,
            seq_id = sequence, status = active ? "active" : "unknown"
        }));
}

internal sealed record OutboundFrame(ulong Sequence, int? Segment, byte[] Payload);

internal sealed class RelayReassembler
{
    private readonly Dictionary<RemoteControlPeer, Assembly> _assemblies = [];
    private readonly Dictionary<RemoteControlPeer, ulong> _completed = [];

    public JsonElement? Observe(RemoteControlPeer peer, JsonElement envelope)
    {
        var sequence = envelope.GetProperty("seq_id").GetUInt64();
        if (_completed.TryGetValue(peer, out var completed) && sequence <= completed)
            return null;
        var index = envelope.GetProperty("segment_id").GetInt32();
        var count = envelope.GetProperty("segment_count").GetInt32();
        var size = envelope.GetProperty("message_size_bytes").GetInt32();
        if (count is <= 0 or > RelayFrames.MaxSegments || index < 0 || index >= count
            || size is <= 0 or > RelayFrames.MaxMessageBytes)
        {
            throw new InvalidDataException("codex_remote_control.invalid_chunk: Invalid segment bounds.");
        }
        if (!_assemblies.TryGetValue(peer, out var assembly))
        {
            if (_assemblies.Count == 128)
                throw new IOException("codex_remote_control.chunk_backpressure: Reassembly capacity is full.");
            assembly = new Assembly(sequence, count, size);
            _assemblies.Add(peer, assembly);
        }
        if (sequence < assembly.Sequence || index < assembly.NextSegment && sequence == assembly.Sequence)
            return null;
        if (sequence != assembly.Sequence || count != assembly.Count || size != assembly.Size
            || index != assembly.NextSegment)
        {
            throw new InvalidDataException("codex_remote_control.invalid_chunk: Inconsistent segment sequence.");
        }
        var chunk = Convert.FromBase64String(envelope.GetProperty("message_chunk_base64").GetString()!);
        if (assembly.Bytes.Length + chunk.Length > size)
            throw new InvalidDataException("codex_remote_control.invalid_chunk: Segment exceeds declared size.");
        assembly.Bytes.Write(chunk);
        assembly.NextSegment++;
        assembly.LastSeen = Environment.TickCount64;
        if (assembly.NextSegment < count)
            return null;
        if (assembly.Bytes.Length != size)
            throw new InvalidDataException("codex_remote_control.invalid_chunk: Incomplete message size.");
        using var document = JsonDocument.Parse(assembly.Bytes.GetBuffer().AsMemory(0, size));
        var result = document.RootElement.Clone();
        Remove(peer);
        return result;
    }

    public void Delivered(RemoteControlPeer peer, ulong sequence) => _completed[peer] = sequence;

    public void Remove(RemoteControlPeer peer)
    {
        if (_assemblies.Remove(peer, out var assembly))
            assembly.Dispose();
    }

    public void Close(RemoteControlPeer peer)
    {
        Remove(peer);
        _completed.Remove(peer);
    }

    public void Sweep()
    {
        foreach (var peer in _assemblies.Where(entry =>
            Environment.TickCount64 - entry.Value.LastSeen >= 10 * 60 * 1000).Select(entry => entry.Key).ToArray())
            Remove(peer);
    }

    public void Clear()
    {
        foreach (var peer in _assemblies.Keys.ToArray())
            Remove(peer);
        _completed.Clear();
    }

    private sealed class Assembly(ulong sequence, int count, int size) : IDisposable
    {
        public ulong Sequence { get; } = sequence;
        public int Count { get; } = count;
        public int Size { get; } = size;
        public int NextSegment { get; set; }
        public long LastSeen { get; set; } = Environment.TickCount64;
        public MemoryStream Bytes { get; } = new();
        public void Dispose() => Bytes.Dispose();
    }
}
