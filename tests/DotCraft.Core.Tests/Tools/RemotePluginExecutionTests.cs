using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemotePluginExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Connect_PreparesAcceptedBundleDependenciesAndResources_WithoutPreinstallation(bool deferred)
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness, deferred: deferred);
        File.WriteAllText(Path.Combine(harness.Workspace, "executor-unavailable"), "");
        harness.CreatePluginConfigStore().Mutate(PluginManifestParser.Load(harness.PluginRoot("probe")).Manifest!,
            "personal", [new PluginConfigMutation("set", "label", JsonSerializer.SerializeToElement("agent-setting"))]);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = Setup(home.Path, workspace.Path);
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient();
        var snapshot = await SnapshotAsync(manager, client, "thread", 1);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        Assert.Contains("Probe.Run", connected.MatchedTools);
        var result = await InvokeAsync(snapshot, "thread", "read");
        Assert.True(result.Success, result.Error?.Message);
        using var document = JsonDocument.Parse(result.Content!);
        Assert.Equal("bundle-resource", document.RootElement.GetProperty("resource").GetString());
        Assert.Equal(workspace.Path, document.RootElement.GetProperty("workspace").GetString());
        Assert.Equal("agent-setting", document.RootElement.GetProperty("settings").GetProperty("label").GetString());
        var local = await new ToolDispatcher().DispatchAsync(snapshot, new("Probe", "Run"),
            new JsonObject { ["operation"] = "read", ["target"] = "local" },
            new("thread", "turn", "local-call", ToolInvocationAudience.Model));
        Assert.False(local.Success);
        var installRoot = Path.Combine(storage.RootPath, "workspaces", "repo", "plugins");
        var dependencyRoot = Path.Combine(installRoot, "dependency", PluginBundleFingerprint.Compute(harness.PluginRoot("dependency")));
        Assert.True(File.Exists(Path.Combine(dependencyRoot, ".craft-plugin", "plugin.json")));
        var probeRoot = Path.Combine(installRoot, "probe", PluginBundleFingerprint.Compute(harness.PluginRoot("probe")));
        Assert.Equal(probeRoot, document.RootElement.GetProperty("contentRoot").GetString());
        Assert.StartsWith(probeRoot + Path.DirectorySeparatorChar, document.RootElement.GetProperty("assemblyPath").GetString());
        var resource = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(installRoot)!, "resource.txt", SearchOption.AllDirectories));
        Assert.Equal(Path.Combine(probeRoot, "resource.txt"), resource);
        var stamp = File.GetLastWriteTimeUtc(resource);
        await client.ConnectAsync("thread", server.PeerId, "repo");
        await client.PrepareTurnAsync("thread", snapshot, "agent");
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(resource));
        Assert.Single(PluginLogFile.ReadLines(Path.Combine(workspace.Path, "plugin-lifecycle.log")), line => line.StartsWith("activate:"));
        await client.DisconnectAsync("thread");
        Assert.Contains("dispose:first", PluginLogFile.ReadLines(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
        Assert.True(Directory.Exists(installRoot));
        await client.ConnectAsync("thread", server.PeerId, "repo");
        Assert.True((await InvokeAsync(snapshot, "thread", "read")).Success);
        Assert.Equal(resource, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(installRoot)!, "resource.txt", SearchOption.AllDirectories)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(resource));
    }

    [Fact]
    public async Task ThreadsKeepDistinctSnapshotsAndState_ReleaseDoesNotStopAnotherThread()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path));
        await using var client = server.CreateClient();
        var first = await SnapshotAsync(manager, client, "first", 1, "agent");
        var second = await SnapshotAsync(manager, client, "second", 1, "plan");
        await client.ConnectAsync("first", server.PeerId, "repo");
        await client.ConnectAsync("second", server.PeerId, "repo");
        var started = await InvokeAsync(first, "first", "start");
        Assert.True(started.Success);
        var firstThread = JsonDocument.Parse(started.Content!).RootElement.GetProperty("thread").GetString();
        var other = await InvokeAsync(second, "second", "read");
        Assert.Contains("\"mode\":\"plan\"", other.Content);
        Assert.Contains("\"running\":false", other.Content);
        var firstRoot = JsonDocument.Parse(started.Content!).RootElement.GetProperty("contentRoot").GetString()!;
        var secondRoot = JsonDocument.Parse(other.Content!).RootElement.GetProperty("contentRoot").GetString()!;
        Assert.Equal(firstRoot, secondRoot);
        await client.DisconnectAsync("first");
        Assert.True((await InvokeAsync(second, "second", "start")).Success);
        var cancelled = await InvokeAsync(second, "second", "cancel");
        Assert.Contains("\"running\":false", cancelled.Content);
        var log = PluginLogFile.ReadLines(Path.Combine(workspace.Path, "plugin-lifecycle.log"));
        Assert.Contains("release:" + firstThread, log);
        Assert.DoesNotContain("dispose:first", log);
    }

    [Fact]
    public async Task NewGenerationWithSameSchema_InvalidatesOldCalls_AndNextTurnPreparesNewCode()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path));
        await using var client = server.CreateClient();
        var first = await SnapshotAsync(manager, client, "thread", 1);
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var initial = await InvokeAsync(first, "thread", "read");
        var oldRoot = JsonDocument.Parse(initial.Content!).RootElement.GetProperty("contentRoot").GetString()!;
        await manager.QuiesceForMutationAsync("probe");
        RemotePluginFixture.Write(harness, "second");
        harness.TrustInstalled();
        await manager.ReconcileAfterMutationAsync("probe");
        Assert.False((await InvokeAsync(first, "thread", "read")).Success);
        var second = await SnapshotAsync(manager, client, "thread", 2);
        Assert.Equal(RemoteToolContractHasher.Compute(RemoteToolMetadata.NativeDefinition(first.Registrations.Values.Single())),
            RemoteToolContractHasher.Compute(RemoteToolMetadata.NativeDefinition(second.Registrations.Values.Single())));
        await client.PrepareTurnAsync("thread", second, "agent");
        var result = await InvokeAsync(second, "thread", "read");
        Assert.True(result.Success, result.Error?.Message);
        Assert.Contains("\"implementation\":\"second\"", result.Content);
        Assert.False((await InvokeAsync(first, "thread", "read")).Success);
        var newRoot = JsonDocument.Parse(result.Content!).RootElement.GetProperty("contentRoot").GetString()!;
        Assert.NotEqual(oldRoot, newRoot);
        for (var attempt = 0; attempt < 100 && Directory.Exists(oldRoot); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await client.PrepareTurnAsync("thread", second, "agent");
            await Task.Delay(50);
        }
        Assert.False(Directory.Exists(oldRoot));
        Assert.Equal(Path.Combine(newRoot, "resource.txt"), Assert.Single(Directory.GetFiles(Path.GetDirectoryName(newRoot)!, "resource.txt", SearchOption.AllDirectories)));
    }

    [Fact]
    public async Task SettingsChangeReusesInstalledContent_AfterOldGenerationReclaims()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path));
        await using var client = server.CreateClient();
        var first = await SnapshotAsync(manager, client, "thread", 1);
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var initial = await InvokeAsync(first, "thread", "read");
        var root = JsonDocument.Parse(initial.Content!).RootElement.GetProperty("contentRoot").GetString()!;
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(root, "resource.txt"));
        await manager.QuiesceForMutationAsync("probe");
        harness.CreatePluginConfigStore().Mutate(PluginManifestParser.Load(harness.PluginRoot("probe")).Manifest!,
            "personal", [new PluginConfigMutation("set", "label", JsonSerializer.SerializeToElement("changed"))]);
        await manager.ReconcileAfterMutationAsync("probe");
        var second = await SnapshotAsync(manager, client, "thread", 2);
        await client.PrepareTurnAsync("thread", second, "agent");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await client.PrepareTurnAsync("thread", second, "agent");
        var result = await InvokeAsync(second, "thread", "read");
        Assert.True(result.Success, result.Error?.Message);
        using var document = JsonDocument.Parse(result.Content!);
        Assert.Equal("changed", document.RootElement.GetProperty("settings").GetProperty("label").GetString());
        Assert.Equal(root, document.RootElement.GetProperty("contentRoot").GetString());
        Assert.Equal(Path.Combine(root, "resource.txt"), Assert.Single(Directory.GetFiles(Path.GetDirectoryName(root)!, "resource.txt", SearchOption.AllDirectories)));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(root, "resource.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerApprovalPrecedesRemoteAssemblyLoad(bool approved)
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = Setup(home.Path, workspace.Path);
        var owner = new Owner(approved);
        await using var server = new RemoteToolHostTestServer(storage, ownerApprovals: owner);
        storage.SaveHostState(storage.LoadHostState()! with
        {
            Peers = [storage.LoadHostState()!.Peers.Single() with { AuthorizationMode = RemoteToolAuthorization.WorkspacePreferred }]
        });
        await using var client = server.CreateClient();
        await SnapshotAsync(manager, client, "thread", 1);
        if (approved) await client.ConnectAsync("thread", server.PeerId, "repo");
        else
        {
            var failure = await Assert.ThrowsAsync<RemoteToolHostException>(async () => await client.ConnectAsync("thread", server.PeerId, "repo"));
            Assert.Equal(RemoteToolErrorCodes.ApprovalDeclined, failure.Code);
            Assert.False(client.TryGetRoute("thread", out _));
        }
        Assert.Equal(approved, File.Exists(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
        Assert.Single(owner.Requests, request => request.Kind == "plugin");
    }

    [Theory]
    [InlineData("block")]
    [InlineData("block-until-stop")]
    public async Task LeaseRevocationCancelsAndDrainsAnExecutingPlugin(string operation)
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path));
        await using var client = server.CreateClient();
        var snapshot = await SnapshotAsync(manager, client, "thread", 1);
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var ready = await InvokeAsync(snapshot, "thread", "read");
        var remoteThread = JsonDocument.Parse(ready.Content!).RootElement.GetProperty("thread").GetString();
        var running = InvokeAsync(snapshot, "thread", operation).AsTask();
        var log = Path.Combine(workspace.Path, "plugin-lifecycle.log");
        await PluginRuntimeHarness.WaitForLineAsync(log, "entered:" + remoteThread);
        server.Leases.ReleaseWorkspace("repo");
        await server.Leases.WaitForDrainAsync("repo").WaitAsync(TimeSpan.FromSeconds(10));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
        Assert.Contains("cancelled:" + remoteThread, PluginLogFile.ReadLines(log));
        Assert.Contains("dispose:first", PluginLogFile.ReadLines(log));
    }

    private static RemoteToolHostStorage Setup(string home, string workspace)
    {
        var storage = new RemoteToolHostStorage(home, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace });
        return storage;
    }

    private static async Task<EffectiveToolSnapshot> SnapshotAsync(DotNetPluginRuntimeManager manager,
        RemoteToolHostClient client, string threadId, long revision, string mode = "agent")
    {
        var planning = new ToolPlanningContext(threadId, null, Path.GetTempPath(), Path.GetTempPath(), mode, null, [], revision);
        var registrations = await manager.ToolSource.GetRegistrationsAsync(planning);
        var snapshot = new EffectiveToolSnapshotBuilder().Build(RemoteToolRegistrationRouter.Wrap(registrations, client), revision);
        client.UpdateRemoteToolSnapshot(threadId, snapshot, mode);
        return snapshot;
    }

    private static ValueTask<ToolExecutionResult> InvokeAsync(EffectiveToolSnapshot snapshot, string threadId, string operation) =>
        new ToolDispatcher().DispatchAsync(snapshot, new("Probe", "Run"), new JsonObject { ["operation"] = operation },
            new(threadId, "turn", Guid.NewGuid().ToString("N"), ToolInvocationAudience.Model));

    private sealed class Owner(bool accepted) : IRemoteToolApprovalPresenter
    {
        internal List<RemoteToolApprovalRequest> Requests { get; } = [];
        public Task<bool> RequestAsync(RemoteToolApprovalRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(accepted);
        }
    }
}
