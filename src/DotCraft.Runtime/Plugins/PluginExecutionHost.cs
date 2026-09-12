using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;
using DotCraft.Workspaces;

namespace DotCraft.Runtime;

/// <summary>Runs explicitly admitted plugin bundles without composing an Agent or Session runtime.</summary>
public sealed class PluginExecutionHost : IAsyncDisposable
{
    private readonly ContributionRegistry _contributions = new();
    private readonly DotNetPluginRuntimeManager _runtime;

    public PluginExecutionHost(DotCraftPaths paths, IServiceProvider services)
    {
        _runtime = new DotNetPluginRuntimeManager(new PluginDiscoveryService(), new AppConfig(), paths,
            services, _contributions, executionOnly: true);
    }

    public Task PrepareAsync(IReadOnlyList<RemotePluginBundleFiles> bundles, CancellationToken cancellationToken = default) =>
        _runtime.PrepareExecutionAsync(bundles, cancellationToken);

    public static bool MatchesBundle(string rootPath, RemotePluginBundle bundle) =>
        PluginBundleFingerprint.Compute(rootPath) == bundle.ContentFingerprint
        && PluginDotnetFingerprint.Compute(rootPath) == bundle.DotnetFingerprint;

    public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        ToolPlanningContext context, CancellationToken cancellationToken = default) =>
        _runtime.ToolSource.GetRegistrationsAsync(context, cancellationToken);

    public async ValueTask ReleaseThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        await _runtime.ToolSource.ReleaseThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        _contributions.ReleaseThread(threadId);
    }

    public ValueTask DisposeAsync() => _runtime.DisposeAsync();
}
