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
        await using var client = server.CreateClient(new ApproveService());
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
        Assert.True(File.Exists(Path.Combine(installRoot, "dependency", ".craft-plugin", "plugin.json")));
        var stamp = File.GetLastWriteTimeUtc(Path.Combine(installRoot, "probe", "resource.txt"));
        await client.ConnectAsync("thread", server.PeerId, "repo");
        await client.PrepareTurnAsync("thread", snapshot, "agent");
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Path.Combine(installRoot, "probe", "resource.txt")));
        Assert.Single(PluginLogFile.ReadLines(Path.Combine(workspace.Path, "plugin-lifecycle.log")), line => line.StartsWith("activate:"));
        await client.DisconnectAsync("thread");
        Assert.Contains("dispose:first", PluginLogFile.ReadLines(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
        Assert.True(Directory.Exists(installRoot));
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
        await using var client = server.CreateClient(new ApproveService());
        var first = await SnapshotAsync(manager, client, "first", 1, "agent");
        var second = await SnapshotAsync(manager, client, "second", 1, "plan");
        await client.ConnectAsync("first", server.PeerId, "repo");
        await client.ConnectAsync("second", server.PeerId, "repo");
        Assert.True((await InvokeAsync(first, "first", "start")).Success);
        var other = await InvokeAsync(second, "second", "read");
        Assert.Contains("\"mode\":\"plan\"", other.Content);
        Assert.Contains("\"running\":false", other.Content);
        await client.DisconnectAsync("first");
        Assert.True((await InvokeAsync(second, "second", "start")).Success);
        var cancelled = await InvokeAsync(second, "second", "cancel");
        Assert.Contains("\"running\":false", cancelled.Content);
        var log = PluginLogFile.ReadLines(Path.Combine(workspace.Path, "plugin-lifecycle.log"));
        Assert.Contains("release:first", log);
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
        await using var client = server.CreateClient(new ApproveService());
        var first = await SnapshotAsync(manager, client, "thread", 1);
        await client.ConnectAsync("thread", server.PeerId, "repo");
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
        await using var client = server.CreateClient(new ApproveService());
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
        await using var client = server.CreateClient(new ApproveService());
        var snapshot = await SnapshotAsync(manager, client, "thread", 1);
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var running = InvokeAsync(snapshot, "thread", operation).AsTask();
        var log = Path.Combine(workspace.Path, "plugin-lifecycle.log");
        await PluginRuntimeHarness.WaitForLineAsync(log, "entered:thread");
        server.Leases.ReleaseWorkspace("repo");
        await server.Leases.WaitForDrainAsync("repo").WaitAsync(TimeSpan.FromSeconds(10));
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Success);
        Assert.Contains("cancelled:thread", PluginLogFile.ReadLines(log));
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
