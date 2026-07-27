using DotCraft.Configuration;
using DotCraft.Context;

namespace DotCraft.CLI;

/// <summary>Provides optional terminal-only visualization guidance.</summary>
public sealed class TerminalVisualizationInstructionsPromptProvider : IThreadSystemPromptContextProvider
{
    private readonly AppConfig _config;

    public TerminalVisualizationInstructionsPromptProvider(AppConfig? config = null)
    {
        _config = config ?? new AppConfig();
    }

    public ContextPageKey ContextPageKey { get; } = new("runtime", "terminalVisualization", "");

    public string? GetSystemPromptSection(ThreadSystemPromptContext context)
    {
        if (!_config.GetSection<CliConfig>("CLI").TerminalVisualizationInstructions
            || !string.Equals(context.OriginChannel, "cli", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return """
        # Terminal Visualizations

        This conversation is displayed in a terminal. Use compact ASCII diagrams, trees, timelines, or tables when they make relationships easier to understand. Use tables for exact mappings and comparisons, trees for hierarchy, and timelines or diagrams for sequence and state changes. Use ASCII characters only. Do not emit inline HTML visualization directives.
        """;
    }
}
