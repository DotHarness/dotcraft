using System.Text;
using System.Text.Json.Nodes;
using DotCraft.Memory;

namespace DotCraft.Context.WorldState;

/// <summary>Entries are keyed by the memory file they point to, so an edit to one entry restates only that entry.</summary>
internal sealed class MemoryIndexSection : IWorldStateSection
{
    public const string SectionId = "memory_index";

    private const string Heading = "## Memory Index";

    private const string OwnHeading = "### Your memory";

    private const string NonPointerKeyPrefix = "line:";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        if (context.MemoryStore is not { } store)
            return new JsonObject();

        // Keep an object even when the index is empty, so an empty index stays distinct from an absent one.
        var snapshot = IndexSnapshot(MemoryIndex.Load(store.MemoryDirectoryPath));
        var sideStores = SideStores(context);
        if (sideStores.Count > 0)
        {
            var stores = new JsonObject();
            foreach (var side in sideStores)
            {
                var storeSnapshot = IndexSnapshot(MemoryIndex.Load(side.DirectoryPath));
                storeSnapshot["name"] = side.Name;
                stores[side.Id] = storeSnapshot;
            }
            snapshot["stores"] = stores;
        }

        return snapshot;
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        if (context.MemoryStore is not { } store)
            return null;

        var index = MemoryIndex.Load(store.MemoryDirectoryPath);
        if (previous.TryGetKnown(out var known) && known["entries"] is JsonObject shown)
            return RenderUpdate(context, index, shown, known["notices"]?.GetValue<string>(), known["stores"] as JsonObject);

        var text = new StringBuilder(Heading).Append('\n');
        if (previous.MayBeShown)
            text.Append("This index replaces any memory index shown earlier.\n");
        if (Personal(context) is { } personal)
            text.Append(RenderFull(personal)).Append("\n\n").Append(OwnHeading).Append('\n');
        text.Append(index.IsEmpty
            ? "MEMORY.md is empty. Saved memories will be listed here."
            : $"Contents of {Path.GetFullPath(store.IndexFilePath)}:\n\n{index.Text}");
        foreach (var attached in Attached(context))
            text.Append("\n\n").Append(RenderFull(attached));
        return text.ToString();
    }

    private static string? RenderUpdate(
        WorldStateContext context,
        MemoryIndex index,
        JsonObject shown,
        string? shownNotices,
        JsonObject? shownStores)
    {
        var parts = new List<string>();
        var personal = Personal(context);
        if (personal is not null)
            AddUpdate(parts, personal, shownStores);

        string? own = null;
        if (index.IsEmpty)
        {
            if (shown.Count > 0)
                own = "MEMORY.md is now empty.";
        }
        else if (RenderEntryUpdate(index, shown, shownNotices) is { } update)
        {
            own = "MEMORY.md changed since it was last shown. These entries update that index." + update;
        }

        if (own is not null)
            parts.Add(personal is null ? own : $"{OwnHeading}\n{own}");

        foreach (var attached in Attached(context))
            AddUpdate(parts, attached, shownStores);

        if (shownStores is not null)
        {
            var current = SideStores(context).Select(static side => side.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var (id, shownStore) in shownStores)
            {
                if (!current.Contains(id))
                    parts.Add($"The shared memory {shownStore?["name"]?.GetValue<string>() ?? id} is no longer available. Disregard its memories.");
            }
        }

        return parts.Count == 0 ? null : $"{Heading}\n" + string.Join("\n\n", parts);
    }

    private static void AddUpdate(List<string> parts, SideStore side, JsonObject? shownStores)
    {
        if (shownStores?[side.Id] is not JsonObject shownStore
            || shownStore["entries"] is not JsonObject shownEntries)
        {
            parts.Add(RenderFull(side));
            return;
        }

        var index = MemoryIndex.Load(side.DirectoryPath);
        if (index.IsEmpty)
        {
            if (shownEntries.Count > 0)
                parts.Add($"### {side.Name}\nIts MEMORY.md is now empty.");
        }
        else if (RenderEntryUpdate(index, shownEntries, shownStore["notices"]?.GetValue<string>()) is { } update)
        {
            parts.Add($"### {side.Name}\nIts MEMORY.md changed since it was last shown. These entries update that index." + update);
        }
    }

    private static string RenderFull(SideStore side)
    {
        var index = MemoryIndex.Load(side.DirectoryPath);
        var indexPath = Path.GetFullPath(Path.Combine(side.DirectoryPath, MemoryStore.IndexFileName));
        return index.IsEmpty
            ? $"### {side.Name}\nIts MEMORY.md is empty."
            : $"### {side.Name}\nContents of {indexPath}:\n\n{index.Text}";
    }

    private sealed record SideStore(string Id, string Name, string DirectoryPath);

    private static SideStore? Personal(WorldStateContext context) =>
        context.PersonalMemoryStore is { } personal
            ? new SideStore(ThreadMemoryStores.PersonalStoreId, "Personal memory", personal.MemoryDirectoryPath)
            : null;

    private static IEnumerable<SideStore> Attached(WorldStateContext context) =>
        context.AttachedMemoryStores.Select(static attached =>
            new SideStore(attached.Attachment.Id, attached.Attachment.Name, attached.DirectoryPath));

    private static IReadOnlyList<SideStore> SideStores(WorldStateContext context) =>
        Personal(context) is { } personal ? [personal, .. Attached(context)] : [.. Attached(context)];

    private static JsonObject IndexSnapshot(MemoryIndex index)
    {
        var entries = new JsonObject();
        foreach (var entry in Entries(index))
            entries[entry.Key] = Fingerprint(entry.Lines);

        var snapshot = new JsonObject { ["entries"] = entries };
        if (index.Notices.Count > 0)
            snapshot["notices"] = Fingerprint(index.Notices);
        return snapshot;
    }

    private static string? RenderEntryUpdate(MemoryIndex index, JsonObject shown, string? shownNotices)
    {
        var entries = Entries(index);
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

        var text = new StringBuilder();
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
