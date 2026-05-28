using DotCraft.Abstractions;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// An <see cref="IAgentToolProvider"/> that replaces the plain <c>McpToolProvider</c>
/// when deferred loading is enabled. It splits MCP tools into two groups:
/// <list type="bullet">
///   <item><b>Always-loaded</b> — tools listed in <c>AlwaysLoadedTools</c> config,
///     returned directly in <c>ChatOptions.Tools</c> so the model can call them immediately.</item>
///   <item><b>Deferred</b> — all remaining MCP tools, registered in a
///     <see cref="DeferredToolRegistry"/> and exposed via a <c>SearchTools</c> function
///     the model must call first.</item>
/// </list>
/// When deferred loading is disabled, or when the MCP tool count is below
/// <c>DeferThreshold</c>, all MCP tools are returned directly (same behaviour as
/// the original <c>McpToolProvider</c>).
/// </summary>
public sealed class DeferredToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 80;

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        if (context.McpClientManager == null)
            return [];

        var allMcpTools = ToolSchemaSanitizer.SanitizeTools(context.McpClientManager.Tools);
        var cfg = context.Config.Tools.DeferredLoading;

        // Fall back to full loading when deferred loading is disabled or tool count
        // is below the threshold (overhead of a discovery round not worth it).
        if (cfg.Strategy == AppConfig.DeferredLoadingStrategy.Off || allMcpTools.Count < cfg.DeferThreshold)
            return allMcpTools;

        var alwaysLoadedSet = new HashSet<string>(cfg.AlwaysLoadedTools, StringComparer.OrdinalIgnoreCase);

        var alwaysLoaded = new List<AITool>();
        var deferred = new List<AITool>();

        foreach (var tool in allMcpTools)
        {
            if (alwaysLoadedSet.Contains(tool.Name))
                alwaysLoaded.Add(tool);
            else
                deferred.Add(tool);
        }

        // Build the registry and store it in context so AgentFactory can wire it
        // into the pipeline (AdditionalTools + DynamicToolInjectionChatClient).
        var registry = new DeferredToolRegistry(deferred);
        context.DeferredToolRegistry = registry;

        // Register the search tool so the model can discover deferred tools.
        var searchTool = new ToolSearchTool(registry, cfg.MaxSearchResults);
        var tools = new List<AITool>(alwaysLoaded.Count + 1);
        tools.AddRange(alwaysLoaded);
        tools.Add(AIFunctionFactory.Create(searchTool.SearchTools));
        return tools;
    }
}
