using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Memory;

namespace DotCraft.Context.WorldState;

/// <summary>Entries are keyed by the memory file they point to, so an edit to one entry restates only that entry.</summary>
internal sealed class MemoryIndexSection : IWorldStateSection
{
    public const string SectionId = "memory_index";

    private const string Heading = "## Memory Index";

    private const string NonPointerKeyPrefix = "line:";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        if (context.MemoryStore is not { } store)
            return new JsonObject();

        var index = MemoryIndex.Load(store.MemoryDirectoryPath);
        var entries = new JsonObject();
        foreach (var entry in Entries(index))
            entries[entry.Key] = Fingerprint(entry.Lines);

        // Keep an object even when the index is empty, so an empty index stays distinct from an absent one.
        var snapshot = new JsonObject { ["entries"] = entries };
        if (index.Notices.Count > 0)
            snapshot["notices"] = Fingerprint(index.Notices);
        return snapshot;
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        if (context.MemoryStore is not { } store)
            return null;

        var index = MemoryIndex.Load(store.MemoryDirectoryPath);
        if (previous.TryGetKnown(out var known) && known["entries"] is JsonObject shown)
            return RenderUpdate(index, shown, known["notices"]?.GetValue<string>());

        var replacement = previous.MayBeShown ? "This index replaces any memory index shown earlier.\n" : string.Empty;
        return index.IsEmpty
            ? $"{Heading}\n{replacement}MEMORY.md is empty. Saved memories will be listed here."
            : $"{Heading}\n{replacement}Contents of {Path.GetFullPath(store.IndexFilePath)}:\n\n{index.Text}";
    }

    private static string? RenderUpdate(MemoryIndex index, JsonObject shown, string? shownNotices)
    {
        var entries = Entries(index);
        if (index.IsEmpty)
            return shown.Count == 0 ? null : $"{Heading}\nMEMORY.md is now empty.";

        var current = entries.Select(static entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        var changed = entries
            .Where(entry => shown[entry.Key]?.GetValue<string>() != Fingerprint(entry.Lines))
            .SelectMany(static entry => entry.Lines)
            .ToArray();
        var removed = shown
            .Select(static entry => entry.Key)
            .Where(key => !current.Contains(key))
            .ToArray();
        var removedFiles = removed
            .Where(static key => !key.StartsWith(NonPointerKeyPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var removedLines = removed.Length - removedFiles.Length;
        IReadOnlyList<string> notices = index.Notices.Count > 0 && Fingerprint(index.Notices) != shownNotices
            ? index.Notices
            : [];
        if (changed.Length == 0 && removed.Length == 0 && notices.Count == 0)
            return null;

        var text = new StringBuilder($"{Heading}\nMEMORY.md changed since it was last shown. These entries update that index.");
        if (changed.Length > 0)
            text.Append("\n\nAdded or changed:\n").AppendJoin('\n', changed);
        if (removed.Length > 0)
        {
            text.Append("\n\nRemoved:");
            foreach (var file in removedFiles)
                text.Append("\n- ").Append(file);
            if (removedLines > 0)
                text.Append($"\n- {removedLines} line(s) that were not pointers");
        }

        if (notices.Count > 0)
            text.Append("\n\n").AppendJoin("\n\n", notices);
        return text.ToString();
    }

    private static IReadOnlyList<(string Key, List<string> Lines)> Entries(MemoryIndex index)
    {
        var entries = new List<(string Key, List<string> Lines)>();
        var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var line in index.Lines)
        {
            var key = line.Target ?? NonPointerKeyPrefix + WorldStateHash.Of(line.Text);
            if (!byKey.TryGetValue(key, out var lines))
            {
                lines = [];
                byKey[key] = lines;
                entries.Add((key, lines));
            }

            lines.Add(line.Text);
        }

        return entries;
    }

    private static string Fingerprint(IEnumerable<string> lines) => WorldStateHash.Of(string.Join('\n', lines));
}
