using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Security;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tools;
using ModelContextProtocol.Client;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemotePluginPreparationTests
{
    [Fact]
    public async Task AcceptedExportPinsImmutableBytes_UntilTransferReleasesThem()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        var manager = harness.CreateManager();
        await manager.StartAsync(default);
        var registration = Assert.Single(await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(1)));
        var export = await RemoteToolMetadata.SourceBinding(registration)!.ExportAsync();
        try
        {
            File.WriteAllText(Path.Combine(harness.PluginRoot("probe"), "resource.txt"), "changed-installed-file");
            var stopping = manager.DisposeAsync().AsTask();
            var root = export.Bundles.Single(file => file.Bundle.PluginId == "probe").RootPath;
            Assert.Equal("bundle-resource", await File.ReadAllTextAsync(Path.Combine(root, "resource.txt")));
            Assert.False(stopping.IsCompleted);
            export.Dispose();
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            export.Dispose();
            await manager.DisposeAsync();
        }
    }

    [Fact]
    public async Task NextTurnPreparationFailureRetainsOwnerError_WithoutDisconnectingCoreTools()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = Setup(home.Path, workspace.Path);
        await using var server = new RemoteToolHostTestServer(storage, ownerApprovals: new DeclineOwner());
        var state = storage.LoadHostState()!;
        storage.SaveHostState(state with
        {
            Peers = [state.Peers.Single() with
        { AuthorizationMode = RemoteToolAuthorization.WorkspacePreferred }]
        });
        await using var client = server.CreateClient(new ApproveService());
        await client.ConnectAsync("thread", server.PeerId, "repo");
        var registrations = await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(2, "thread"));
        var snapshot = new EffectiveToolSnapshotBuilder().Build(RemoteToolRegistrationRouter.Wrap(registrations, client), 2);

        await client.PrepareTurnAsync("thread", snapshot, "agent");

        var result = await new ToolDispatcher().DispatchAsync(snapshot, new("Probe", "Run"),
            new JsonObject { ["operation"] = "read" }, new("thread", "turn", "call", ToolInvocationAudience.Model));
        Assert.Equal(RemoteToolErrorCodes.ApprovalDeclined, result.Error?.Code);
        Assert.True(client.TryGetRoute("thread", out _));
        Assert.NotEmpty((await client.ListAsync("thread")).Hosts);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
    }

    [Fact]
    public async Task IncompleteUploadCannotActivate_AndReleasesStagingFiles()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        var registration = Assert.Single(await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(1)));
        using var export = await RemoteToolMetadata.SourceBinding(registration)!.ExportAsync();
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = Setup(home.Path, workspace.Path);
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        await using var raw = await server.ConnectRawAsync();
        var bundles = new List<PluginBundleTransfer>();
        foreach (var file in export.Bundles)
            bundles.Add(new(file.Bundle, await TransferFileTree.DescribeAsync(file.RootPath,
                new FileAccessGuard(file.RootPath), 10_000_000, default)));
        var definition = registration.Definition;
        var prepare = await raw.SendRequestAsync<PluginPrepareRequest, ExtensionResponse<PluginPrepareResponse>>(
            RemotePluginProtocol.Prepare, new(connected.Route.LeaseId, "repo", "thread", 1, "agent", bundles,
                [new(definition.Id.ToString(), definition.Name.ToString(), RemoteToolContractHasher.Compute(definition))]),
            RemoteToolHostProtocol.JsonOptions, default, default);
        Assert.True(prepare.Success, prepare.Error?.Message);
        var pending = prepare.Result!;
        var activate = await raw.SendRequestAsync<PluginActivateRequest, ExtensionResponse<PluginActivateResponse>>(
            RemotePluginProtocol.Activate, new(pending.PreparationId), RemoteToolHostProtocol.JsonOptions, default, default);

        Assert.False(activate.Success);
        Assert.Contains("incomplete", activate.Error?.Message);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
        var staging = Path.Combine(storage.RootPath, "workspaces", "repo", "plugin-staging", pending.PreparationId);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public async Task CancellingConnectStopsActivation_WithoutPublishingRouteOrLeakingLease()
    {
        using var harness = new PluginRuntimeHarness();
        RemotePluginFixture.Write(harness);
        await using var manager = harness.CreateManager();
        await manager.StartAsync(default);
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(workspace.Path, "block-plugin-activation"), "");
        await using var server = new RemoteToolHostTestServer(Setup(home.Path, workspace.Path));
        await using var client = server.CreateClient(new ApproveService());
        var registrations = await manager.ToolSource.GetRegistrationsAsync(PluginRuntimeHarness.PlanningContext(1, "thread"));
        client.UpdateRemoteToolSnapshot("thread", new EffectiveToolSnapshotBuilder().Build(registrations, 1), "agent");
        using var cancellation = new CancellationTokenSource();
        var connecting = client.ConnectAsync("thread", server.PeerId, "repo", cancellation.Token).AsTask();
        var log = Path.Combine(workspace.Path, "activation.log");
        await PluginRuntimeHarness.WaitForLineAsync(log, "entered");

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("cancelled", PluginLogFile.ReadLines(log));
        Assert.False(client.TryGetRoute("thread", out _));
        Assert.False(server.Leases.HasActiveLease);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "plugin-lifecycle.log")));
    }

    [Fact]
    public async Task ExpiredLeaseStopsResourcesBeforeWaitingForCalls_AndBlocksNewOwnerUntilDrained()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var leases = new WorkspaceLeaseManager(clock);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        leases.DrainResourcesAsync = async (_, calls) =>
        {
            stopping.SetResult();
            await calls;
            await released.Task;
        };
        var lease = leases.Acquire("owner", "workspace", Path.GetTempPath(), "host", 1);
        using var call = leases.EnterCall(lease.LeaseId, "workspace");
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Throws<RemoteToolHostException>(() => leases.Validate(lease.LeaseId, "workspace"));
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(call.Token.IsCancellationRequested);
        Assert.Throws<RemoteToolHostException>(() => leases.Acquire("next", "workspace", Path.GetTempPath(), "host", 1));
        call.Dispose();
        Assert.Throws<RemoteToolHostException>(() => leases.Acquire("next", "workspace", Path.GetTempPath(), "host", 1));
        released.SetResult();
        await leases.WaitForDrainAsync("workspace").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(lease.LeaseId, leases.Acquire("next", "workspace", Path.GetTempPath(), "host", 1).LeaseId);
    }

    private static RemoteToolHostStorage Setup(string home, string workspace)
    {
        var storage = new RemoteToolHostStorage(home, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace });
        return storage;
    }

    private sealed class DeclineOwner : IRemoteToolApprovalPresenter
    {
        public Task<bool> RequestAsync(RemoteToolApprovalRequest request, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
