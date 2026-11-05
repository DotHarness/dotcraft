using System.Text.Json.Nodes;
using DotCraft.Sessions;

namespace DotCraft.Context.WorldState;

internal sealed class ThreadInstructionsSection(
    string id,
    string heading,
    string noun,
    Func<ThreadConfiguration?, string?> read) : IWorldStateSection
{
    public const string RoleSectionId = "role_instructions";
    public const string DeveloperSectionId = "developer_instructions";

    private const string FingerprintKey = "fingerprint";

    public static ThreadInstructionsSection Role() =>
        new(RoleSectionId, "Role Instructions", "role", static config => config?.RoleInstructions);

    public static ThreadInstructionsSection Developer() =>
        new(DeveloperSectionId, "Developer Instructions", "developer", static config => config?.DeveloperInstructions);

    public string Id => id;

    public JsonNode Snapshot(WorldStateContext context) =>
        Text(context) is { } text ? new JsonObject { [FingerprintKey] = WorldStateHash.Of(text) } : new JsonObject();

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        var text = Text(context);
        var previouslyVisible = previous.Kind == PreviousSectionKind.Unknown
            || previous.TryGetKnown(out var known) && known[FingerprintKey] != null;
        if (text == null)
            return previouslyVisible ? $"The previously provided {noun} instructions no longer apply." : null;
        var section = $"## {heading}\n\n{text}";
        return previouslyVisible
            ? $"These {noun} instructions replace all previously provided {noun} instructions.\n\n{section}"
            : section;
    }

    private string? Text(WorldStateContext context) =>
        read(context.Thread.Configuration)?.Trim() is { Length: > 0 } text ? text : null;
}
