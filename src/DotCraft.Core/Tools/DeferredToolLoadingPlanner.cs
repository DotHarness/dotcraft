using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.GeneratedTools.Core;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

internal enum DeferredToolLoadingMode
{
    Off,
    Simulated,
    Native
}

internal static class DeferredToolLoadingPlanner
{
    public static DeferredToolLoadingMode ResolveMode(
        AppConfig.DeferredLoadingConfig config,
        string protocol)
    {
        var normalizedProtocol = ModelProviderProtocols.Normalize(protocol);
        var supportsNative =
            normalizedProtocol == ModelProviderProtocols.OpenAIResponses
            || normalizedProtocol == ModelProviderProtocols.Anthropic;
        return config.Strategy switch
        {
            AppConfig.DeferredLoadingStrategy.Off => DeferredToolLoadingMode.Off,
            AppConfig.DeferredLoadingStrategy.Simulated => DeferredToolLoadingMode.Simulated,
            AppConfig.DeferredLoadingStrategy.Auto => supportsNative
                ? DeferredToolLoadingMode.Native
                : DeferredToolLoadingMode.Simulated,
            AppConfig.DeferredLoadingStrategy.Native when supportsNative
                => DeferredToolLoadingMode.Native,
            AppConfig.DeferredLoadingStrategy.Native => throw new InvalidOperationException(
                "Deferred tool loading strategy 'Native' requires provider protocol 'openai-responses' or 'anthropic'."),
            _ => DeferredToolLoadingMode.Off
        };
    }

    public static void Apply(List<AITool> tools, AgentRuntimeContext context)
    {
        context.DeferredToolActivationIndex = null;
        var cfg = context.Config.Tools.DeferredLoading;
        var normalizedProtocol = ModelProviderProtocols.Normalize(context.EffectiveProviderProtocol);
        var mode = ResolveMode(cfg, context.EffectiveProviderProtocol);
        if (mode == DeferredToolLoadingMode.Off || tools.Count == 0)
            return;

        var mcpToolNames = context.McpClientManager?.Tools
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var mcpServerMap = context.McpClientManager?.ToolServerMap
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var deferMcpTools = mcpToolNames.Count >= cfg.DeferThreshold;
        var alwaysLoadedMcpTools = new HashSet<string>(cfg.AlwaysLoadedTools, StringComparer.OrdinalIgnoreCase);
        var deferredNames = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<DeferredToolEntry>();

        foreach (var tool in tools)
        {
            if (ShouldDeferTool(
                    tool,
                    mcpToolNames,
                    mcpServerMap,
                    deferMcpTools,
                    alwaysLoadedMcpTools,
                    out var entry))
            {
                deferredNames.Add(tool.Name);
                entries.Add(entry);
            }
        }

        if (entries.Count == 0)
            return;

        var sanitizedEntries = ToolSchemaSanitizer.SanitizeTools(entries.Select(static entry => entry.Tool))
            .Zip(entries, static (tool, entry) => entry with { Tool = tool })
            .ToArray();
        var registry = new DeferredToolActivationIndex(sanitizedEntries, mode);
        context.DeferredToolActivationIndex = registry;

        tools.RemoveAll(tool => deferredNames.Contains(tool.Name));
        if (mode == DeferredToolLoadingMode.Native)
        {
            var toolName = normalizedProtocol == ModelProviderProtocols.Anthropic
                ? AnthropicToolSearchTool.ToolName
                : NativeToolSearchTool.ToolName;
            var traceContext = context.TraceCollector == null
                ? null
                : new DeferredToolLoadingTraceContext(
                    context.TraceCollector,
                    cfg.Strategy.ToString(),
                    mode.ToString(),
                    normalizedProtocol,
                    toolName,
                    sanitizedEntries.Length,
                    cfg.MaxSearchResults);
            tools.Add(normalizedProtocol == ModelProviderProtocols.Anthropic
                ? new AnthropicToolSearchTool(registry, cfg.MaxSearchResults, traceContext)
                : new NativeToolSearchTool(registry, cfg.MaxSearchResults, traceContext));
        }
        else
        {
            var searchTool = new ToolSearchTool(registry, cfg.MaxSearchResults);
            tools.Add(GeneratedToolFunctions.ToolSearchTool_SearchTools(searchTool));
        }
    }

    private static bool ShouldDeferTool(
        AITool tool,
        IReadOnlySet<string> mcpToolNames,
        IReadOnlyDictionary<string, string> mcpServerMap,
        bool deferMcpTools,
        HashSet<string> alwaysLoadedMcpTools,
        out DeferredToolEntry entry)
    {
        if (mcpToolNames.Contains(tool.Name))
        {
            if (deferMcpTools && !alwaysLoadedMcpTools.Contains(tool.Name))
            {
                var source = mcpServerMap.TryGetValue(tool.Name, out var serverName) ? serverName : "mcp";
                entry = new DeferredToolEntry(tool, source);
                return true;
            }

            entry = default!;
            return false;
        }

        if (DeferredToolMetadataResolver.TryGet(tool, out var metadata) && metadata.DeferLoading)
        {
            entry = new DeferredToolEntry(
                tool,
                string.IsNullOrWhiteSpace(metadata.Source) ? "runtime" : metadata.Source!,
                metadata.Namespace);
            return true;
        }

        entry = default!;
        return false;
    }
}
