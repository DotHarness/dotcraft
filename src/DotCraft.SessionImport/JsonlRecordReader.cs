using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace DotCraft.SessionImport;

internal static class JsonlRecordReader
{
    private const int InitialBufferSize = 64 * 1024;
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 256 };

    public static FileStream OpenShared(string path) =>
        new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.SequentialScan
        });

    /// <summary>
    /// Yields newline-terminated records, plus a final unterminated one only when it is already complete JSON.
    /// Each element stays valid only until the next one is requested.
    /// </summary>
    public static IEnumerable<JsonElement> ReadRecords(
        Stream stream,
        IncrementalHash? hash = null,
        long byteLimit = long.MaxValue)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        var start = 0;
        var end = 0;
        var scanFrom = 0;
        var consumed = 0L;
        var atEnd = false;
        var firstLine = true;
        try
        {
            while (true)
            {
                var newline = end > scanFrom ? Array.IndexOf(buffer, (byte)'\n', scanFrom, end - scanFrom) : -1;
                if (newline < 0 && !atEnd)
                {
                    scanFrom = end;
                    if (start > 0)
                    {
                        Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                        end -= start;
                        scanFrom -= start;
                        start = 0;
                    }

                    if (end == buffer.Length)
                    {
                        var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        Buffer.BlockCopy(buffer, 0, larger, 0, end);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }

                    var toRead = (int)Math.Min(buffer.Length - end, byteLimit - consumed);
                    var read = toRead > 0 ? stream.Read(buffer, end, toRead) : 0;
                    if (read == 0)
                    {
                        atEnd = true;
                        continue;
                    }

                    hash?.AppendData(buffer, end, read);
                    end += read;
                    consumed += read;
                    continue;
                }

                var lineEnd = newline >= 0 ? newline : end;
                var document = TryParse(buffer, start, lineEnd - start, firstLine);
                firstLine = false;
                start = newline >= 0 ? newline + 1 : end;
                scanFrom = start;
                if (document is not null)
                {
                    using (document)
                    {
                        if (document.RootElement.ValueKind == JsonValueKind.Object)
                            yield return document.RootElement;
                    }
                }

                if (newline < 0)
                    yield break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string ToHex(IncrementalHash hash) => Convert.ToHexStringLower(hash.GetHashAndReset());

    private static JsonDocument? TryParse(byte[] buffer, int offset, int count, bool firstLine)
    {
        if (firstLine && count >= 3 && buffer[offset] == 0xEF && buffer[offset + 1] == 0xBB && buffer[offset + 2] == 0xBF)
        {
            offset += 3;
            count -= 3;
        }

        if (IsBlank(buffer, offset, count))
            return null;

        try
        {
            return JsonDocument.Parse(new ReadOnlyMemory<byte>(buffer, offset, count), DocumentOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsBlank(byte[] buffer, int offset, int count)
    {
        for (var i = offset; i < offset + count; i++)
        {
            if (buffer[i] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return false;
        }

        return true;
    }
}
