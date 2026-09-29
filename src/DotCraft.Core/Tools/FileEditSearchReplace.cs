using System.Text;

namespace DotCraft.Tools;

internal static class FileEditSearchReplace
{
    private enum MatchStatus { NotFound, Unique, Ambiguous }

    private readonly record struct LineMatch(MatchStatus Status, int Start = 0, int Length = 0, int Line = 0);

    /// <summary>
    /// All inputs must be LF-normalized. Fuzzy comparisons locate source ranges without rewriting their contents.
    /// When <paramref name="replaceAll"/> is true, only exact substring matches are used.
    /// </summary>
    internal static (bool Ok, string NewContent, string? Error, string? MatchKind, int LineNum, int OldLineCount, int ReplaceCount) Apply(
        string content,
        string oldText,
        string newText,
        bool replaceAll = false)
    {
        if (oldText.Length == 0)
            return (false, content, "Error: oldText is required.", null, 0, 0, 0);

        var count = CountOccurrences(content, oldText);
        if (count > 1)
        {
            if (!replaceAll)
            {
                return (false, content,
                    $"Error: Found {count} matches of oldText. To replace all, set replaceAll to true. To replace one, provide more context to make it unique.",
                    null, 0, 0, 0);
            }

            var firstIdx = content.IndexOf(oldText, StringComparison.Ordinal);
            return (true, content.Replace(oldText, newText, StringComparison.Ordinal), null, "replace all",
                content[..firstIdx].Count(c => c == '\n') + 1, oldText.Count(c => c == '\n') + 1, count);
        }

        var match = default(LineMatch);
        string? matchKind = null;
        var oldLineCount = oldText.Count(c => c == '\n') + 1;
        if (count == 1)
        {
            var idx = content.IndexOf(oldText, StringComparison.Ordinal);
            match = new LineMatch(MatchStatus.Unique, idx, oldText.Length, content[..idx].Count(c => c == '\n') + 1);
        }
        else if (!replaceAll)
        {
            var contentLines = content.Split('\n');
            var searchLines = oldText.Split('\n');
            var lineStarts = new int[contentLines.Length];
            for (var i = 1; i < lineStarts.Length; i++)
                lineStarts[i] = lineStarts[i - 1] + contentLines[i - 1].Length + 1;

            (string Kind, Func<string, string> Normalize)[] stages =
            [
                ("trailing-whitespace fallback", static line => line.TrimEnd()),
                ("line-trimmed fallback", static line => line.Trim()),
                ("unicode-normalized fallback", NormalizeUnicodeLine),
            ];
            foreach (var (kind, normalize) in stages)
            {
                match = FindLineMatch(contentLines, searchLines, lineStarts, normalize);
                if (match.Status == MatchStatus.Ambiguous)
                {
                    return (false, content,
                        $"Error: Found multiple matches of oldText using {kind}. Provide more context to make it unique.",
                        null, 0, 0, 0);
                }
                if (match.Status == MatchStatus.Unique)
                {
                    matchKind = kind;
                    break;
                }
            }
        }

        if (match.Status == MatchStatus.NotFound)
        {
            var preview = content.Length > 50 ? content[..50] : content;
            return (false, content,
                $"Error: oldText not found in file. Make sure it matches the content. File has {content.Length} chars. First 50 chars: \"{preview}\"",
                null, 0, 0, 0);
        }

        var newContent = content[..match.Start] + newText + content[(match.Start + match.Length)..];
        return (true, newContent, null, matchKind, match.Line, oldLineCount, 1);
    }

    private static int CountOccurrences(string content, string searchText)
    {
        var count = 0;
        var pos = 0;
        while ((pos = content.IndexOf(searchText, pos, StringComparison.Ordinal)) != -1)
        {
            count++;
            pos += searchText.Length;
        }
        return count;
    }

    private static LineMatch FindLineMatch(
        string[] contentLines, string[] searchLines, int[] lineStarts, Func<string, string> normalize)
    {
        var normalizedContent = contentLines.Select(normalize).ToArray();
        var normalizedSearch = searchLines.Select(normalize).ToArray();
        var match = default(LineMatch);
        for (var i = 0; i <= contentLines.Length - searchLines.Length; i++)
        {
            var allMatch = true;
            for (var j = 0; j < searchLines.Length; j++)
            {
                if (normalizedContent[i + j] != normalizedSearch[j])
                {
                    allMatch = false;
                    break;
                }
            }
            if (!allMatch)
                continue;
            if (match.Status == MatchStatus.Unique)
                return new LineMatch(MatchStatus.Ambiguous);

            var last = i + searchLines.Length - 1;
            match = new LineMatch(MatchStatus.Unique, lineStarts[i],
                lineStarts[last] + contentLines[last].Length - lineStarts[i], i + 1);
        }
        return match;
    }

    private static string NormalizeUnicodeLine(string line)
    {
        var trimmed = line.Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            sb.Append(c switch
            {
                '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
                '\u2018' or '\u2019' or '\u201A' or '\u201B' => '\'',
                '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
                '\u00A0' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or '\u2006' or '\u2007' or '\u2008'
                    or '\u2009' or '\u200A' or '\u202F' or '\u205F' or '\u3000' => ' ',
                _ => c
            });
        }
        return sb.ToString();
    }
}
