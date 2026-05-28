using System.Text;
using System.Text.RegularExpressions;

namespace DotCraft.Tools;

/// <summary>
/// Shared search/replace application with multi-tier fuzzy matching (exact, line-trimmed,
/// indentation-flexible, whitespace-collapsed, unicode-normalized). Used by FileTools and SandboxFileTools.
/// </summary>
internal static class FileEditSearchReplace
{
    private static readonly Regex WhitespaceCollapseRegex = new(@"\s+", RegexOptions.Compiled);
    /// <summary>
    /// All inputs must be LF-normalized. Returns new LF-normalized content on success.
    /// When <paramref name="replaceAll"/> is true, only exact substring matches are used (no fuzzy fallbacks).
    /// </summary>
    internal static (bool Ok, string NewContent, string? Error, string? MatchKind, int LineNum, int OldLineCount, int ReplaceCount) Apply(
        string content,
        string oldText,
        string newText,
        bool replaceAll = false)
    {
        var count = CountOccurrences(content, oldText);
        if (count == 1)
        {
            var idx = content.IndexOf(oldText, StringComparison.Ordinal);
            var newContent = content[..idx] + newText + content[(idx + oldText.Length)..];
            var lineNum = content[..idx].Count(c => c == '\n') + 1;
            var oldLineCount = oldText.Count(c => c == '\n') + 1;
            return (true, newContent, null, null, lineNum, oldLineCount, 1);
        }

        if (count > 1)
        {
            if (!replaceAll)
            {
                return (false, content,
                    $"Error: Found {count} matches of oldText. To replace all, set replaceAll to true. To replace one, provide more context to make it unique.",
                    null, 0, 0, 0);
            }

            var replacedAll = content.Replace(oldText, newText, StringComparison.Ordinal);
            var firstIdx = content.IndexOf(oldText, StringComparison.Ordinal);
            var lineNumAll = content[..firstIdx].Count(c => c == '\n') + 1;
            var oldLineCountAll = oldText.Count(c => c == '\n') + 1;
            return (true, replacedAll, null, "replace all", lineNumAll, oldLineCountAll, count);
        }

        if (count == 0 && replaceAll)
        {
            var preview = content.Length > 50 ? content[..50] : content;
            return (false, content,
                $"Error: oldText not found in file. Make sure it matches the content. File has {content.Length} chars. First 50 chars: \"{preview}\"",
                null, 0, 0, 0);
        }

        var found = TryLineTrimmedMatch(content, oldText);
        if (found != null)
        {
            var idx = content.IndexOf(found, StringComparison.Ordinal);
            if (idx != -1)
            {
                var newContent = content[..idx] + newText + content[(idx + found.Length)..];
                var lineNum = content[..idx].Count(c => c == '\n') + 1;
                var oldLineCount = found.Count(c => c == '\n') + 1;
                return (true, newContent, null, "line-trimmed fallback", lineNum, oldLineCount, 1);
            }
        }

        found = TryIndentFlexibleMatch(content, oldText);
        if (found != null)
        {
            var idx = content.IndexOf(found, StringComparison.Ordinal);
            if (idx != -1)
            {
                var newContent = content[..idx] + newText + content[(idx + found.Length)..];
                var lineNum = content[..idx].Count(c => c == '\n') + 1;
                var oldLineCount = found.Count(c => c == '\n') + 1;
                return (true, newContent, null, "indentation-flexible fallback", lineNum, oldLineCount, 1);
            }
        }

        found = TryWhitespaceNormalizedMatch(content, oldText);
        if (found != null)
        {
            var idx = content.IndexOf(found, StringComparison.Ordinal);
            if (idx != -1)
            {
                var newContent = content[..idx] + newText + content[(idx + found.Length)..];
                var lineNum = content[..idx].Count(c => c == '\n') + 1;
                var oldLineCount = found.Count(c => c == '\n') + 1;
                return (true, newContent, null, "whitespace-normalized fallback", lineNum, oldLineCount, 1);
            }
        }

        found = TryUnicodeNormalizedMatch(content, oldText);
        if (found != null)
        {
            var idx = content.IndexOf(found, StringComparison.Ordinal);
            if (idx != -1)
            {
                var newContent = content[..idx] + newText + content[(idx + found.Length)..];
                var lineNum = content[..idx].Count(c => c == '\n') + 1;
                var oldLineCount = found.Count(c => c == '\n') + 1;
                return (true, newContent, null, "unicode-normalized fallback", lineNum, oldLineCount, 1);
            }
        }

        var first50 = content.Length > 50 ? content[..50] : content;
        return (false, content,
            $"Error: oldText not found in file. Make sure it matches the content. File has {content.Length} chars. First 50 chars: \"{first50}\"",
            null, 0, 0, 0);
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

    private static string? TryLineTrimmedMatch(string content, string oldText)
    {
        var contentLines = content.Split('\n');
        var searchLines = oldText.Split('\n');
        if (searchLines.Length == 0) return null;

        string? uniqueMatch = null;
        for (var i = 0; i <= contentLines.Length - searchLines.Length; i++)
        {
            var allMatch = true;
            for (var j = 0; j < searchLines.Length; j++)
            {
                if (contentLines[i + j].Trim() != searchLines[j].Trim())
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                var block = string.Join("\n", contentLines.Skip(i).Take(searchLines.Length));
                if (uniqueMatch != null) return null;
                uniqueMatch = block;
            }
        }

        return uniqueMatch;
    }

    private static string? TryIndentFlexibleMatch(string content, string oldText)
    {
        var contentLines = content.Split('\n');
        var searchLines = oldText.Split('\n');
        if (searchLines.Length == 0) return null;

        var searchDeindented = DeindentLines(searchLines);
        string? uniqueMatch = null;

        for (var i = 0; i <= contentLines.Length - searchLines.Length; i++)
        {
            var blockLines = contentLines.Skip(i).Take(searchLines.Length).ToArray();
            var blockDeindented = DeindentLines(blockLines);
            if (blockDeindented.Length != searchDeindented.Length) continue;

            var allMatch = true;
            for (var j = 0; j < searchDeindented.Length; j++)
            {
                if (blockDeindented[j] != searchDeindented[j])
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                var block = string.Join("\n", blockLines);
                if (uniqueMatch != null) return null;
                uniqueMatch = block;
            }
        }

        return uniqueMatch;
    }

    /// <summary>
    /// Line-by-line match after collapsing runs of whitespace to a single space (OpenCode-style).
    /// </summary>
    private static string? TryWhitespaceNormalizedMatch(string content, string oldText)
    {
        var contentLines = content.Split('\n');
        var searchLines = oldText.Split('\n');
        if (searchLines.Length == 0) return null;

        var normSearch = searchLines.Select(NormalizeWhitespaceLine).ToArray();
        string? uniqueMatch = null;

        for (var i = 0; i <= contentLines.Length - searchLines.Length; i++)
        {
            var allMatch = true;
            for (var j = 0; j < searchLines.Length; j++)
            {
                if (NormalizeWhitespaceLine(contentLines[i + j]) != normSearch[j])
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                var block = string.Join("\n", contentLines.Skip(i).Take(searchLines.Length));
                if (uniqueMatch != null) return null;
                uniqueMatch = block;
            }
        }

        return uniqueMatch;
    }

    private static string NormalizeWhitespaceLine(string line)
    {
        var trimmed = line.TrimEnd('\r');
        return WhitespaceCollapseRegex.Replace(trimmed.Trim(), " ");
    }

    /// <summary>
    /// Line-by-line match after Unicode punctuation normalization.
    /// </summary>
    private static string? TryUnicodeNormalizedMatch(string content, string oldText)
    {
        var contentLines = content.Split('\n');
        var searchLines = oldText.Split('\n');
        if (searchLines.Length == 0) return null;

        var normSearch = searchLines.Select(NormalizeUnicodeLine).ToArray();
        string? uniqueMatch = null;

        for (var i = 0; i <= contentLines.Length - searchLines.Length; i++)
        {
            var allMatch = true;
            for (var j = 0; j < searchLines.Length; j++)
            {
                if (NormalizeUnicodeLine(contentLines[i + j]) != normSearch[j])
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                var block = string.Join("\n", contentLines.Skip(i).Take(searchLines.Length));
                if (uniqueMatch != null) return null;
                uniqueMatch = block;
            }
        }

        return uniqueMatch;
    }

    private static string NormalizeUnicodeLine(string line)
    {
        var trimmed = line.TrimEnd('\r').Trim();
        if (trimmed.Length == 0)
            return trimmed;

        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
            sb.Append(NormalizeUnicodeChar(c));
        return sb.ToString();
    }

    private static char NormalizeUnicodeChar(char c)
    {
        return c switch
        {
            '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
            '\u2018' or '\u2019' or '\u201A' or '\u201B' => '\'',
            '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
            '\u00A0' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or '\u2006' or '\u2007' or '\u2008'
                or '\u2009' or '\u200A' or '\u202F' or '\u205F' or '\u3000' => ' ',
            _ => c
        };
    }

    private static string[] DeindentLines(string[] lines)
    {
        var trimmed = lines.Select(l => l.TrimEnd('\r')).ToArray();
        var nonEmpty = trimmed.Where(l => l.Trim().Length > 0).ToArray();
        if (nonEmpty.Length == 0) return trimmed;

        var minIndent = nonEmpty.Min(l => l.Length - l.TrimStart().Length);
        return trimmed.Select(l => l.Length > minIndent ? l[minIndent..] : l.TrimStart()).ToArray();
    }
}
