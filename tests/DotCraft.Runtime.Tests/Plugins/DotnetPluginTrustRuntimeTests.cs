using DotCraft.Plugins;
using DotCraft.Contributions;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotnetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;
using DotCraft.Runtime;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>Covers the fingerprint-bound trust gate in front of every activation. The gate's promise
/// is negative, so the tests assert on the absence of a generation shadow copy, not only on the
/// reported state.</summary>
public sealed class DotnetPluginTrustRuntimeTests : IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task UntrustedPlugin_IsBlockedWithoutCreatingALoadContext()
    {
        _harness.WriteNoop("trust.untrusted");
        await using var manager = _harness.CreateManager(trustInstalled: false);

        await manager.StartAsync(CancellationToken.None);

        var plugin = Plugin(manager, "trust.untrusted");
        AssertState(plugin, PluginDotnetRuntimeState.Blocked);
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, plugin.TrustStatus);
        Assert.Null(plugin.GenerationId);
        var blocker = Assert.Single(
            plugin.Blockers,
            candidate => candidate.Code == PluginDotnetDiagnosticCodes.Untrusted);
        Assert.Equal("untrusted", blocker.Parameters["trustStatus"].GetString());
        Assert.Equal("trust.untrusted", blocker.Parameters["pluginId"].GetString());
        Assert.Equal(12, blocker.Parameters["fingerprintPrefix"].GetString()!.Length);
        Assert.Empty(GenerationAssemblies(_harness.GenerationsRoot));
    }

    [Fact]
    public async Task Trust_ActivatesTheBlockedPluginWithoutARestart()
    {
        _harness.WriteNoop("trust.granted");
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        AssertBlocker(manager, "trust.granted", PluginDotnetDiagnosticCodes.Untrusted);

        var granted = await manager.TrustAsync("trust.granted");

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, granted.Outcome);
        Assert.Equal(["trust.granted"], granted.AffectedPluginIds);
        var active = Plugin(manager, "trust.granted");
        AssertState(active, PluginDotnetRuntimeState.Active);
        Assert.Equal(PluginDotnetTrustStatus.Trusted, active.TrustStatus);
        Assert.NotEmpty(GenerationAssemblies(_harness.GenerationsRoot));

        var again = await manager.TrustAsync("trust.granted");
        Assert.Equal(PluginRuntimeMutationOutcome.NoChange, again.Outcome);
        Assert.Equal(active.GenerationId, Plugin(manager, "trust.granted").GenerationId);
    }

    [Fact]
    public async Task Trust_WithoutADurableStoreDoesNotActivateThePlugin()
    {
        _harness.WriteNoop("trust.no-store");
        await using var manager = _harness.CreateManager(
            trustInstalled: false,
            trustStoreAvailable: false);
        await manager.StartAsync(CancellationToken.None);

        var result = await manager.TrustAsync("trust.no-store");

        Assert.Equal(PluginRuntimeMutationOutcome.NotApplied, result.Outcome);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PluginTrustNotPersisted");
        AssertBlocker(manager, "trust.no-store", PluginDotnetDiagnosticCodes.Untrusted);
        Assert.Empty(GenerationAssemblies(_harness.GenerationsRoot));
    }

    [Fact]
    public async Task Trust_IsNotApplicableToAnUnknownPlugin()
    {
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);

        var result = await manager.TrustAsync("trust.absent");

        Assert.Equal(PluginRuntimeMutationOutcome.NotApplied, result.Outcome);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PluginRuntimeNotDeclared");
    }

    [Fact]
    public async Task ModifiedBundle_DeactivatesTheOldGenerationAndBlocksTheNewOne()
    {
        WriteToolPlugin("trust.update");
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        var before = Plugin(manager, "trust.update");
        AssertState(before, PluginDotnetRuntimeState.Active);
        Assert.NotEmpty(before.Tools!);

        await manager.QuiesceForMutationAsync("trust.update");
        var manifestPath = Path.Combine(_harness.PluginRoot("trust.update"), ".craft-plugin", "plugin.json");
        await File.AppendAllTextAsync(manifestPath, "\n");
        var reconciled = await manager.ReconcileAfterMutationAsync("trust.update");

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, reconciled.Outcome);
        var blocked = Plugin(manager, "trust.update");
        AssertState(blocked, PluginDotnetRuntimeState.Blocked);
        Assert.Equal(PluginDotnetTrustStatus.Modified, blocked.TrustStatus);
        Assert.Null(blocked.GenerationId);
        Assert.Empty(blocked.Tools!);
        var blocker = Assert.Single(
            blocked.Blockers,
            candidate => candidate.Code == PluginDotnetDiagnosticCodes.TrustModified);
        Assert.NotEqual(
            blocker.Parameters["fingerprintPrefix"].GetString(),
            blocker.Parameters["trustedFingerprintPrefix"].GetString());

        await manager.TrustAsync("trust.update");
        var reactivated = Plugin(manager, "trust.update");
        AssertState(reactivated, PluginDotnetRuntimeState.Active);
        Assert.NotEqual(before.GenerationId, reactivated.GenerationId);
        Assert.NotEmpty(reactivated.Tools!);
    }

    [Fact]
    public async Task RevokeTrust_StopsTheRunningPluginAndItsConsumers()
    {
        _harness.WriteNoop("revoke.provider");
        _harness.WriteNoop(
            "revoke.consumer",
            dependencies: new Dictionary<string, string> { ["revoke.provider"] = "1.0.0" });
        await using var manager = _harness.CreateManager();
        await manager.StartAsync(CancellationToken.None);
        AssertState(Plugin(manager, "revoke.provider"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "revoke.consumer"), PluginDotnetRuntimeState.Active);

        var revoked = await manager.RevokeTrustAsync("revoke.provider");

        Assert.Equal(PluginRuntimeMutationOutcome.Applied, revoked.Outcome);
        Assert.Equal(["revoke.consumer", "revoke.provider"], revoked.AffectedPluginIds);
        var provider = Plugin(manager, "revoke.provider");
        AssertState(provider, PluginDotnetRuntimeState.Blocked);
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, provider.TrustStatus);
        Assert.Null(provider.GenerationId);
        Assert.Contains(provider.Blockers, blocker => blocker.Code == PluginDotnetDiagnosticCodes.Untrusted);
        var consumer = Plugin(manager, "revoke.consumer");
        AssertState(consumer, PluginDotnetRuntimeState.Blocked);
        Assert.Null(consumer.GenerationId);
        Assert.Contains(consumer.Blockers, blocker => blocker.Code == "PluginDependencyUnsatisfied");

        Assert.Equal(
            PluginRuntimeMutationOutcome.NoChange,
            (await manager.RevokeTrustAsync("revoke.provider")).Outcome);

        await manager.TrustAsync("revoke.provider");
        AssertState(Plugin(manager, "revoke.provider"), PluginDotnetRuntimeState.Active);
        AssertState(Plugin(manager, "revoke.consumer"), PluginDotnetRuntimeState.Active);
    }

    [Fact]
    public async Task TrustGrant_SurvivesARestartThroughTheUserAuthority()
    {
        _harness.WriteNoop("trust.persisted");
        var configPath = Path.Combine(_harness.Root, "user-data", "config.json");
        _harness.Config.GlobalConfigPath = configPath;

        await using (var first = _harness.CreateManager(trustInstalled: false))
        {
            await first.StartAsync(CancellationToken.None);
            AssertBlocker(first, "trust.persisted", PluginDotnetDiagnosticCodes.Untrusted);
            Assert.Equal(PluginRuntimeMutationOutcome.Applied, (await first.TrustAsync("trust.persisted")).Outcome);
            AssertState(Plugin(first, "trust.persisted"), PluginDotnetRuntimeState.Active);
            await first.StopAsync(CancellationToken.None);
        }

        await using var second = _harness.CreateManager(trustInstalled: false);
        await second.StartAsync(CancellationToken.None);

        var plugin = Plugin(second, "trust.persisted");
        AssertState(plugin, PluginDotnetRuntimeState.Active);
        Assert.Equal(PluginDotnetTrustStatus.Trusted, plugin.TrustStatus);
        var authorityPath = PluginDotnetTrustConfigStore.PathForConfig(configPath);
        Assert.Contains("trust.persisted", await File.ReadAllTextAsync(authorityPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalAuthorityRevoke_StopsAnActiveGeneration()
    {
        const string pluginId = "trust.external-revoke";
        _harness.WriteNoop(pluginId);
        var configPath = Path.Combine(_harness.Root, "user-data", "config.json");
        _harness.Config.GlobalConfigPath = configPath;
        var authority = new PluginDotnetTrustConfigStore(
            PluginDotnetTrustConfigStore.PathForConfig(configPath));
        var fingerprint = PluginBundleFingerprint.Compute(_harness.PluginRoot(pluginId));
        authority.SetTrusted(pluginId, fingerprint, isTrusted: true);

        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        AssertState(Plugin(manager, pluginId), PluginDotnetRuntimeState.Active);

        authority.SetTrusted(pluginId, fingerprint, isTrusted: false);

        var blocked = await WaitForStateAsync(manager, pluginId, PluginDotnetRuntimeState.Blocked);
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, blocked.TrustStatus);
        AssertBlocker(manager, pluginId, PluginDotnetDiagnosticCodes.Untrusted);
    }

    [Fact]
    public async Task ExternalAuthorityRevoke_DuringActivationRejectsTheFinalCommit()
    {
        const string pluginId = "trust.revoke-during-activation";
        WritePlugin(
            _harness.PluginRoot(pluginId),
            pluginId,
            "TrustPending.Plugin",
            """
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Contributions;
            using DotCraft.Plugins;
            namespace TrustPending;
            public sealed class Plugin : IDotCraftPlugin
            {
                public async ValueTask ActivateAsync(
                    IPluginActivationContext context,
                    CancellationToken cancellationToken)
                {
                    Directory.CreateDirectory(context.DataRoot);
                    context.Contributions.Add<ISystemPromptSection>(new Section());
                    File.WriteAllText(Path.Combine(context.DataRoot, "activation-started"), "started");
                    while (!File.Exists(Path.Combine(context.DataRoot, "activation-release")))
                        await Task.Delay(10, cancellationToken);
                }
                private sealed class Section : ISystemPromptSection
                {
                    public string Name => "trust-pending";
                    public string? GetContent(SystemPromptSectionContext context) => "must-not-publish";
                }
            }
            """);
        var configPath = Path.Combine(_harness.Root, "user-data", "config.json");
        _harness.Config.GlobalConfigPath = configPath;
        var authorityPath = PluginDotnetTrustConfigStore.PathForConfig(configPath);
        var fingerprint = PluginBundleFingerprint.Compute(_harness.PluginRoot(pluginId));
        new PluginDotnetTrustConfigStore(authorityPath)
            .SetTrusted(pluginId, fingerprint, isTrusted: true);

        await using var manager = _harness.CreateManager(trustInstalled: false);
        var observedStates = new System.Collections.Concurrent.ConcurrentQueue<PluginDotnetRuntimeState>();
        manager.SnapshotChanged += (_, args) =>
        {
            var observed = args.Snapshot.Plugins.SingleOrDefault(
                plugin => plugin.PluginId == pluginId);
            if (observed != null)
                observedStates.Enqueue(observed.State);
        };
        var start = manager.StartAsync(CancellationToken.None);
        var releasePath = _harness.DataPath(pluginId, "activation-release");
        string generationId;
        try
        {
            await WaitForFileAsync(_harness.DataPath(pluginId, "activation-started"));
            var activating = Plugin(manager, pluginId);
            AssertState(activating, PluginDotnetRuntimeState.Activating);
            generationId = Assert.IsType<string>(activating.GenerationId);

            // Use a second authority instance so the runtime's change worker must wait behind the
            // activation mutation while the final commit check observes the durable revocation.
            new PluginDotnetTrustConfigStore(authorityPath)
                .SetTrusted(pluginId, fingerprint, isTrusted: false);
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(releasePath)!);
            File.WriteAllText(releasePath, "release");
        }

        await start;

        var blocked = Plugin(manager, pluginId);
        AssertState(blocked, PluginDotnetRuntimeState.Blocked);
        Assert.Equal(PluginDotnetTrustStatus.Untrusted, blocked.TrustStatus);
        Assert.Null(blocked.GenerationId);
        AssertBlocker(manager, pluginId, PluginDotnetDiagnosticCodes.Untrusted);
        Assert.Empty(_harness.Registry.ResolveEntries<ISystemPromptSection>());
        Assert.Equal(0, _harness.Registry.GetRevision<ISystemPromptSection>());
        Assert.False(manager.CallGates.IsCallable(pluginId, generationId));
        Assert.DoesNotContain(PluginDotnetRuntimeState.Active, observedStates);
    }

    [Fact]
    public async Task WorkspaceSuppliedGrant_DoesNotTrustAPluginWhenAUserConfigExists()
    {
        _harness.WriteNoop("trust.workspace");
        _harness.Config.GlobalConfigPath = Path.Combine(_harness.Root, "user-data", "config.json");

        // A workspace config travels with a repository, so only the user-global file grants trust.
        _harness.TrustInstalled();
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);

        AssertBlocker(manager, "trust.workspace", PluginDotnetDiagnosticCodes.Untrusted);
        Assert.Empty(GenerationAssemblies(_harness.GenerationsRoot));
    }

    [Fact]
    public async Task BuiltInDeploymentMarker_DoesNotGrantImplicitTrust()
    {
        _harness.WriteNoop("trust.builtin");
        await File.WriteAllTextAsync(
            Path.Combine(_harness.PluginRoot("trust.builtin"), BuiltInPluginDeployer.MarkerFile),
            "builtin");
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);

        // The marker lives in a workspace-writable directory, so it says nothing about trust.
        AssertBlocker(manager, "trust.builtin", PluginDotnetDiagnosticCodes.Untrusted);
        Assert.Empty(GenerationAssemblies(_harness.GenerationsRoot));

        await manager.TrustAsync("trust.builtin");
        AssertState(Plugin(manager, "trust.builtin"), PluginDotnetRuntimeState.Active);
    }

    private void WriteToolPlugin(string pluginId) =>
        WritePluginBundle(
            _harness.PluginRoot(pluginId),
            pluginId,
            "TrustTools.Plugin",
            """
            using System.Text.Json.Nodes;
            using System.Threading;
            using System.Threading.Tasks;
            using DotCraft.Plugins;
            using DotCraft.Tests.Bundle;
            using DotCraft.Tools;
            namespace TrustTools;
            public sealed class Plugin : IDotCraftPlugin
            {
                public ValueTask ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
                {
                    context.Contributions.Add<IToolSource>(new Ping());
                    return ValueTask.CompletedTask;
                }
                private sealed class Ping() : TestTool("ping-v1", "trust", "ping", "Answers with pong.")
                {
                    public override ValueTask<ToolExecutionResult> InvokeAsync(
                        ToolInvocationContext context,
                        JsonObject arguments,
                        CancellationToken cancellationToken = default)
                        => ValueTask.FromResult(ToolExecutionResult.Succeeded("pong"));
                }
            }
            """);
}
