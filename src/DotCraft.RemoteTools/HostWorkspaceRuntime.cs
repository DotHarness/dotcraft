using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Workspaces;

namespace DotCraft.RemoteTools;

internal sealed class HostWorkspaceRuntime : IAsyncDisposable
{
    private const string CatalogProbeWorkspaceId = "catalog";
    private readonly ExecutionSessionTerminalService _terminals;
    private readonly LspServerManager? _lsp;
    private readonly Lazy<PluginExecutionWorkspace> _plugins;
    private readonly object _lifetimeGate = new();
    private Task? _disposal;

    private HostWorkspaceRuntime(
        string workspacePath,
        IReadOnlyList<ToolRegistration> registrations,
        ExecutionSessionTerminalService terminals,
        LspServerManager? lsp,
        Func<PluginExecutionWorkspace> plugins)
    {
        WorkspacePath = workspacePath;
        Registrations = registrations;
        _terminals = terminals;
        _lsp = lsp;
        _plugins = new(plugins);
    }

    public string WorkspacePath { get; }
    public IReadOnlyList<ToolRegistration> Registrations { get; }
    public ExecutionSessionTerminalService Terminals => _terminals;
    internal PluginExecutionWorkspace Plugins
    {
        get
        {
            lock (_lifetimeGate)
            {
                ObjectDisposedException.ThrowIf(_disposal is not null, this);
                return _plugins.Value;
            }
        }
    }
    internal IReadOnlyList<ToolRegistration> PluginRegistrations(string? threadId) =>
        _plugins.IsValueCreated ? _plugins.Value.List(threadId) : [];
    internal Task ReleasePluginThreadAsync(string threadId, CancellationToken ct) =>
        _plugins.IsValueCreated ? _plugins.Value.ReleaseThreadAsync(threadId, ct) : Task.CompletedTask;

    public Task InitializeLspAsync(CancellationToken ct) => _lsp?.InitializeAsync(ct) ?? Task.CompletedTask;

    public static AppConfig LoadWorkspaceConfig(string globalConfigPath, string workspacePath) =>
        AppConfig.LoadWithGlobalFallback(
            Path.Combine(workspacePath, ".craft", "config.json"),
            globalConfigPath);

    public static async Task<HostWorkspaceRuntime> CreateAsync(
        string workspaceId,
        string workspacePath,
        long catalogRevision,
        string globalConfigPath,
        string hostDataPath,
        string executionSessionId,
        Func<PluginExecutionWorkspace> plugins,
        CancellationToken cancellationToken)
    {
        var config = AppConfig.Load(globalConfigPath);
        var workspaceData = Path.Combine(hostDataPath, "workspaces", workspaceId, "sessions", executionSessionId);
        Directory.CreateDirectory(workspaceData);
        var terminals = new ExecutionSessionTerminalService(new BackgroundTerminalService(workspaceData, config.Tools.Shell.Background));
        LspServerManager? lsp = null;
        if (config.Tools.Lsp.Enabled)
        {
            lsp = new LspServerManager(
                config,
                DotCraftPaths.CreateForExecutionHost(workspacePath, workspaceData, hostDataPath));

        }
        var registrations = await CreateSource(config, terminals, lsp, Path.Combine(hostDataPath, "artifacts", executionSessionId))
            .GetRegistrationsAsync(
                CreatePlanningContext(workspaceId, workspacePath, workspaceData, catalogRevision),
                cancellationToken)
            .ConfigureAwait(false);
        return new HostWorkspaceRuntime(
            workspacePath,
            [.. registrations.Where(item => RemoteToolMetadata.IsRpcEligible(item.Definition))],
            terminals,
            lsp,
            plugins);
    }

    /// <summary>
    /// Describes the tools this Host exports under its own configuration, reading declarations
    /// only: no workspace is leased, no language server starts, and no Host state is written.
    /// </summary>
    public static async Task<IReadOnlyList<ToolDefinition>> DescribeExportedToolsAsync(
        string globalConfigPath,
        string hostDataPath,
        CancellationToken cancellationToken)
    {
        var config = AppConfig.Load(globalConfigPath);
        var lsp = config.Tools.Lsp.Enabled
            ? new LspServerManager(
                config,
                DotCraftPaths.CreateForExecutionHost(hostDataPath, hostDataPath, hostDataPath))
            : null;
        try
        {
            var registrations = await CreateSource(config, new DeclarationOnlyTerminals(), lsp, hostDataPath)
                .GetRegistrationsAsync(
                    CreatePlanningContext(
                        CatalogProbeWorkspaceId,
                        hostDataPath,
                        hostDataPath,
                        catalogRevision: 0),
                    cancellationToken)
                .ConfigureAwait(false);
            return
            [
                .. registrations
                    .Select(item => item.Definition)
                    .Where(RemoteToolMetadata.IsRpcEligible)
            ];
        }
        finally
        {
            if (lsp is not null)
                await lsp.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _terminals.DisposeAsync().ConfigureAwait(false);
        if (_lsp is not null)
            await _lsp.DisposeAsync().ConfigureAwait(false);
    }

    private static WorkspaceExecutionToolSource CreateSource(
        AppConfig config,
        IBackgroundTerminalService terminals,
        LspServerManager? lsp,
        string hostDataPath) => new(
            config,
            terminals,
            pathBlacklist: new PathBlacklist(config.Security.BlacklistedPaths),
            lspServerManager: lsp,
            userDataPath: hostDataPath,
            approvalService: new HostInvocationApprovalService(),
            notifyLspOnFileChanges: false,
            managedFileSearch: true);

    private sealed class DeclarationOnlyTerminals : IBackgroundTerminalService
    {
        public event Action<BackgroundTerminalEvent>? TerminalEvent { add { } remove { } }

        public Task<BackgroundTerminalSnapshot> StartAsync(
            BackgroundTerminalStartRequest request,
            CancellationToken ct = default) => throw Unsupported();

        public Task<BackgroundTerminalSnapshot> ReadAsync(
            string sessionId,
            int waitMs = 0,
            int? maxOutputChars = null,
            CancellationToken ct = default) => throw Unsupported();

        public Task<BackgroundTerminalSnapshot> WriteStdinAsync(
            string sessionId,
            string input,
            int yieldTimeMs = 1000,
            int? maxOutputChars = null,
            CancellationToken ct = default) => throw Unsupported();

        public Task<IReadOnlyList<BackgroundTerminalSnapshot>> ListAsync(
            string? threadId = null,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BackgroundTerminalSnapshot>>([]);

        public Task<BackgroundTerminalSnapshot> StopAsync(
            string sessionId,
            CancellationToken ct = default) => throw Unsupported();

        public Task<IReadOnlyList<BackgroundTerminalSnapshot>> CleanThreadAsync(
            string threadId,
            CancellationToken ct = default) => throw Unsupported();

        public Task<IReadOnlyList<string>> DeleteThreadArtifactsAsync(
            string threadId,
            CancellationToken ct = default) => throw Unsupported();

        public Task<int> CleanupExpiredArtifactsAsync(CancellationToken ct = default) => throw Unsupported();

        private static NotSupportedException Unsupported() =>
            new("The Remote Tool Host catalog probe never executes tools.");
    }

    private static ToolPlanningContext CreatePlanningContext(
        string workspaceId,
        string workspacePath,
        string workspaceData,
        long catalogRevision) => new(
            $"remote-host:{workspaceId}",
            turnId: null,
            workspacePath,
            workspaceData,
            mode: "remote-tool-host",
            profile: null,
            providerCapabilities: [],
            revision: catalogRevision,
            workspaceRoots: [workspacePath],
            requireApprovalOutsideWorkspace: true);
}
