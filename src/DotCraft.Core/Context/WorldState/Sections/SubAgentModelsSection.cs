using System.Text.Json.Nodes;
using DotCraft.Sessions;

namespace DotCraft.Context.WorldState;

internal sealed class SubAgentModelsSection : IWorldStateSection
{
    public const string SectionId = "subagent_models";

    private const string Heading = "## SubAgent models";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context) =>
        context.SubAgentModels is { } choices
            ? new JsonObject { ["hash"] = WorldStateHash.Of(SubAgentModelCatalogSnapshots.Render(choices)) }
            : null;

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous) =>
        context.SubAgentModels is { } choices
            ? $"{Heading}\n{SubAgentModelCatalogSnapshots.Render(choices)}"
            : null;
}
