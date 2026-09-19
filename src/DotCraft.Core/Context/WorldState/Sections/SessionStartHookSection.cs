using System.Text.Json.Nodes;

namespace DotCraft.Context.WorldState;

internal sealed class SessionStartHookSection : IWorldStateSection
{
    public const string SectionId = "session_start_hook";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context) =>
        Text(context) is { } text ? new JsonObject { ["context"] = WorldStateHash.Of(text) } : null;

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous) =>
        Text(context) is { } text ? "## SessionStart Hook Context\n" + text : null;

    private static string? Text(WorldStateContext context) =>
        string.IsNullOrWhiteSpace(context.SessionStartHookContext)
            ? null
            : context.SessionStartHookContext.Trim();
}
