using System.Text;

namespace DotCraft.Tools.BackgroundTerminals;

internal sealed class TerminalOutputBuffer
{
    internal const int CapacityBytes = 1024 * 1024;
    private readonly LinkedList<string> _chunks = new();
    private readonly object _sync = new();
    private int _bytes;
    private long _characters;
    private long _trailingLineEndings;

    public void Append(string text)
    {
        lock (_sync)
        {
            foreach (var chunk in SplitFrames(text, 8192))
            {
                _chunks.AddLast(chunk);
                _bytes += Encoding.UTF8.GetByteCount(chunk);
                while (_bytes > CapacityBytes)
                {
                    var head = _chunks.First!.Value;
                    _chunks.RemoveFirst();
                    var removeChars = 0;
                    foreach (var rune in head.EnumerateRunes())
                    {
                        _bytes -= rune.Utf8SequenceLength;
                        removeChars += rune.Utf16SequenceLength;
                        if (_bytes <= CapacityBytes) break;
                    }
                    if (removeChars < head.Length)
                        _chunks.AddFirst(head[removeChars..]);
                }
            }
            _characters += text.Length;
            var tail = text.Length - text.TrimEnd('\r', '\n').Length;
            _trailingLineEndings = tail == text.Length ? _trailingLineEndings + tail : tail;
        }
    }

    public (string Output, int OriginalChars, bool Truncated) Snapshot(int maxCharacters)
    {
        lock (_sync)
        {
            var output = string.Concat(_chunks).TrimEnd('\r', '\n');
            var total = _characters - _trailingLineEndings;
            var limit = maxCharacters > 0 ? Math.Min(maxCharacters, CapacityBytes) : CapacityBytes;
            var start = Math.Max(0, output.Length - limit);
            if (start > 0 && char.IsLowSurrogate(output[start]) && char.IsHighSurrogate(output[start - 1]))
                start++;
            output = output[start..];
            var omitted = total - output.Length;
            return (
                omitted > 0
                    ? $"... (truncated, {omitted} earlier chars){Environment.NewLine}{output}"
                    : output.Length == 0 ? "(no output)" : output,
                (int)Math.Min(total, int.MaxValue),
                omitted > 0);
        }
    }

    public static async Task<TerminalOutputBuffer> ReadAsync(string path, CancellationToken ct)
    {
        var buffer = new TerminalOutputBuffer();
        if (!File.Exists(path))
            return buffer;
        using var reader = new StreamReader(new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan), Encoding.UTF8);
        await foreach (var chunk in ReadChunksAsync(reader, ct))
            buffer.Append(chunk);
        return buffer;
    }

    internal static async IAsyncEnumerable<string> ReadChunksAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var chars = new char[4096];
        var pending = 0;
        while (true)
        {
            var read = await reader.ReadAsync(chars.AsMemory(pending, chars.Length - pending), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            var count = pending + read;
            pending = char.IsHighSurrogate(chars[count - 1]) ? 1 : 0;
            if (count > pending)
                yield return new string(chars, 0, count - pending);
            if (pending != 0)
                chars[0] = chars[count - 1];
        }
        if (pending != 0)
            yield return "\uFFFD";
    }

    internal static IEnumerable<string> SplitFrames(string text, int maxBytes)
    {
        var start = 0;
        var offset = 0;
        var bytes = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxBytes)
            {
                yield return text[start..offset];
                start = offset;
                bytes = 0;
            }
            offset += rune.Utf16SequenceLength;
            bytes += rune.Utf8SequenceLength;
        }
        if (offset > start)
            yield return text[start..offset];
    }
}
