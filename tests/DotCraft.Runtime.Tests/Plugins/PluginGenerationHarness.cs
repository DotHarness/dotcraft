using DotCraft.Configuration;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Drives one generation through the real accept, shadow-copy, and activation path, without
/// the runtime manager: only a registry, a call-gate lookup, and a service provider.</summary>
internal sealed class PluginGenerationHarness : IDisposable
{
    private readonly PluginBundleSnapshotStore _store;

    public PluginGenerationHarness()
    {
        Root = Path.Combine(Path.GetTempPath(), $"dotcraft_generation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        _store = new PluginBundleSnapshotStore(Path.Combine(Root, "runtime", "test-process"));
    }

    public string Root { get; }

    public ContributionRegistry Registry { get; } = new();

    public PluginCallGateRegistry CallGates { get; } = new();

    public AppConfig Config { get; } = new();

    public IServiceProvider Services { get; set; } = DotnetPluginTestBundle.EmptyServices;

    public string PluginRoot(string pluginId) => Path.Combine(Root, "plugins", pluginId);

    public string DataRoot(string pluginId) => Path.Combine(Root, "data", pluginId);

    public string WorkspaceRoot => Path.Combine(Root, "workspace");

    public Task<PluginActivationAttempt> ActivateAsync(
        string pluginId,
        CancellationToken cancellationToken = default) =>
        ActivateAsync(pluginId, Guid.NewGuid().ToString("N"), cancellationToken);

    public async Task<PluginActivationAttempt> ActivateAsync(
        string pluginId,
        string generationId,
        CancellationToken cancellationToken)
    {
        var parsed = PluginManifestParser.Load(PluginRoot(pluginId));
        if (parsed.Manifest?.Dotnet == null)
        {
            throw new InvalidOperationException(
                "The test bundle does not carry an admitted .NET manifest: "
                + string.Join(" | ", parsed.Diagnostics.Select(static d => $"{d.Code}: {d.Message}")));
        }

        var discovered = new DiscoveredPlugin(
            parsed.Manifest,
            PluginDiscoverySourceKind.Workspace,
            PluginRoot(pluginId),
            Enabled: true);
        var snapshot = _store.Accept(discovered);
        var shadowRoot = _store.CreateGenerationCopy(snapshot, generationId);
        return await PluginGeneration.CreateAsync(
            snapshot,
            generationId,
            shadowRoot,
            DataRoot(pluginId),
            WorkspaceRoot,
            new PluginGenerationHost(Services, Registry, CallGates, Config),
            new Dictionary<string, PluginGeneration>(StringComparer.Ordinal),
            new PluginActivationCommitGate(),
            static (_, _) => null,
            cancellationToken);
    }

    public string DataFile(string pluginId, string fileName) =>
        Path.Combine(DataRoot(pluginId), fileName);

    public static async Task WaitForLineAsync(string path, string expected)
    {
        for (var attempt = 0; attempt < 3000; attempt++)
        {
            try
            {
                if (PluginLogFile.ReadLines(path).Contains(expected, StringComparer.Ordinal))
                    return;
            }
            catch (IOException)
            {
                // The plugin may be appending the line concurrently.
            }
            await Task.Delay(10);
        }

        throw new TimeoutException(
            $"Timed out waiting for '{expected}' in '{path}'. "
            + $"Observed: {string.Join(" | ", PluginLogFile.ReadLines(path))}");
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // A deliberately leaked generation keeps its shadow copy mapped.
        }
    }
}
