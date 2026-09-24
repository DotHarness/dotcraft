using System.Text;
using DiffPlex;
using DiffPlex.Model;

namespace DotCraft.Tools;

internal sealed record UnifiedDiffLimits(int MaxDiffChars, int MaxMyersLines)
{
    internal static readonly UnifiedDiffLimits PerCall = new(128 * 1024, 4_000);
    internal static readonly UnifiedDiffLimits Aggregate = new(1024 * 1024, 4_000);
}

/// <summary>
/// <see cref="Diff"/> is null when nothing changed or when the diff exceeds the limits, which sets <see cref="Truncated"/>.
/// </summary>
internal sealed record UnifiedDiffResult(string? Diff, int Additions, int Deletions, bool Truncated);

internal static class UnifiedDiffRenderer
{
    private const int ContextLines = 3;
    private const string RegularFileMode = "100644";

    private static readonly UnifiedDiffResult NoChange = new(null, 0, 0, false);

    internal static UnifiedDiffResult Render(string displayPath, string? before, string? after, UnifiedDiffLimits limits)
    {
        if (before == after)
            return NoChange;

        var oldLines = LineFeedChunker.Instance.Chunk(before ?? "");
        var newLines = LineFeedChunker.Instance.Chunk(after ?? "");
        var blocks = ComputeBlocks(oldLines, newLines, limits.MaxMyersLines);
        var additions = blocks.Sum(b => b.InsertCountB);
        var deletions = blocks.Sum(b => b.DeleteCountA);

        if ((long)(before?.Length ?? 0) + (after?.Length ?? 0) > 2L * limits.MaxDiffChars)
            return new UnifiedDiffResult(null, additions, deletions, true);

        var diff = new StringBuilder();
        AppendHeader(diff, displayPath.Replace('\\', '/'), before, after, blocks.Count > 0);
        foreach (var hunk in GroupIntoHunks(blocks))
            AppendHunk(diff, hunk, oldLines, newLines);

        return diff.Length > limits.MaxDiffChars
            ? new UnifiedDiffResult(null, additions, deletions, true)
            : new UnifiedDiffResult(diff.ToString(), additions, deletions, false);
    }

    private static List<DiffBlock> ComputeBlocks(string[] oldLines, string[] newLines, int maxMyersLines)
    {
        var shared = Math.Min(oldLines.Length, newLines.Length);
        var prefix = 0;
        while (prefix < shared && oldLines[prefix] == newLines[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < shared - prefix && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)])
            suffix++;

        var oldCount = oldLines.Length - prefix - suffix;
        var newCount = newLines.Length - prefix - suffix;
        if (oldCount == 0 && newCount == 0)
            return [];

        // Myers costs O((N+M)·D), so past the cap the changed middle becomes one replace block
        // instead of letting a large rewrite stall the turn.
        if (oldCount == 0 || newCount == 0 || Math.Max(oldCount, newCount) > maxMyersLines)
            return [new DiffBlock(prefix, oldCount, prefix, newCount)];

        var middle = Differ.Instance.CreateDiffs(
            string.Concat(oldLines.AsSpan(prefix, oldCount)),
            string.Concat(newLines.AsSpan(prefix, newCount)),
            ignoreWhiteSpace: false,
            ignoreCase: false,
            LineFeedChunker.Instance);
        return middle.DiffBlocks
            .Select(b => new DiffBlock(b.DeleteStartA + prefix, b.DeleteCountA, b.InsertStartB + prefix, b.InsertCountB))
            .ToList();
    }

    private static void AppendHeader(StringBuilder diff, string path, string? before, string? after, bool hasHunks)
    {
        diff.Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n');
        if (before is null)
            diff.Append("new file mode ").Append(RegularFileMode).Append('\n');
        else if (after is null)
            diff.Append("deleted file mode ").Append(RegularFileMode).Append('\n');

        diff.Append("index ")
            .Append(before is null ? GitBlobOid.Zero : GitBlobOid.Compute(before))
            .Append("..")
            .Append(after is null ? GitBlobOid.Zero : GitBlobOid.Compute(after))
            .Append('\n');

        // Like git, an empty file that is created or deleted has no file headers.
        if (!hasHunks)
            return;
        diff.Append(before is null ? "--- /dev/null\n" : $"--- a/{path}\n");
        diff.Append(after is null ? "+++ /dev/null\n" : $"+++ b/{path}\n");
    }

    private static List<List<DiffBlock>> GroupIntoHunks(List<DiffBlock> blocks)
    {
        var hunks = new List<List<DiffBlock>>();
        foreach (var block in blocks)
        {
            var previous = hunks.Count > 0 ? hunks[^1][^1] : null;
            if (previous != null && block.DeleteStartA - (previous.DeleteStartA + previous.DeleteCountA) <= 2 * ContextLines)
                hunks[^1].Add(block);
            else
                hunks.Add([block]);
        }
        return hunks;
    }

    private static void AppendHunk(StringBuilder diff, List<DiffBlock> hunk, string[] oldLines, string[] newLines)
    {
        var first = hunk[0];
        var last = hunk[^1];
        var leading = Math.Min(ContextLines, first.DeleteStartA);
        var trailing = Math.Min(ContextLines, oldLines.Length - (last.DeleteStartA + last.DeleteCountA));
        var oldStart = first.DeleteStartA - leading;
        var newStart = first.InsertStartB - leading;
        var oldEnd = last.DeleteStartA + last.DeleteCountA + trailing;
        var newEnd = last.InsertStartB + last.InsertCountB + trailing;

        diff.Append("@@ -").Append(FormatRange(oldStart, oldEnd - oldStart))
            .Append(" +").Append(FormatRange(newStart, newEnd - newStart))
            .Append(" @@\n");

        var line = oldStart;
        foreach (var block in hunk)
        {
            for (; line < block.DeleteStartA; line++)
                AppendLine(diff, ' ', oldLines[line]);
            for (var i = 0; i < block.DeleteCountA; i++)
                AppendLine(diff, '-', oldLines[block.DeleteStartA + i]);
            for (var i = 0; i < block.InsertCountB; i++)
                AppendLine(diff, '+', newLines[block.InsertStartB + i]);
            line = block.DeleteStartA + block.DeleteCountA;
        }
        for (; line < oldEnd; line++)
            AppendLine(diff, ' ', oldLines[line]);
    }

    // An empty range names the line it follows, so a new file reads "-0,0".
    private static string FormatRange(int start, int count) => count switch
    {
        0 => $"{start},0",
        1 => $"{start + 1}",
        _ => $"{start + 1},{count}",
    };

    private static void AppendLine(StringBuilder diff, char tag, string line)
    {
        diff.Append(tag).Append(line);
        if (!line.EndsWith('\n'))
            diff.Append("\n\\ No newline at end of file\n");
    }

    // DiffPlex's LineEndingsPreservingChunker also breaks on a lone '\r', which git does not.
    private sealed class LineFeedChunker : IChunker
    {
        internal static readonly LineFeedChunker Instance = new();

        public string[] Chunk(string str)
        {
            var lines = new List<string>();
            for (var start = 0; start < str.Length;)
            {
                var end = str.IndexOf('\n', start);
                end = end < 0 ? str.Length : end + 1;
                lines.Add(str[start..end]);
                start = end;
            }
            return [.. lines];
        }
    }
}
