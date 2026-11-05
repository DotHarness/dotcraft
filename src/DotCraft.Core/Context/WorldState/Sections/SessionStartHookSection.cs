using System.Text.Json.Nodes;

namespace DotCraft.Context.WorldState;

internal sealed class SessionStartHookSection : IWorldStateSection
{
    public const string SectionId = "session_start_hook";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context) =>
        Text(context) is { } text ? CreateSnapshot(text) : null;

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous) => Text(context);

    internal static JsonObject CreateSnapshot(string renderedText) =>
        new() { ["context"] = WorldStateHash.Of(renderedText) };

    private static string? Text(WorldStateContext context) =>
        string.IsNullOrWhiteSpace(context.SessionStartHookContext)
            ? null
            : "## SessionStart Hook Context\n" + context.SessionStartHookContext.Trim();
}
