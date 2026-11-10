using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Workspaces;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.RemoteTools;

internal sealed class HostInstalledPluginWorkspace : IAsyncDisposable
{
    private readonly PluginExecutionHost _host;
    private readonly ServiceProvider _services;
    private readonly RemoteOperationScope _operations = new();
    private readonly object _gate = new();
    private readonly Action<RemoteToolHostDiagnostic>? _diagnostic;
    private Task? _initialization;
    private Task? _disposal;

    internal HostInstalledPluginWorkspace(DotCraftPaths paths, AppConfig config,
        Action<RemoteToolHostDiagnostic>? diagnostic)
    {
        _diagnostic = diagnostic;
        _services = new ServiceCollection().AddLogging().AddSingleton(paths).AddSingleton(config)
            .AddSingleton<IApprovalService>(new HostInvocationApprovalService())
            .AddSingleton<DotCraft.Tools.BackgroundTerminals.IBackgroundTerminalService>(new ExecutionSessionTerminals())
            .BuildServiceProvider();
        _host = new PluginExecutionHost(paths, _services, config);
    }

    internal async Task InitializeAsync(string directory, CancellationToken ct)
    {
        Task initialization;
        lock (_gate) initialization = _initialization ??= InitializeCoreAsync(directory);
        await initialization.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task InitializeCoreAsync(string directory)
    {
        using var operation = _operations.Enter(default);
        await _host.LoadInstalledAsync(directory, operation.Token).ConfigureAwait(false);
        foreach (var diagnostic in _host.Diagnostics)
            _diagnostic?.Invoke(new(diagnostic.Severity switch
            {
                PluginDiagnosticSeverity.Info => RemoteToolHostDiagnosticLevel.Information,
                PluginDiagnosticSeverity.Warning => RemoteToolHostDiagnosticLevel.Warning,
                _ => RemoteToolHostDiagnosticLevel.Error
            }, diagnostic.Code, diagnostic.Message, PluginId: diagnostic.PluginId));
    }

    internal IReadOnlyList<RemoteHostPluginInfo> Plugins => _host.InstalledPlugins
        .Select(static plugin => new RemoteHostPluginInfo(plugin.Id, plugin.Version, plugin.RootPath)).ToArray();

    internal ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context, CancellationToken ct) => _host.GetRegistrationsAsync(context, ct);

    internal ValueTask ReleaseThreadAsync(string threadId, CancellationToken ct) =>
        _host.ReleaseThreadAsync(threadId, ct);

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _operations.DisposeAsync().ConfigureAwait(false);
        try { await _host.DisposeAsync().ConfigureAwait(false); }
        finally { await _services.DisposeAsync().ConfigureAwait(false); }
    }
}
