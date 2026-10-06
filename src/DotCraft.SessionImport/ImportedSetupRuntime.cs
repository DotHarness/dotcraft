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
    SkillsLoader? skills = null, HookRunner? hooks = null, McpClientManager? mcp = null, LspServerManager? lsp = null)
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
            if (_sessions is IThreadAgentRefreshService agents) agents.InvalidateThreadAgents();
            monitor.NotifyChanged("import/completed", [ConfigChangeRegions.Skills, ConfigChangeRegions.Plugins, ConfigChangeRegions.Hooks, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp]);
            _revision = revision;
        }
        finally { _gate.Release(); }
    }
}
