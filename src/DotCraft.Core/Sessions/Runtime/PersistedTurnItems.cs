using System.Text.Json;

namespace DotCraft.Sessions;

/// <summary>What a running Turn has persisted of each Item, so later writes append only what changed.</summary>
internal sealed class PersistedTurnItems
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _persisted = new(StringComparer.Ordinal);
    private bool _known;

    public SemaphoreSlim WriteGate { get; } = new(1, 1);

    public static IReadOnlyList<PersistedItem> Capture(IEnumerable<SessionItem> items) =>
        items.Select(static item => new PersistedItem(item, JsonSerializer.Serialize(item, SessionJsonOptions.Default))).ToList();

    /// <summary>Records items as persisted by a write that covered the whole Turn.</summary>
    public void Reset(IReadOnlyList<PersistedItem> items)
    {
        lock (_lock)
        {
            _persisted.Clear();
            foreach (var item in items)
                _persisted[item.Item.Id] = item.Json;
            _known = true;
        }
    }

    /// <summary>Returns the items that differ from what was persisted, or null when nothing is known yet.</summary>
    public IReadOnlyList<PersistedItem>? FindChanged(IReadOnlyList<PersistedItem> items)
    {
        lock (_lock)
        {
            if (!_known)
                return null;
            return items
                .Where(item => !_persisted.TryGetValue(item.Item.Id, out var json) || json != item.Json)
                .ToList();
        }
    }

    public void MarkPersisted(IReadOnlyList<PersistedItem> items)
    {
        lock (_lock)
        {
            foreach (var item in items)
                _persisted[item.Item.Id] = item.Json;
        }
    }

    internal sealed record PersistedItem(SessionItem Item, string Json);
}
