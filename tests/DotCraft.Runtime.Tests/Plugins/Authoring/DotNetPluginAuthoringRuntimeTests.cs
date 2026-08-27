using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Runtime;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginAuthoringRuntimeTests :
    IClassFixture<AuthoringReferencePackFixture>,
    IDisposable
{
    private const string PluginId = "acme.live-authoring";

    private readonly PluginRuntimeHarness _harness = new();
    private readonly DotNetPluginCompiler _compiler;

    public DotNetPluginAuthoringRuntimeTests(AuthoringReferencePackFixture fixture)
    {
        _compiler = new DotNetPluginCompiler(fixture.Load());
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Apply_FirstBuildNoOpAndChangedBuild_ReuseTheRuntimeLifecycle()
    {
        var globalConfigPath = Path.Combine(_harness.Root, "global", "config.json");
        _harness.Config.GlobalConfigPath = globalConfigPath;
        WriteProject(PluginId, PromptPlugin("first"));
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);

        using var firstPreparation = Prepare(PluginId);
        var first = await manager.ApplyAuthoringBuildAsync(PluginId, firstPreparation);

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, first.Outcome);
        var firstRuntime = Assert.IsType<PluginDotnetRuntimeInfo>(first.Runtime);
        AssertState(firstRuntime, PluginDotnetRuntimeState.Active);
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, firstRuntime.TrustStatus);
        Assert.False(File.Exists(PluginDotnetTrustConfigStore.PathForConfig(globalConfigPath)));
        Assert.Equal("first", ReadPrompt());
        Assert.Equal(
            firstPreparation.Fingerprint,
            PluginDotnetFingerprint.Compute(ProjectPluginRoot(PluginId)));
        var firstGeneration = firstRuntime.GenerationId;
        var runtimeRevision = manager.Snapshot.Revision;
        var contributionRevision = _harness.Registry.GetRevision<ISystemPromptSection>();

        using var repeatedPreparation = Prepare(PluginId);
        var repeated = await manager.ApplyAuthoringBuildAsync(PluginId, repeatedPreparation);

        Assert.Equal(PluginRuntimeMutationOutcome.NoChange, repeated.Outcome);
        Assert.Equal(runtimeRevision, manager.Snapshot.Revision);
        Assert.Equal(contributionRevision, _harness.Registry.GetRevision<ISystemPromptSection>());
        Assert.Equal(firstGeneration, repeated.Runtime?.GenerationId);
        Assert.Equal("first", ReadPrompt());

        File.WriteAllText(ProjectSourcePath(PluginId), PromptPlugin("second"));
        using var changedPreparation = Prepare(PluginId);
        var changed = await manager.ApplyAuthoringBuildAsync(PluginId, changedPreparation);

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, changed.Outcome);
        var changedRuntime = Assert.IsType<PluginDotnetRuntimeInfo>(changed.Runtime);
        AssertState(changedRuntime, PluginDotnetRuntimeState.Active);
        Assert.NotEqual(firstPreparation.Fingerprint, changedPreparation.Fingerprint);
        Assert.NotEqual(firstGeneration, changedRuntime.GenerationId);
        Assert.Equal("second", ReadPrompt());
        Assert.Single(_harness.Registry.ResolveEntries<ISystemPromptSection>());
    }

    [Fact]
    public async Task FailedPreparation_LeavesPublishedBytesAndActiveGenerationUntouched()
    {
        WriteProject(PluginId, PromptPlugin("stable"));
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        using var valid = Prepare(PluginId);
        var active = await manager.ApplyAuthoringBuildAsync(PluginId, valid);
        var generation = active.Runtime?.GenerationId;
        var fingerprint = PluginBundleFingerprint.Compute(ProjectPluginRoot(PluginId));
        var runtimeRevision = manager.Snapshot.Revision;

        File.WriteAllText(ProjectSourcePath(PluginId), "this is not valid C#");
        using var failed = _compiler.Prepare(DataRoot, PluginId);

        Assert.False(failed.Succeeded);
        Assert.Equal(fingerprint, PluginBundleFingerprint.Compute(ProjectPluginRoot(PluginId)));
        Assert.Equal(generation, Plugin(manager, PluginId).GenerationId);
        Assert.Equal(runtimeRevision, manager.Snapshot.Revision);
        Assert.Equal("stable", ReadPrompt());
    }

    [Fact]
    public async Task ActivationFailure_PublishesTheNewBundleWithoutRestoringTheOldGeneration()
    {
        WriteProject(PluginId, PromptPlugin("old"));
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        using var firstPreparation = Prepare(PluginId);
        var first = await manager.ApplyAuthoringBuildAsync(PluginId, firstPreparation);
        var oldGeneration = first.Runtime?.GenerationId;

        File.WriteAllText(ProjectSourcePath(PluginId), FaultingPlugin);
        using var faultingPreparation = Prepare(PluginId);
        var faulting = await manager.ApplyAuthoringBuildAsync(PluginId, faultingPreparation);

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, faulting.Outcome);
        var runtime = Assert.IsType<PluginDotnetRuntimeInfo>(faulting.Runtime);
        AssertState(runtime, PluginDotnetRuntimeState.Faulted);
        Assert.NotEqual(oldGeneration, runtime.GenerationId);
        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());
        Assert.Equal(
            faultingPreparation.Fingerprint,
            PluginDotnetFingerprint.Compute(ProjectPluginRoot(PluginId)));
        Assert.NotEqual(firstPreparation.Fingerprint, faultingPreparation.Fingerprint);

        var revision = manager.Snapshot.Revision;
        using var repeatedPreparation = Prepare(PluginId);
        var repeated = await manager.ApplyAuthoringBuildAsync(PluginId, repeatedPreparation);

        Assert.Equal(PluginRuntimeMutationOutcome.NoChange, repeated.Outcome);
        Assert.Equal(runtime.GenerationId, repeated.Runtime?.GenerationId);
        Assert.Equal(PluginDotnetRuntimeState.Faulted, repeated.Runtime?.State);
        Assert.Equal(revision, manager.Snapshot.Revision);
    }

    [Fact]
    public async Task ProviderReplacement_ReactivatesItsDependentClosure()
    {
        const string providerId = "acme.authoring-provider";
        const string consumerId = "acme.authoring-consumer";
        WriteProject(providerId, NoopPlugin);
        WriteProject(
            consumerId,
            PromptPlugin("consumer"),
            new Dictionary<string, string> { [providerId] = "1.0.0" });
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);

        using var providerPreparation = Prepare(providerId);
        await manager.ApplyAuthoringBuildAsync(providerId, providerPreparation);
        using var consumerPreparation = Prepare(consumerId);
        await manager.ApplyAuthoringBuildAsync(consumerId, consumerPreparation);
        var providerGeneration = Plugin(manager, providerId).GenerationId;
        var consumerGeneration = Plugin(manager, consumerId).GenerationId;

        var changedSource = NoopPlugin.Replace(
            "=> ValueTask.CompletedTask;",
            "=> new(Task.CompletedTask);",
            StringComparison.Ordinal);
        File.WriteAllText(ProjectSourcePath(providerId), changedSource);
        using var changedProvider = Prepare(providerId);
        var replacement = await manager.ApplyAuthoringBuildAsync(providerId, changedProvider);

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, replacement.Outcome);
        AssertState(Plugin(manager, providerId), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, consumerId), PluginDotnetRuntimeState.Active);
        Assert.NotEqual(providerGeneration, Plugin(manager, providerId).GenerationId);
        Assert.NotEqual(consumerGeneration, Plugin(manager, consumerId).GenerationId);
        Assert.Equal("consumer", ReadPrompt());
    }

    [Fact]
    public async Task Restart_DoesNotDiscoverOrAuthorizeADevelopmentBundleUntilItIsBuiltAgain()
    {
        WriteProject(PluginId, PromptPlugin("restart"));
        await using (var firstManager = _harness.CreateManager(trustInstalled: false))
        {
            await firstManager.StartAsync(CancellationToken.None);
            using var firstPreparation = Prepare(PluginId);
            var first = await firstManager.ApplyAuthoringBuildAsync(PluginId, firstPreparation);
            AssertState(Assert.IsType<PluginDotnetRuntimeInfo>(first.Runtime), PluginDotnetRuntimeState.Active);
        }

        await using var restartedManager = _harness.CreateManager(trustInstalled: false);
        await restartedManager.StartAsync(CancellationToken.None);
        Assert.DoesNotContain(restartedManager.Snapshot.Plugins, plugin => plugin.PluginId == PluginId);
        Assert.Empty(_harness.Registry.Resolve<ISystemPromptSection>());

        using var rebuiltPreparation = Prepare(PluginId);
        var rebuilt = await restartedManager.ApplyAuthoringBuildAsync(PluginId, rebuiltPreparation);

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, rebuilt.Outcome);
        AssertState(Assert.IsType<PluginDotnetRuntimeInfo>(rebuilt.Runtime), PluginDotnetRuntimeState.Active);
        Assert.Equal("restart", ReadPrompt());
    }

    [Fact]
    public async Task DurableTrustChanges_DoNotReplaceTheExactProcessLocalQualification()
    {
        WriteProject(PluginId, PromptPlugin("qualified"));
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        using var preparation = Prepare(PluginId);
        var built = await manager.ApplyAuthoringBuildAsync(PluginId, preparation);
        var generation = built.Runtime?.GenerationId;

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, (await manager.TrustAsync(PluginId)).Outcome);
        Assert.Equal(PluginDotnetTrustStatus.Trusted, Plugin(manager, PluginId).TrustStatus);
        Assert.Equal(PluginRuntimeMutationOutcome.Applied, (await manager.RevokeTrustAsync(PluginId)).Outcome);

        var runtime = Plugin(manager, PluginId);
        AssertState(runtime, PluginDotnetRuntimeState.Active);
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, runtime.TrustStatus);
        Assert.Equal(generation, runtime.GenerationId);
        Assert.Equal("qualified", ReadPrompt());
    }

    [Fact]
    public async Task Apply_PublishesAnIdenticalBuildFromAnotherWorkspaceRoot()
    {
        WriteProject(PluginId, WorkspacePromptPlugin("same"));
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        using var basePreparation = Prepare(PluginId);
        var active = await manager.ApplyAuthoringBuildAsync(PluginId, basePreparation);
        var generation = active.Runtime?.GenerationId;
        var baseFingerprint = PluginDotnetFingerprint.Compute(ProjectPluginRoot(PluginId));
        var overrideDataRoot = Path.Combine(_harness.Root, "override", ".craft");
        WriteProjectAt(overrideDataRoot, PluginId, WorkspacePromptPlugin("same"));
        using var overridePreparation = _compiler.Prepare(overrideDataRoot, PluginId);
        Assert.True(overridePreparation.Succeeded);
        Assert.Equal(baseFingerprint, overridePreparation.Fingerprint);

        var replacement = await manager.ApplyAuthoringBuildAsync(PluginId, overridePreparation);

        Assert.Equal(baseFingerprint, PluginDotnetFingerprint.Compute(ProjectPluginRoot(PluginId)));
        Assert.Equal(PluginRuntimeMutationOutcome.Applied, replacement.Outcome);
        Assert.NotEqual(generation, Plugin(manager, PluginId).GenerationId);
        Assert.Equal(
            overridePreparation.Fingerprint,
            PluginDotnetFingerprint.Compute(Path.Combine(
                overrideDataRoot,
                "plugin-projects",
                PluginId,
                "plugin")));
        Assert.Equal(
            "same|" + Path.GetDirectoryName(overrideDataRoot),
            ReadPrompt());
    }

    private DotNetPluginBuildPreparation Prepare(string pluginId)
    {
        var preparation = _compiler.Prepare(DataRoot, pluginId);
        Assert.True(
            preparation.Succeeded,
            string.Join(Environment.NewLine, preparation.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return preparation;
    }

    private void WriteProject(
        string pluginId,
        string source,
        IReadOnlyDictionary<string, string>? dependencies = null) =>
        WriteProjectAt(DataRoot, pluginId, source, dependencies);

    private static void WriteProjectAt(
        string dataRoot,
        string pluginId,
        string source,
        IReadOnlyDictionary<string, string>? dependencies = null)
    {
        var projectRoot = Path.Combine(dataRoot, "plugin-projects", pluginId);
        var sourceRoot = Path.Combine(projectRoot, "src");
        var pluginRoot = Path.Combine(projectRoot, "plugin");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "lib"));
        File.WriteAllText(Path.Combine(sourceRoot, "Plugin.cs"), source);

        var dependenciesJson = dependencies is { Count: > 0 }
            ? ",\n  \"dependencies\": { " + string.Join(", ", dependencies
                .OrderBy(static dependency => dependency.Key, StringComparer.Ordinal)
                .Select(static dependency => $"\"{dependency.Key}\": \"{dependency.Value}\"")) + " }"
            : string.Empty;
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
            {
              "schemaVersion": 1,
              "id": "{{pluginId}}",
              "version": "1.0.0",
              "displayName": "Authoring runtime test",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "0.0.0",
                "entryAssembly": "./lib/Acme.Plugin.dll",
                "entryType": "Acme.Plugin"
              }{{dependenciesJson}}
            }
            """);
    }

    private string ReadPrompt() =>
        Assert.Single(_harness.Registry.Resolve<ISystemPromptSection>())
            .GetContent(new SystemPromptSectionContext(null, _harness.Workspace, DataRoot))!;

    private string DataRoot => Path.Combine(_harness.Workspace, ".craft");

    private string ProjectRoot(string pluginId) => Path.Combine(DataRoot, "plugin-projects", pluginId);

    private string ProjectPluginRoot(string pluginId) => Path.Combine(ProjectRoot(pluginId), "plugin");

    private string ProjectSourcePath(string pluginId) => Path.Combine(ProjectRoot(pluginId), "src", "Plugin.cs");

    private static string PromptPlugin(string content) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Contributions;
        using DotCraft.Plugins;

        namespace Acme;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                context.Contributions.Add<ISystemPromptSection>(new Section());
                return ValueTask.CompletedTask;
            }

            private sealed class Section : ISystemPromptSection
            {
                public string Name => "authoring-test";
                public string? GetContent(SystemPromptSectionContext context) => "{{content}}";
            }
        }
        """;

    private static string WorkspacePromptPlugin(string content) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Contributions;
        using DotCraft.Plugins;

        namespace Acme;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
            {
                context.Contributions.Add<ISystemPromptSection>(new Section(context.WorkspaceRoot));
                return ValueTask.CompletedTask;
            }

            private sealed class Section(string workspaceRoot) : ISystemPromptSection
            {
                public string Name => "authoring-test";
                public string? GetContent(SystemPromptSectionContext context) => "{{content}}|" + workspaceRoot;
            }
        }
        """;

    private const string NoopPlugin = """
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;

        namespace Acme;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                => ValueTask.CompletedTask;
        }
        """;

    private const string FaultingPlugin = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotCraft.Plugins;

        namespace Acme;

        public sealed class Plugin : IDotCraftPlugin
        {
            public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                => throw new InvalidOperationException("activation failed");
        }
        """;
}
