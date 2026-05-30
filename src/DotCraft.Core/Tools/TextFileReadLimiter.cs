using System.Text;

namespace DotCraft.Tools;

internal static class TextFileReadLimiter
{
    public const int DefaultReadLimit = 2000;
    public const int MaxLineLength = 2000;
    public const int MaxUnpaginatedTextBytes = 256 * 1024;

    public static bool IsPagedRead(int offset, int limit)
        => offset > 0 || limit > 0;

    public static string FormatUnpaginatedTooLarge(string path, long byteLength)
        => $"Error: File is too large to read without pagination ({byteLength:N0} bytes; max unpaginated read: {MaxUnpaginatedTextBytes:N0} bytes). Use ReadFile with offset=1 and limit=200, or use GrepFiles to search within it: {path}";

    public static async Task<string> ReadPageAsync(
        string fullPath,
        Encoding encoding,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var startLine = NormalizeOffset(offset);
        var readLimit = NormalizeLimit(limit);
        var lines = new List<string>();
        var lineNumber = 0;
        var hasMore = false;

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
                break;

            lineNumber++;
            if (lineNumber < startLine)
                continue;

            if (lines.Count < readLimit)
            {
                lines.Add(line);
                continue;
            }

            hasMore = true;
            break;
        }

        if (lines.Count == 0 && lineNumber < startLine && !(startLine == 1 && lineNumber == 0))
            return $"Error: Offset {startLine} is out of range for this file ({lineNumber} lines).";

        return FormatLines(lines, startLine, hasMore, hasMore ? null : lineNumber);
    }

    public static string FormatInMemory(string content, int offset, int limit)
    {
        var pagedRead = IsPagedRead(offset, limit);
        var startLine = NormalizeOffset(offset);
        var readLimit = pagedRead ? NormalizeLimit(limit) : int.MaxValue;
        var lines = new List<string>();
        var lineNumber = 0;
        var hasMore = false;

        using var reader = new StringReader(content);
        while (true)
        {
            var line = reader.ReadLine();
            if (line == null)
                break;

            lineNumber++;
            if (lineNumber < startLine)
                continue;

            if (lines.Count < readLimit)
            {
                lines.Add(line);
                continue;
            }

            hasMore = true;
            break;
        }

        if (pagedRead && lines.Count == 0 && lineNumber < startLine && !(startLine == 1 && lineNumber == 0))
            return $"Error: Offset {startLine} is out of range for this file ({lineNumber} lines).";

        return FormatLines(lines, startLine, hasMore, hasMore ? null : lineNumber);
    }

    private static int NormalizeOffset(int offset)
        => offset > 0 ? offset : 1;

    private static int NormalizeLimit(int limit)
        => limit > 0 ? limit : DefaultReadLimit;

    private static string FormatLines(
        IReadOnlyList<string> lines,
        int startLine,
        bool hasMore,
        int? totalLineCount)
    {
        if (lines.Count == 0)
            return "(Empty file - total 0 lines)";

        var sb = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = TruncateLine(lines[i]);
            sb.AppendLine($"{startLine + i}: {line}");
        }

        var endLine = startLine + lines.Count - 1;
        if (hasMore)
            sb.AppendLine($"\n(Showing lines {startLine}-{endLine}. Use offset={endLine + 1} to read more.)");
        else
            sb.AppendLine($"\n(End of file - total {totalLineCount ?? endLine} lines)");

        return sb.ToString();
    }

    private static string TruncateLine(string line)
        => line.Length > MaxLineLength
            ? line[..MaxLineLength] + "..."
            : line;
}
