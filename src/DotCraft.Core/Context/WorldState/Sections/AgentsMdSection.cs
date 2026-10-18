using System.Text.Json.Nodes;

namespace DotCraft.Context.WorldState;

internal sealed class AgentsMdSection : IWorldStateSection
{
    public const string SectionId = "agents_md";
    private const string ReplacementNotice =
        "These AGENTS.md instructions replace all previously provided AGENTS.md instructions.";
    private const string RemovalNotice = "The previously provided AGENTS.md instructions no longer apply.";

    public string Id => SectionId;

    public static JsonObject CreateSnapshot(string content) => new() { ["content"] = content };

    public JsonNode Snapshot(WorldStateContext context) => CreateSnapshot(context.AgentInstructions ?? string.Empty);

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        var content = context.AgentInstructions ?? string.Empty;
        var previouslyVisible = previous.Kind == PreviousSectionKind.Unknown
            || previous.TryGetKnown(out var known) && !string.IsNullOrEmpty(known["content"]?.GetValue<string>());
        if (content.Length == 0)
            return previouslyVisible ? RemovalNotice : null;
        return previouslyVisible ? $"{ReplacementNotice}\n\n{content}" : content;
    }
}
