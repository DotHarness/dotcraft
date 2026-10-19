using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Skills;

namespace DotCraft.Context.WorldState;

/// <summary>
/// One section of the state the model can see. <see cref="Id"/> is persisted in rollouts and must
/// stay stable; a snapshot must not carry values that move on their own, such as elapsed time or
/// token counts. <see cref="RenderDiff"/> runs only when the snapshot differs from what was shown.
/// </summary>
public interface IWorldStateSection : IContributionContract
{
    string Id { get; }

    /// <summary>
    /// Comparison state for this turn, or <c>null</c> when its facts are unavailable, which skips the section
    /// and keeps the baseline the model was last shown. It must not serialize to JSON null.
    /// </summary>
    JsonNode? Snapshot(WorldStateContext context);

    string? RenderDiff(WorldStateContext context, PreviousSectionState previous);
}

public sealed record WorldStateContext
{
    public required SessionThread Thread { get; init; }

    public AgentModeManager? ModeManager { get; init; }

    public string? WorkspacePath { get; init; }

    public string? AgentInstructions { get; init; }

    public bool HasActivePlan { get; init; }

    public ThreadGoal? ThreadGoal { get; init; }

    public string? SessionStartHookContext { get; init; }

    /// <summary>The Thread's memory store, or <c>null</c> when the Thread does not use memory.</summary>
    public MemoryStore? MemoryStore { get; init; }

    /// <summary>The stores attached beside <see cref="MemoryStore"/>; empty when the Thread does not use memory.</summary>
    public IReadOnlyList<AttachedMemoryStore> AttachedMemoryStores { get; init; } = [];

    /// <summary>Reads the skills the Thread may use, or <c>null</c> when it composes no skill context.</summary>
    public Func<IReadOnlyList<SkillCatalogEntry>>? Skills { get; init; }
}

public enum PreviousSectionKind
{
    /// <summary>No baseline entry, and retained history holds no item for this section.</summary>
    Absent,

    /// <summary>Retained history holds an item for this section, but its snapshot is unavailable.</summary>
    Unknown,

    /// <summary>The exact previous snapshot is available.</summary>
    Known
}

public readonly record struct PreviousSectionState(PreviousSectionKind Kind, JsonNode? Value)
{
    public static PreviousSectionState Absent { get; } = new(PreviousSectionKind.Absent, null);

    public static PreviousSectionState Unknown { get; } = new(PreviousSectionKind.Unknown, null);

    public static PreviousSectionState Known(JsonNode value) => new(PreviousSectionKind.Known, value);

    /// <summary>Separates a first-time render from a replacement or a retraction.</summary>
    public bool MayBeShown => Kind != PreviousSectionKind.Absent;

    public bool TryGetKnown(out JsonNode value)
    {
        value = Value!;
        return Kind == PreviousSectionKind.Known && Value != null;
    }
}
