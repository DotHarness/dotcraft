using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotCraft.Memory;

/// <summary>One non-blank line of a loaded index, with the memory file it points to when it is a pointer.</summary>
public sealed record MemoryIndexLine(string Text, string? Target);

/// <summary>A store's <c>MEMORY.md</c> as the model sees it: the part within the load limits, then notices about the file.</summary>
public sealed partial class MemoryIndex
{
    public const int MaxLines = 200;

    public const int MaxBytes = 25_000;

    private const int MaxPointerLength = 200;

    private const string CompactAdvice =
        "Compact it now: one line per entry, detail moved into memory files, stale entries merged or dropped.";

    private MemoryIndex(string content, IReadOnlyList<MemoryIndexLine> lines, IReadOnlyList<string> notices)
    {
        Content = content;
        Lines = lines;
        Notices = notices;
    }

    /// <summary>The loaded part of the index, without notices.</summary>
    public string Content { get; }

    public IReadOnlyList<MemoryIndexLine> Lines { get; }

    public IReadOnlyList<string> Notices { get; }

    public bool IsEmpty => Content.Length == 0;

    /// <summary>The loaded part followed by its notices.</summary>
    public string Text => Notices.Count == 0 ? Content : Content + "\n\n" + string.Join("\n\n", Notices);

    public static MemoryIndex Load(string storeDirectory) =>
        Parse(MemoryStore.ReadIndexText(Path.Combine(storeDirectory, MemoryStore.IndexFileName)));

    public static MemoryIndex Parse(string text)
    {
        var trimmed = Normalize(text);
        if (trimmed.Length == 0)
            return new MemoryIndex(string.Empty, [], []);

        var allLines = trimmed.Split('\n');
        var byteCount = Encoding.UTF8.GetByteCount(trimmed);
        var overLines = allLines.Length > MaxLines;
        var overBytes = byteCount > MaxBytes;

        var content = overLines ? string.Join('\n', allLines[..MaxLines]).TrimEnd() : trimmed;
        content = CutAtByteLimit(content);

        var notices = new List<string>();
        if (overLines || overBytes)
        {
            var reason = (overLines, overBytes) switch
            {
                (true, true) => Invariant($"{allLines.Length} lines and {byteCount:N0} bytes, over its {MaxLines}-line and {MaxBytes:N0}-byte limits"),
                (true, false) => Invariant($"{allLines.Length} lines, over its {MaxLines}-line limit"),
                _ => Invariant($"{byteCount:N0} bytes, over its {MaxBytes:N0}-byte limit")
            };
            notices.Add(
                $"> WARNING: MEMORY.md is {reason}. Only part of it was loaded. Keep index entries to one line under ~200 characters; move detail into memory files.");
        }

        if (allLines.Any(static line => !string.IsNullOrWhiteSpace(line) && !IsPointer(line)))
        {
            notices.Add(
                "> MEMORY.md holds content that belongs in memory files. Move it into memory files the next time you save a memory.");
        }

        var lines = content.Split('\n')
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => new MemoryIndexLine(line, TargetOf(line)))
            .ToArray();
        return new MemoryIndex(content, lines, notices);
    }

    /// <summary>The notice a write of <paramref name="content"/> to the store's index ends with when the index fills more than 80% of a load limit, or null.</summary>
    public static string? WriteNotice(string storeDirectory, string fullPath, string content)
    {
        if (!MemoryStore.IsIndexFile(storeDirectory, fullPath))
            return null;

        var trimmed = Normalize(content);
        var lineCount = trimmed.Length == 0 ? 0 : trimmed.Count(static c => c == '\n') + 1;
        var byteCount = Encoding.UTF8.GetByteCount(trimmed);
        if (lineCount * 5 <= MaxLines * 4 && byteCount * 5 <= MaxBytes * 4)
            return null;

        var usage = Invariant($"{lineCount} of {MaxLines} lines, {byteCount:N0} of {MaxBytes:N0} bytes");
        return lineCount > MaxLines || byteCount > MaxBytes
            ? $"MEMORY.md is over its load limit ({usage}), so entries past the limit are not shown. {CompactAdvice}"
            : $"MEMORY.md is near its load limit ({usage}). {CompactAdvice}";
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string CutAtByteLimit(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        if (bytes.Length <= MaxBytes)
            return content;

        var cut = Array.LastIndexOf(bytes, (byte)'\n', MaxBytes);
        if (cut <= 0)
        {
            cut = MaxBytes;
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
                cut--;
        }

        return Encoding.UTF8.GetString(bytes, 0, cut).TrimEnd();
    }

    private static bool IsPointer(string line) => line.Length <= MaxPointerLength && TargetOf(line) != null;

    private static string? TargetOf(string line)
    {
        var match = Pointer().Match(line);
        if (!match.Success)
            return null;

        var target = match.Groups["target"].Value.Replace('\\', '/');
        return target.StartsWith("./", StringComparison.Ordinal) ? target[2..] : target;
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^\s*[-*+]\s+\[[^\]]*\]\((?<target>[^)\s]+)\)")]
    private static partial Regex Pointer();
}
