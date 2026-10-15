using System.Text;

namespace DotCraft.Tools.BackgroundTerminals;

internal sealed class TerminalOutputBuffer
{
    internal const int CapacityBytes = 1024 * 1024;
    private readonly StringBuilder _head = new();
    private readonly LinkedList<string> _tailChunks = new();
    private readonly object _sync = new();
    private int _headBytes;
    private bool _headComplete;
    private int _tailBytes;
    private long _characters;
    private long _trailingLineEndings;

    public void Append(string text)
    {
        lock (_sync)
        {
            if (!_headComplete)
            {
                var headCharacters = 0;
                foreach (var rune in text.EnumerateRunes())
                {
                    if (_headBytes + rune.Utf8SequenceLength > CapacityBytes / 2)
                    {
                        _headComplete = true;
                        break;
                    }
                    _headBytes += rune.Utf8SequenceLength;
                    headCharacters += rune.Utf16SequenceLength;
                }
                _head.Append(text.AsSpan(0, headCharacters));
            }
            foreach (var chunk in SplitFrames(text, 8192))
            {
                _tailChunks.AddLast(chunk);
                _tailBytes += Encoding.UTF8.GetByteCount(chunk);
                while (_tailBytes > CapacityBytes / 2)
                {
                    var firstChunk = _tailChunks.First!.Value;
                    _tailChunks.RemoveFirst();
                    var removeChars = 0;
                    foreach (var rune in firstChunk.EnumerateRunes())
                    {
                        _tailBytes -= rune.Utf8SequenceLength;
                        removeChars += rune.Utf16SequenceLength;
                        if (_tailBytes <= CapacityBytes / 2) break;
                    }
                    if (removeChars < firstChunk.Length)
                        _tailChunks.AddFirst(firstChunk[removeChars..]);
                }
            }
            _characters += text.Length;
            var tail = text.Length - text.TrimEnd('\r', '\n').Length;
            _trailingLineEndings = tail == text.Length ? _trailingLineEndings + tail : tail;
        }
    }

    public long Characters
    {
        get
        {
            lock (_sync)
                return _characters;
        }
    }

    /// <summary>
    /// Returns output from character offset <paramref name="from"/> to the current end. When the
    /// retained excerpts no longer reach back to that offset, only the retained tail is returned.
    /// </summary>
    public TerminalOutputSlice Slice(long from, int maxCharacters, bool tailOnly = false)
    {
        lock (_sync)
        {
            var end = _characters;
            from = Math.Clamp(from, 0, end);
            var tail = string.Concat(_tailChunks);
            var tailStart = end - tail.Length;
            var head = _head.ToString();
            string text;
            var lossy = false;
            if (from >= tailStart)
                text = tail[(int)(from - tailStart)..];
            else if (head.Length >= tailStart)
                text = head[(int)from..(int)tailStart] + tail;
            else
            {
                text = tail;
                lossy = true;
            }

            var newChars = (int)Math.Min(end - from, int.MaxValue);
            text = text.TrimEnd('\r', '\n');
            var limit = maxCharacters > 0 ? Math.Min(maxCharacters, CapacityBytes) : CapacityBytes;
            if (text.Length <= limit)
                return new TerminalOutputSlice(text, end, newChars, false, lossy);
            if (tailOnly)
            {
                var start = text.Length - limit;
                if (char.IsLowSurrogate(text[start]))
                    start++;
                return new TerminalOutputSlice(text[start..], end, newChars, true, lossy);
            }

            var headLength = (limit + 1) / 2;
            if (char.IsHighSurrogate(text[headLength - 1]))
                headLength--;
            var tailFrom = text.Length - (limit - headLength);
            if (char.IsLowSurrogate(text[tailFrom]))
                tailFrom++;
            var omitted = tailFrom - headLength;
            return new TerminalOutputSlice(
                $"{text[..headLength]}{Environment.NewLine}... (truncated, {omitted} middle chars){Environment.NewLine}{text[tailFrom..]}",
                end,
                newChars,
                true,
                lossy);
        }
    }

    public (string Text, long End, bool Lossy) SliceRaw(long from)
    {
        lock (_sync)
        {
            var end = _characters;
            from = Math.Clamp(from, 0, end);
            var tail = string.Concat(_tailChunks);
            var tailStart = end - tail.Length;
            if (from >= tailStart)
                return (tail[(int)(from - tailStart)..], end, false);
            var head = _head.ToString();
            return head.Length >= tailStart
                ? (head[(int)from..(int)tailStart] + tail, end, false)
                : (tail, end, true);
        }
    }

    public (string Output, int OriginalChars, bool Truncated) Snapshot(int maxCharacters)
    {
        lock (_sync)
        {
            var total = _characters - _trailingLineEndings;
            var limit = maxCharacters > 0 ? Math.Min(maxCharacters, CapacityBytes) : CapacityBytes;
            var head = _head.ToString();
            head = head[..(int)Math.Min(head.Length, total)];
            var tail = string.Concat(_tailChunks).TrimEnd('\r', '\n');
            var overlap = head.Length + tail.Length - total;
            if (total <= limit && overlap >= 0)
            {
                var complete = head + tail[(int)overlap..];
                return (complete.Length == 0 ? "(no output)" : complete, (int)total, false);
            }
            var headLength = Math.Min(head.Length, (limit + 1) / 2);
            if (headLength > 0 && headLength < head.Length && char.IsHighSurrogate(head[headLength - 1]))
                headLength--;
            var tailStart = Math.Max(0, tail.Length - (limit - headLength));
            if (tailStart < tail.Length && char.IsLowSurrogate(tail[tailStart]))
                tailStart++;
            head = head[..headLength];
            tail = tail[tailStart..];
            var omitted = total - head.Length - tail.Length;
            return (
                $"{head}{Environment.NewLine}... (truncated, {omitted} middle chars){Environment.NewLine}{tail}",
                (int)Math.Min(total, int.MaxValue),
                true);
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

/// <summary>
/// Output read from a terminal cursor. <see cref="End"/> is the cursor after the read, and
/// <see cref="Lossy"/> means earlier unread output is only in the log file.
/// </summary>
internal sealed record TerminalOutputSlice(string Text, long End, int NewChars, bool Truncated, bool Lossy);
