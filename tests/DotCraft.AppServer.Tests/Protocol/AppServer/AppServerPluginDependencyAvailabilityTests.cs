using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerPluginDependencyAvailabilityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        $"plugin_dependency_availability_{Guid.NewGuid():N}");

    private string WorkspaceCraftPath => Path.Combine(_tempRoot, ".craft");

    [Fact]
    public async Task PluginList_UsesRuntimeDependencyAvailabilityWhenRuntimeStateExists()
    {
        WriteDotNetExtension("wire.provider", "1.2.0");
        WriteDotNetExtension(
            "wire.consumer",
            "1.0.0",
            dependencies: new Dictionary<string, string> { ["wire.provider"] = "1.0.0" });
        var runtime = new SnapshotPluginRuntimeCoordinator(new PluginRuntimeSnapshot(
            4,
            [
                new DotNetExtensionRuntimeInfo(
                    "wire.consumer",
                    "1.0.0",
                    DotNetExtensionRuntimeState.Active,
                    "wire.consumer-g1",
                    [],
                    DependencyObservations:
                    [
                        new PluginDependencyObservation(
                            "wire.provider",
                            "1.0.0",
                            "1.2.0",
                            PluginDependencyAvailability.Active)
                    ]),
                new DotNetExtensionRuntimeInfo(
                    "wire.provider",
                    "1.2.0",
                    DotNetExtensionRuntimeState.Active,
                    "wire.provider-g1",
                    [],
                    DependencyObservations: [])
            ],
            []));
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: WorkspaceCraftPath,
            appConfigMonitor: new AppConfigMonitor(new AppConfig()),
            dotNetExtensionRuntimeCoordinator: runtime);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var consumer = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            plugin => plugin.GetProperty("id").GetString() == "wire.consumer");
        var dependency = Assert.Single(consumer.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("wire.provider", dependency.GetProperty("id").GetString());
        Assert.Equal("1.2.0", dependency.GetProperty("observedVersion").GetString());
        Assert.Equal("active", dependency.GetProperty("availability").GetString());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private void WriteDotNetExtension(
        string pluginId,
        string version,
        IReadOnlyDictionary<string, string>? dependencies = null)
    {
        var pluginRoot = Path.Combine(WorkspaceCraftPath, "plugins", pluginId);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        var dependencyJson = dependencies is { Count: > 0 }
            ? string.Join(",", dependencies.Select(static dependency =>
                $"\"{dependency.Key}\":\"{dependency.Value}\""))
            : string.Empty;
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{pluginId}}",
              "version": "{{version}}",
              "displayName": "{{pluginId}}",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "0.1.0",
                "entryAssembly": "./dotnet/Plugin.dll",
                "entryType": "Test.Plugin",
                "exportedApiAssemblies": []
              },
              "dependencies": { {{dependencyJson}} }
            }
            """);
    }

    private sealed class SnapshotPluginRuntimeCoordinator(PluginRuntimeSnapshot snapshot)
        : IDotNetExtensionRuntimeCoordinator
    {
        public IReadOnlySet<string> HostSharedAssemblies { get; } = new HashSet<string>();

        public Task<DotNetExtensionBuildApplyResult> ApplyBuildAsync(
            string pluginId,
            DotNetExtensionBuild build,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public PluginRuntimeSnapshot Snapshot { get; } = snapshot;

        public event EventHandler<PluginRuntimeSnapshotChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task SetEnabledAsync(
            string pluginId,
            bool enabled,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PluginRuntimeMutationResult> QuiesceForMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            MutationResult();

        public Task<PluginRuntimeMutationResult> ReconcileAfterMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            MutationResult();

        public Task<PluginRuntimeMutationResult> TrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            MutationResult();

        public Task<PluginRuntimeMutationResult> RevokeTrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            MutationResult();

        private static Task<PluginRuntimeMutationResult> MutationResult() =>
            Task.FromResult(new PluginRuntimeMutationResult(
                PluginRuntimeMutationOutcome.NoChange,
                [],
                []));
    }
}
