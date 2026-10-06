using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Workspaces;

namespace DotCraft.SessionImport;

public sealed class ImportedSetupRuntime(DotCraftPaths paths, IAppConfigMonitor monitor,
    SkillsLoader? skills = null, HookRunner? hooks = null, McpClientManager? mcp = null, LspServerManager? lsp = null,
    IPluginDotnetRuntimeCoordinator? dotnet = null)
    : ISessionRuntimeRefresher, ISessionServiceConsumer
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _revision;
    private ISessionService? _sessions;

    public void SetSessionService(ISessionService service) => _sessions = service;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var roots = new[] { paths.UserData.RootPath, paths.Data.RootPath }.OfType<string>();
            var revision = string.Join("|", roots.Select(root =>
            {
                var path = Path.Combine(root, "imports", "revision.json");
                return File.Exists(path) ? File.ReadAllText(path) : "";
            }));
            if (revision == _revision || string.IsNullOrWhiteSpace(revision.Replace("|", ""))) return;
            var current = monitor.Current;
            var workspaceConfig = Path.Combine(paths.Data.RootPath, "config.json");
            var merged = AppConfig.LoadWithGlobalFallback(workspaceConfig, current.GlobalConfigPath);
            current.Plugins = merged.Plugins;
            current.Skills = merged.Skills;
            current.Hooks = merged.Hooks;
            current.McpServers = McpScopeStore.Effective(workspaceConfig, current.GlobalConfigPath);
            PluginRuntimeConfigurator.ConfigureSkillsLoader(skills, current, paths);
            hooks?.ReplaceSnapshot(new HooksLoader(paths).Discover(current, paths.WorkspacePath));
            if (mcp != null)
            {
                var servers = PluginMcpServerResolver.LoadEffectiveServers(current, paths, out _);
                await mcp.ConnectAsync(servers, cancellationToken).ConfigureAwait(false);
            }
            if (lsp != null) await lsp.InitializeAsync(cancellationToken).ConfigureAwait(false);
            if (dotnet != null) await ConvergeDotnetPluginsAsync(dotnet, current, cancellationToken).ConfigureAwait(false);
            if (_sessions is IThreadAgentRefreshService agents) agents.InvalidateThreadAgents();
            monitor.NotifyChanged("import/completed", [ConfigChangeRegions.Skills, ConfigChangeRegions.Plugins, ConfigChangeRegions.Hooks, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
            _revision = revision;
        }
        finally { _gate.Release(); }
    }

    private async Task ConvergeDotnetPluginsAsync(IPluginDotnetRuntimeCoordinator runtime, AppConfig config, CancellationToken cancellationToken)
    {
        var installed = new PluginDiscoveryService(paths).DiscoverAll(config, paths.WorkspacePath, paths.Data.RootPath).Plugins
            .Where(plugin => plugin.Installed && plugin.Manifest.Dotnet != null)
            .ToDictionary(
                plugin => PluginIds.Canonicalize(plugin.Manifest.Id),
                plugin => new DotnetPluginState(plugin.Manifest.Version ?? "", plugin.Enabled),
                StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, _) in PlanDotnetConvergence(installed, RunningDotnetPlugins(runtime))
                     .Where(step => step.Action == DotnetConvergence.Readmit))
        {
            var quiesce = await runtime.QuiesceForMutationAsync(pluginId, cancellationToken).ConfigureAwait(false);
            if (quiesce.Outcome != PluginRuntimeMutationOutcome.NotApplied)
                await runtime.ReconcileAfterMutationAsync(pluginId, cancellationToken).ConfigureAwait(false);
        }
        foreach (var (pluginId, action) in PlanDotnetConvergence(installed, RunningDotnetPlugins(runtime))
                     .Where(step => step.Action != DotnetConvergence.Readmit))
            await runtime.SetEnabledAsync(pluginId, action == DotnetConvergence.Enable, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, DotnetPluginState> RunningDotnetPlugins(IPluginDotnetRuntimeCoordinator runtime) =>
        runtime.Snapshot.Plugins.ToDictionary(
            plugin => PluginIds.Canonicalize(plugin.PluginId),
            plugin => new DotnetPluginState(plugin.Version, plugin.Enabled),
            StringComparer.OrdinalIgnoreCase);

    internal static IEnumerable<(string PluginId, DotnetConvergence Action)> PlanDotnetConvergence(
        IReadOnlyDictionary<string, DotnetPluginState> installed,
        IReadOnlyDictionary<string, DotnetPluginState> running)
    {
        foreach (var pluginId in installed.Keys.Union(running.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!installed.TryGetValue(pluginId, out var onDisk)
                || !running.TryGetValue(pluginId, out var current)
                || onDisk.Version != current.Version)
                yield return (pluginId, DotnetConvergence.Readmit);
            else if (onDisk.Enabled != current.Enabled)
                yield return (pluginId, onDisk.Enabled ? DotnetConvergence.Enable : DotnetConvergence.Disable);
        }
    }
}

internal readonly record struct DotnetPluginState(string Version, bool Enabled);

internal enum DotnetConvergence
{
    Readmit,
    Enable,
    Disable
}
