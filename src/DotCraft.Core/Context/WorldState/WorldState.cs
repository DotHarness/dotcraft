using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace DotCraft.Context.WorldState;

public sealed record RenderedWorldStateSection(string SectionId, string Text);

public sealed record WorldStateSectionFault(string SectionId, string Reason);

/// <summary>Carries the silent sections too, so a step that emits nothing can still be explained.</summary>
public sealed record WorldStateRender(
    IReadOnlyList<RenderedWorldStateSection> Fragments,
    IReadOnlyList<string> SilentSectionIds,
    IReadOnlyList<WorldStateSectionFault> Faults,
    WorldStateSnapshot Snapshot);

public sealed class WorldState
{
    private readonly List<IWorldStateSection> _sections = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly ILogger? _logger;

    public WorldState(ILogger? logger = null) => _logger = logger;

    public void AddSection(IWorldStateSection section)
    {
        if (!_ids.Add(section.Id))
            throw new ArgumentException($"Duplicate world-state section id: {section.Id}", nameof(section));
        _sections.Add(section);
    }

    /// <summary>Contributed sections come from outside the runtime, so a bad id must not fail the turn.</summary>
    public bool TryAddSection(IWorldStateSection section)
    {
        var id = section.Id;
        if (string.IsNullOrWhiteSpace(id) || !_ids.Add(id))
        {
            _logger?.LogWarning("Skipped a world-state section with a missing or duplicate id: {SectionId}", id);
            return false;
        }

        _sections.Add(section);
        return true;
    }

    public WorldStateRender RenderFull(WorldStateContext context) => Render(context, null, null);

    public WorldStateRender Render(
        WorldStateContext context,
        WorldStateSnapshot? previous,
        IReadOnlySet<string>? sectionIdsInHistory)
    {
        var fragments = new List<RenderedWorldStateSection>();
        var silent = new List<string>();
        var faults = new List<WorldStateSectionFault>();
        var snapshots = new List<KeyValuePair<string, JsonNode>>(_sections.Count);

        foreach (var section in _sections)
        {
            var id = section.Id;
            var snapshot = Snapshot(section, context);
            if (snapshot != null)
                snapshots.Add(new KeyValuePair<string, JsonNode>(id, snapshot));

            PreviousSectionState state;
            if (previous != null && previous.TryGetSection(id, out var value))
                state = PreviousSectionState.Known(value);
            else if (sectionIdsInHistory?.Contains(id) == true)
                state = PreviousSectionState.Unknown;
            else
                state = PreviousSectionState.Absent;

            if (state.TryGetKnown(out var known) && JsonNode.DeepEquals(known, snapshot))
            {
                silent.Add(id);
                continue;
            }

            string? text;
            try
            {
                text = section.RenderDiff(context, state)?.Trim();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "World-state section {SectionId} failed to render.", id);
                faults.Add(new WorldStateSectionFault(id, "render_failed"));
                continue;
            }

            if (string.IsNullOrEmpty(text))
                silent.Add(id);
            else
                fragments.Add(new RenderedWorldStateSection(id, text));
        }

        return new WorldStateRender(fragments, silent, faults, WorldStateSnapshot.FromSections(snapshots));
    }

    private JsonNode? Snapshot(IWorldStateSection section, WorldStateContext context)
    {
        JsonNode? raw;
        try
        {
            raw = section.Snapshot(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "World-state section {SectionId} failed to snapshot.", section.Id);
            return null;
        }

        if (raw == null)
            return null;

        // Merge-patch nulls mean removal, so a snapshot can never be null and null members are dropped.
        var normalized = WorldStateSnapshot.NormalizeSnapshot(raw);
        if (normalized == null)
            _logger?.LogWarning("World-state section {SectionId} produced a null snapshot.", section.Id);
        return normalized;
    }
}
