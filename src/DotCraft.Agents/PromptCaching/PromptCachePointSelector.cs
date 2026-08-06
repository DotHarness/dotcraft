using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed record CachePointCandidate(
    int MessageIndex,
    int ContentIndex,
    ChatRole Role,
    int Sequence,
    string Hash,
    string ContentKind);

internal sealed record SelectedCachePoint(
    CachePointCandidate Candidate,
    bool Remembered,
    bool Latest);

internal sealed record PromptCacheMaintenanceSelection(
    int SnapshotMessageCount,
    bool ReadOnlyPrefix,
    bool InsertedSystemMessage);

/// <summary>Provider-neutral selection of stable prompt-cache boundaries.</summary>
internal static class PromptCachePointSelector
{
    private const int MaxCacheBreakpoints = 4;

    public static IReadOnlyList<SelectedCachePoint> Select(
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered,
        bool openAiCompatible,
        PromptCacheMaintenanceSelection? maintenance = null)
    {
        if (candidates.Count == 0)
            return [];

        var selected = maintenance is not null
            ? SelectMaintenance(candidates, remembered, maintenance)
            : openAiCompatible
                ? SelectOpenAiCompatible(candidates, remembered)
                : SelectStablePrefix(candidates, remembered);
        return selected.Values.OrderBy(static point => point.Candidate.Sequence).ToArray();
    }

    private static Dictionary<string, SelectedCachePoint> SelectMaintenance(
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered,
        PromptCacheMaintenanceSelection maintenance)
    {
        var selected = NewSelection();
        AddLatest(selected, candidates, remembered, ChatRole.System);
        AddLatestSnapshotPrefix(selected, candidates, remembered, maintenance);
        if (maintenance.ReadOnlyPrefix)
            return selected;

        var latestTail = FindLatestConversationTail(candidates);
        AddNearestRememberedBefore(selected, candidates, remembered, latestTail?.Sequence ?? int.MaxValue);
        if (latestTail is not null)
            AddSelected(selected, latestTail, remembered.Contains(latestTail.Hash), latest: true);
        return selected;
    }

    private static Dictionary<string, SelectedCachePoint> SelectOpenAiCompatible(
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered)
    {
        var selected = NewSelection();
        AddLatest(selected, candidates, remembered, ChatRole.System);
        var latestTail = FindLatestConversationTail(candidates);
        AddNearestRememberedBefore(selected, candidates, remembered, latestTail?.Sequence ?? int.MaxValue);
        if (latestTail is not null)
            AddSelected(selected, latestTail, remembered.Contains(latestTail.Hash), latest: true);
        return selected;
    }

    private static Dictionary<string, SelectedCachePoint> SelectStablePrefix(
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered)
    {
        var selected = NewSelection();
        AddLatest(selected, candidates, remembered, ChatRole.System);
        var latestTail = FindLatestConversationTail(candidates);
        if (latestTail is not null)
            AddSelected(selected, latestTail, remembered.Contains(latestTail.Hash), latest: true);

        foreach (var candidate in candidates.Where(candidate => remembered.Contains(candidate.Hash))
                     .OrderByDescending(static candidate => candidate.Sequence))
        {
            AddSelected(selected, candidate, remembered: true, latest: false);
            if (selected.Count >= MaxCacheBreakpoints)
                break;
        }

        AddLatest(selected, candidates, remembered, ChatRole.User);
        AddLatest(selected, candidates, remembered, ChatRole.Assistant);
        AddLatest(selected, candidates, remembered, ChatRole.Tool);
        return selected;
    }

    private static void AddLatestSnapshotPrefix(
        Dictionary<string, SelectedCachePoint> selected,
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered,
        PromptCacheMaintenanceSelection maintenance)
    {
        if (maintenance.SnapshotMessageCount <= 0)
            return;
        var offset = maintenance.InsertedSystemMessage ? 1 : 0;
        var endExclusive = offset + maintenance.SnapshotMessageCount;
        var latest = candidates.Where(candidate => candidate.MessageIndex >= offset && candidate.MessageIndex < endExclusive)
            .OrderByDescending(static candidate => candidate.Sequence)
            .FirstOrDefault();
        if (latest is not null)
            AddSelected(selected, latest, remembered.Contains(latest.Hash), latest: false);
    }

    private static CachePointCandidate? FindLatestConversationTail(IReadOnlyList<CachePointCandidate> candidates)
    {
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i].Role == ChatRole.User ||
                candidates[i].Role == ChatRole.Assistant ||
                candidates[i].Role == ChatRole.Tool)
                return candidates[i];
        }
        return null;
    }

    private static void AddNearestRememberedBefore(
        Dictionary<string, SelectedCachePoint> selected,
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered,
        int beforeSequence)
    {
        var candidate = candidates.Where(candidate => candidate.Sequence < beforeSequence)
            .Where(candidate => remembered.Contains(candidate.Hash))
            .OrderByDescending(static candidate => candidate.Sequence)
            .FirstOrDefault(candidate => !selected.ContainsKey(candidate.Hash));
        if (candidate is not null)
            AddSelected(selected, candidate, remembered: true, latest: false);
    }

    private static void AddLatest(
        Dictionary<string, SelectedCachePoint> selected,
        IReadOnlyList<CachePointCandidate> candidates,
        HashSet<string> remembered,
        ChatRole role)
    {
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i].Role != role)
                continue;
            AddSelected(selected, candidates[i], remembered.Contains(candidates[i].Hash), latest: true);
            return;
        }
    }

    private static void AddSelected(
        Dictionary<string, SelectedCachePoint> selected,
        CachePointCandidate candidate,
        bool remembered,
        bool latest)
    {
        if (selected.TryGetValue(candidate.Hash, out var existing))
        {
            selected[candidate.Hash] = existing with
            {
                Remembered = existing.Remembered || remembered,
                Latest = existing.Latest || latest
            };
        }
        else if (selected.Count < MaxCacheBreakpoints)
        {
            selected[candidate.Hash] = new SelectedCachePoint(candidate, remembered, latest);
        }
    }

    private static Dictionary<string, SelectedCachePoint> NewSelection() =>
        new(StringComparer.Ordinal);
}
