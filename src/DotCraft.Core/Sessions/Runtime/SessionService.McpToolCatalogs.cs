using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public Action<string, McpServerStatusSnapshot>? ThreadMcpStatusChangedForBroadcast { get; set; }

    public async Task<McpClientManager?> GetEffectiveMcpRuntimeAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var thread = await GetOrLoadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread.Status != ThreadStatus.Active)
            return null;
        var runtime = _runtimeRegistry.SetThread(thread);
        await runtime.McpRefresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        McpClientManager? manager;
        using (await AcquireThreadAgentLockAsync(threadId, cancellationToken).ConfigureAwait(false))
        {
            EnsureMcpRuntimeOwner(thread, runtime);
            manager = await ReconcileMcpRuntimeAsync(
                thread, runtime, thread.Configuration ?? new ThreadConfiguration(), cancellationToken).ConfigureAwait(false);
        }
        if (manager is not null)
            await manager.WaitForStartupCompletionAsync(cancellationToken).ConfigureAwait(false);
        return manager;
    }

    public async Task SetBindingMcpServersAsync(
        string threadId,
        string bindingId,
        IReadOnlyList<McpServerConfig> servers,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentNullException.ThrowIfNull(servers);
        var normalized = servers.Select(server =>
        {
            var clone = server.Clone();
            clone.Origin = McpServerOrigin.Binding(bindingId, server.Origin.DeclaredName ?? server.Name);
            return clone;
        }).ToArray();
        await InvokeThreadCommandAsync(threadId, async commandCt =>
        {
            var thread = await GetOrLoadThreadAsync(threadId, commandCt).ConfigureAwait(false);
            var runtime = _runtimeRegistry.SetThread(thread);
            using (await AcquireThreadAgentLockAsync(threadId, commandCt).ConfigureAwait(false))
            {
                runtime.SetBindingMcpServers(bindingId, normalized);
                if (thread.Status == ThreadStatus.Active)
                    SetThreadAgent(threadId, await BuildAgentForThreadAsync(thread, commandCt).ConfigureAwait(false));
                else
                    await runtime.ReleaseMcpAsync().ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureMcpRuntimeOwner(SessionThread thread, ThreadRuntime runtime)
    {
        if (thread.Status != ThreadStatus.Active || !_runtimeRegistry.IsCurrent(thread.Id, runtime))
            throw new InvalidOperationException($"Thread '{thread.Id}' has no active runtime.");
    }

    private async Task<McpClientManager?> ReconcileMcpRuntimeAsync(
        SessionThread thread,
        ThreadRuntime threadRuntimeState,
        ThreadConfiguration config,
        CancellationToken ct)
    {
        var workspaceManager = agentFactory.RuntimeContext.McpClientManager;
        var bindingMcpServers = threadRuntimeState.GetBindingMcpServers();
        var inheritedMcpServers = !config.UseToolProfileOnly && config.McpServers is null && workspaceManager is not null
            ? await workspaceManager.ListConfigsAsync(ct)
            : [];
        var effectiveMcpServers = McpServerComposition.Compose(
            thread.Id,
            config.UseToolProfileOnly ? [] : config.McpServers,
            inheritedMcpServers,
            bindingMcpServers);
        if (effectiveMcpServers.Count > 0
            || (!config.UseToolProfileOnly && (config.McpServers is not null || workspaceManager is not null)))
        {
            if (threadRuntimeState.McpManager is null)
            {
                var threadManager = new McpClientManager(dotCraftPaths, loggerFactory?.CreateLogger<McpClientManager>(),
                    workspaceManager?.ElicitationHandler)
                {
                    WaitForRuntimeRefresh = ct => threadRuntimeState.McpRefresh.WaitAsync(ct)
                };
                threadRuntimeState.McpManager = threadManager;
                threadRuntimeState.McpStatusHandler = (sender, args) =>
                {
                    if (ReferenceEquals(sender, threadRuntimeState.McpManager))
                        ThreadMcpStatusChangedForBroadcast?.Invoke(thread.Id, args.Status);
                };
                threadManager.StatusChanged += threadRuntimeState.McpStatusHandler;
            }
            await threadRuntimeState.McpManager.ConnectAsync(effectiveMcpServers, ct);
        }
        else
        {
            await threadRuntimeState.ReleaseMcpConnectionsAsync();
        }
        return threadRuntimeState.McpManager;
    }

    private async Task<AgentRuntimeContext> PrepareMcpToolContextAsync(
        SessionThread thread,
        ThreadRuntime threadRuntimeState,
        ThreadConfiguration config,
        AgentRuntimeContext toolContext,
        CancellationToken ct)
    {
        var mcpManager = await ReconcileMcpRuntimeAsync(thread, threadRuntimeState, config, ct);
        if (mcpManager is not null)
            await mcpManager.WaitForStartupCompletionAsync(ct);
        toolContext = CloneContextWithMcpManager(toolContext, mcpManager);

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
