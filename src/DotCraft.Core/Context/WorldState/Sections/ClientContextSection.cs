using System.Text.Json.Nodes;

namespace DotCraft.Context.WorldState;

internal sealed class ClientContextSection : IWorldStateSection
{
    public const string SectionId = "client_context";

    private const string FingerprintKey = "fingerprint";
    private const string Cleared =
        "## Client Runtime Context\n\nThe client runtime context described earlier is no longer active.";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        if (context.ClientContext is not { } compose)
            return null;
        var text = compose();
        return text.Length == 0 ? new JsonObject() : new JsonObject { [FingerprintKey] = WorldStateHash.Of(text) };
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        var text = context.ClientContext?.Invoke() ?? string.Empty;
        if (text.Length > 0)
            return text;
        var previouslyVisible = previous.Kind == PreviousSectionKind.Unknown
            || previous.TryGetKnown(out var known) && known[FingerprintKey] != null;
        return previouslyVisible ? Cleared : null;
    }

    public static string Compose(
        IReadOnlyList<IThreadSystemPromptContextProvider> providers,
        ThreadSystemPromptContext context) =>
        string.Join(
            "\n\n",
            providers
                .Where(static provider => provider.Placement == ThreadPromptPlacement.ThreadContextItem)
                .OrderBy(static provider => provider.ContextPageKey.Scope, StringComparer.Ordinal)
                .ThenBy(static provider => provider.ContextPageKey.Name, StringComparer.Ordinal)
                .Select(provider => provider.GetSystemPromptSection(context))
                .Where(static section => !string.IsNullOrWhiteSpace(section))
                .Select(static section => section!.Trim()));
}
