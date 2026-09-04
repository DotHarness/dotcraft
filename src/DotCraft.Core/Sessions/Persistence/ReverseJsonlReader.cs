using System.Buffers;
using System.Runtime.CompilerServices;

namespace DotCraft.Sessions;

internal sealed class ReverseJsonlReader(string path, int blockSize = 64 * 1024)
{
    public long BytesRead { get; private set; }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        var position = stream.Length;
        var chunk = ArrayPool<byte>.Shared.Rent(blockSize);
        using var record = new ReverseRecordBuffer();
        try
        {
            var chunkPosition = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (chunkPosition == 0)
                {
                    if (position == 0)
                    {
                        var finalRecord = record.Finish();
                        if (finalRecord.HasValue)
                            yield return finalRecord.Value;
                        yield break;
                    }

                    var readSize = (int)Math.Min(blockSize, position);
                    position -= readSize;
                    stream.Seek(position, SeekOrigin.Begin);
                    var read = 0;
                    while (read < readSize)
                    {
                        var current = await stream.ReadAsync(chunk.AsMemory(read, readSize - read), ct);
                        if (current == 0)
                            throw new EndOfStreamException("The rollout ended during a reverse scan.");
                        read += current;
                    }

                    BytesRead += read;
                    chunkPosition = read;
                }

                var newlinePosition = chunk.AsSpan(0, chunkPosition).LastIndexOf((byte)'\n');
                if (newlinePosition >= 0)
                {
                    record.AppendReversed(chunk.AsSpan(newlinePosition + 1, chunkPosition - newlinePosition - 1));
                    chunkPosition = newlinePosition;
                    var completedRecord = record.Finish();
                    if (completedRecord.HasValue)
                        yield return completedRecord.Value;
                    record.Reset();
                }
                else
                {
                    record.AppendReversed(chunk.AsSpan(0, chunkPosition));
                    chunkPosition = 0;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private sealed class ReverseRecordBuffer : IDisposable
    {
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(4 * 1024);

        public int Count { get; private set; }

        public void AppendReversed(ReadOnlySpan<byte> fragment)
        {
            EnsureCapacity(checked(Count + fragment.Length));
            var destination = _buffer.AsSpan(Count, fragment.Length);
            for (var index = 0; index < fragment.Length; index++)
                destination[index] = fragment[fragment.Length - index - 1];
            Count += fragment.Length;
        }

        public ReadOnlyMemory<byte>? Finish()
        {
            if (Count == 0)
                return null;

            _buffer.AsSpan(0, Count).Reverse();
            var value = _buffer.AsMemory(0, Count);
            return Utf8JsonlReader.IsWhiteSpace(value.Span) ? null : value;
        }

        public void Reset() => Count = 0;

        public void Dispose()
        {
            var buffer = _buffer;
            _buffer = [];
            Count = 0;
            ArrayPool<byte>.Shared.Return(buffer);
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _buffer.Length)
                return;

            var replacement = ArrayPool<byte>.Shared.Rent(Math.Max(required, checked(_buffer.Length * 2)));
            _buffer.AsSpan(0, Count).CopyTo(replacement);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = replacement;
        }
    }
}
