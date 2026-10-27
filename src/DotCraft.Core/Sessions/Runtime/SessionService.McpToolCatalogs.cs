using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task<AgentRuntimeContext> PrepareMcpToolContextAsync(
        SessionThread thread,
        ThreadRuntime threadRuntimeState,
        ThreadConfiguration config,
        AgentRuntimeContext toolContext,
        CancellationToken ct)
    {
        var bindingMcpServers = threadRuntimeState.GetBindingMcpServers();
        if (config.McpServers is not null || bindingMcpServers.Count > 0)
        {
            var inheritedMcpServers = config.McpServers is null && toolContext.McpClientManager is not null
                ? await toolContext.McpClientManager.ListConfigsAsync(ct)
                : [];
            var effectiveMcpServers = McpServerComposition.Compose(
                thread.Id,
                config.McpServers,
                inheritedMcpServers,
                bindingMcpServers);
            var threadMcpManager = threadRuntimeState.McpManager
                ?? new McpClientManager(dotCraftPaths, loggerFactory?.CreateLogger<McpClientManager>());
            await threadMcpManager.ConnectAsync(effectiveMcpServers, ct);
            await threadMcpManager.WaitForStartupCompletionAsync(ct);
            threadRuntimeState.McpManager = threadMcpManager;
            toolContext = CloneContextWithMcpManager(toolContext, threadMcpManager);
        }
        else
        {
            if (threadRuntimeState.McpManager is { } obsoleteThreadManager)
            {
                threadRuntimeState.McpManager = null;
                await obsoleteThreadManager.DisposeAsync();
            }
            if (toolContext.McpClientManager != null)
                await toolContext.McpClientManager.WaitForStartupCompletionAsync(ct);
        }

        if (config.McpPolicy?.Servers is { Length: > 0 } selectors && toolContext.McpClientManager is { } manager)
        {
            var unmatched = selectors
                .Where(selector => !string.IsNullOrWhiteSpace(selector)
                                   && McpServerSelector.Find(selector.Trim(), manager.ServerConfigs) is null)
                .ToArray();
            if (unmatched.Length > 0)
                Logger?.LogWarning("Thread {ThreadId} MCP policy names servers that are not configured: {Servers}",
                    thread.Id, string.Join(", ", unmatched));
        }

        return toolContext;
    }

    private void AddMcpToolSource(List<IToolSource> sources, AgentRuntimeContext context, AppConfig config, bool profileOnly)
    {
        if (profileOnly) return;
        var contributors = mcpToolCatalogContributors?.ToArray() ?? [];
        if (context.McpClientManager is not null || contributors.Length > 0)
            sources.Add(new McpToolSource(context.McpClientManager, config, contributors));
    }
}
