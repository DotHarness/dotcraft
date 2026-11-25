using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DotCraft.Sessions;

/// <summary>What a running Turn has persisted of each Item, so later writes append only what changed.</summary>
internal sealed class PersistedTurnItems
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _persisted = new(StringComparer.Ordinal);
    private bool _known;

    public SemaphoreSlim WriteGate { get; } = new(1, 1);

    public static IReadOnlyList<PersistedItem> Capture(IEnumerable<SessionItem> items)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sink = new HashingBufferWriter(hash);
        using var writer = new Utf8JsonWriter(sink);
        var captured = new List<PersistedItem>();
        foreach (var item in items)
        {
            JsonSerializer.Serialize(writer, item, SessionJsonOptions.Default);
            writer.Flush();
            writer.Reset();
            captured.Add(new PersistedItem(item, Convert.ToHexString(hash.GetHashAndReset())));
        }

        return captured;
    }

    /// <summary>Records items as persisted by a write that covered the whole Turn.</summary>
    public void Reset(IReadOnlyList<PersistedItem> items)
    {
        lock (_lock)
        {
            _persisted.Clear();
            foreach (var item in items)
                _persisted[item.Item.Id] = item.Digest;
            _known = true;
        }
    }

    /// <summary>Returns the items that differ from what was persisted, or null when nothing is known yet.</summary>
    public IReadOnlyList<PersistedItem>? FindChanged(IReadOnlyList<PersistedItem> items)
    {
        lock (_lock)
        {
            if (!_known)
                return null;
            return items
                .Where(item => !_persisted.TryGetValue(item.Item.Id, out var digest) || digest != item.Digest)
                .ToList();
        }
    }

    public void MarkPersisted(IReadOnlyList<PersistedItem> items)
    {
        lock (_lock)
        {
            foreach (var item in items)
                _persisted[item.Item.Id] = item.Digest;
        }
    }

    internal sealed record PersistedItem(SessionItem Item, string Digest);

    private sealed class HashingBufferWriter(IncrementalHash hash) : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        public void Advance(int count) => hash.AppendData(_buffer, 0, count);

        public Memory<byte> GetMemory(int sizeHint = 0) => EnsureCapacity(sizeHint);

        public Span<byte> GetSpan(int sizeHint = 0) => EnsureCapacity(sizeHint);

        public void Dispose() => ArrayPool<byte>.Shared.Return(_buffer);

        private byte[] EnsureCapacity(int sizeHint)
        {
            if (sizeHint > _buffer.Length)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            }

            return _buffer;
        }
    }
}
