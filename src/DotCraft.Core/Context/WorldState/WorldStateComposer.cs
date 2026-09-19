using Microsoft.Extensions.Logging;

namespace DotCraft.Context.WorldState;

/// <summary>Registration order is the order sections reach the model.</summary>
public static class WorldStateComposer
{
    private const string ItemKindPrefix = "world_state.";

    public static string ItemKind(string sectionId) => ItemKindPrefix + sectionId;

    public static string? SectionIdFromItemKind(string? kind) =>
        kind != null && kind.StartsWith(ItemKindPrefix, StringComparison.Ordinal)
            ? kind[ItemKindPrefix.Length..]
            : null;

    public static WorldState Build(
        IEnumerable<IWorldStateSection>? contributedSections = null,
        ILogger? logger = null)
    {
        var world = new WorldState(logger);
        world.AddSection(new EnvironmentSection());
        world.AddSection(new ModeSection());
        world.AddSection(new SessionStartHookSection());
        world.AddSection(new ThreadGoalSection());

        foreach (var section in contributedSections ?? [])
            world.TryAddSection(section);

        return world;
    }
}
