using System.Text.Json.Nodes;
using DotCraft.Channels;
using DotCraft.CLI;
using DotCraft.Configuration;
using DotCraft.Context.WorldState;

namespace DotCraft.AppServer;

internal sealed class ClientSurfaceSection(AppConfig? config = null) : IWorldStateSection
{
    public const string SectionId = "client_surface";

    private const string TerminalOutput =
        "The latest request came from a terminal. Replies appear as plain text, so Markdown and file links are not rendered.";

    private const string TerminalVisualizationGuidance =
        "Use compact ASCII diagrams, trees, timelines, or tables when they make relationships easier to understand. " +
        "Use tables for exact mappings and comparisons, trees for hierarchy, and timelines or diagrams for sequence and state changes. " +
        "Use ASCII characters only. Do not emit inline HTML visualization directives.";

    private const string RemoteHost = "Tools run on the workspace host, not on the user's device.";

    private const string Retired = "## Client\nThe client notes given earlier no longer apply.";

    private readonly bool _terminalVisualizations =
        config?.GetSection<CliConfig>("CLI").TerminalVisualizationInstructions == true;

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        if (context.ClientSurface is not { } surface || context.Thread.Source.SubAgent != null)
            return null;

        var values = new JsonObject();
        if (Describe(surface) is { } text)
            values["notes"] = WorldStateHash.Of(text);
        return values;
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        if (context.ClientSurface is not { } surface)
            return null;

        if (Describe(surface) is { } text)
        {
            return previous.MayBeShown
                ? $"## Client\nThis replaces the client notes given earlier.\n{text}"
                : $"## Client\n{text}";
        }

        var previouslyShown = previous.Kind switch
        {
            PreviousSectionKind.Known => previous.Value is JsonObject values && values.ContainsKey("notes"),
            PreviousSectionKind.Unknown => true,
            _ => false
        };
        return previouslyShown ? Retired : null;
    }

    private string? Describe(ClientSurface surface)
    {
        var lines = new List<string>();
        if (surface.Kind == ClientSurfaceKind.Terminal)
        {
            lines.Add(TerminalOutput);
            if (_terminalVisualizations)
                lines.Add(TerminalVisualizationGuidance);
        }
        if (surface.RemoteHost && surface.Kind != ClientSurfaceKind.Chat)
            lines.Add(RemoteHost);
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
