using System.Buffers;
using System.Text;
using System.Text.Json;

namespace DotCraft.Sessions;

internal delegate void Utf8JsonLineHandler(ReadOnlySpan<byte> line);

internal static class Utf8JsonlReader
{
    private const int BufferSize = 64 * 1024;

    public static async Task ReadAsync(
        string path,
        Utf8JsonLineHandler handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var carry = new MemoryStream();
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false);
                if (read == 0)
                    break;

                ProcessChunk(buffer.AsSpan(0, read), carry, handler);
            }

            ProcessRemainder(carry, handler);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static void Read(string path, Utf8JsonLineHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var carry = new MemoryStream();
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, BufferSize)) != 0)
                ProcessChunk(buffer.AsSpan(0, read), carry, handler);

            ProcessRemainder(carry, handler);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool IsWhiteSpace(ReadOnlySpan<byte> line)
    {
        var offset = 0;
        while (offset < line.Length)
        {
            var value = line[offset];
            if (value < 0x80)
            {
                if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'
                    or (byte)'\v' or (byte)'\f'))
                {
                    return false;
                }
                offset++;
                continue;
            }

            if (Rune.DecodeFromUtf8(line[offset..], out var rune, out var consumed) != OperationStatus.Done
                || !Rune.IsWhiteSpace(rune))
            {
                return false;
            }
            offset += consumed;
        }

        return true;
    }

    private static void ProcessChunk(
        ReadOnlySpan<byte> chunk,
        MemoryStream carry,
        Utf8JsonLineHandler handler)
    {
        var segmentStart = 0;
        while (segmentStart < chunk.Length)
        {
            var relativeNewline = chunk[segmentStart..].IndexOf((byte)'\n');
            if (relativeNewline < 0)
            {
                carry.Write(chunk[segmentStart..]);
                return;
            }

            var segment = chunk.Slice(segmentStart, relativeNewline);
            if (carry.Length == 0)
            {
                handler(TrimCarriageReturn(segment));
            }
            else
            {
                carry.Write(segment);
                handler(TrimCarriageReturn(carry.GetBuffer().AsSpan(0, checked((int)carry.Length))));
                carry.SetLength(0);
            }

            segmentStart += relativeNewline + 1;
        }
    }

    private static void ProcessRemainder(MemoryStream carry, Utf8JsonLineHandler handler)
    {
        if (carry.Length == 0)
            return;

        handler(TrimCarriageReturn(carry.GetBuffer().AsSpan(0, checked((int)carry.Length))));
    }

    private static ReadOnlySpan<byte> TrimCarriageReturn(ReadOnlySpan<byte> line) =>
        line is [.., (byte)'\r'] ? line[..^1] : line;
}

internal static class RolloutJsonEnvelopeReader
{
    public static string? ReadKind(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, isFinalBlock: true, state: default);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        string? kind = null;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName
                || reader.CurrentDepth != 1
                || !reader.ValueTextEquals("kind"u8))
            {
                continue;
            }

            if (!reader.Read())
                return null;
            kind = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        }

        return kind;
    }
}
