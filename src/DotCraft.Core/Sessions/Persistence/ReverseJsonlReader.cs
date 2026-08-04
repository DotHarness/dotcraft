using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace DotCraft.Sessions;

internal sealed class ReverseJsonlReader(string path, int blockSize = 64 * 1024)
{
    public long BytesRead { get; private set; }

    public async IAsyncEnumerable<string> ReadLinesAsync(
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
        byte[] carry = [];
        var firstBlock = true;

        while (position > 0)
        {
            ct.ThrowIfCancellationRequested();
            var count = (int)Math.Min(blockSize, position);
            var start = position - count;
            var rented = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                stream.Seek(start, SeekOrigin.Begin);
                var read = 0;
                while (read < count)
                {
                    var current = await stream.ReadAsync(rented.AsMemory(read, count - read), ct);
                    if (current == 0)
                        break;
                    read += current;
                }

                BytesRead += read;
                var data = new byte[read + carry.Length];
                Buffer.BlockCopy(rented, 0, data, 0, read);
                if (carry.Length > 0)
                    Buffer.BlockCopy(carry, 0, data, read, carry.Length);

                var end = data.Length;
                if (firstBlock && end > 0 && data[end - 1] == (byte)'\n')
                    end--;
                firstBlock = false;

                for (var index = end - 1; index >= 0; index--)
                {
                    if (data[index] != (byte)'\n')
                        continue;

                    var lineStart = index + 1;
                    var lineLength = end - lineStart;
                    if (lineLength > 0 && data[end - 1] == (byte)'\r')
                        lineLength--;
                    if (lineLength > 0)
                        yield return Encoding.UTF8.GetString(data, lineStart, lineLength);
                    end = index;
                }

                carry = data.AsSpan(0, end).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
            position = start;
        }

        if (carry.Length > 0)
        {
            var length = carry.Length;
            if (carry[length - 1] == (byte)'\r')
                length--;
            if (length > 0)
                yield return Encoding.UTF8.GetString(carry, 0, length);
        }
    }
}
