using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteToolOwnerAuthorizationTests
{
    [Theory]
    [InlineData(RemoteToolAuthorization.WorkspacePreferred, false, false)]
    [InlineData(RemoteToolAuthorization.WorkspacePreferred, true, true)]
    [InlineData(RemoteToolAuthorization.FullAccess, false, true)]
    public async Task OutsideWrite_RequiresOwnerDecision_NotInviterApproval(string mode, bool accept, bool writes)
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var owner = new Owner(accept);
        var storage = Setup(home.Path, workspace.Path, mode);
        await using var server = new RemoteToolHostTestServer(storage, ownerApprovals: owner);
        var inviter = new ApproveService();
        await using var client = server.CreateClient(inviter);
        var tools = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. tools.Select(tool => tool.Definition)]);
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        var target = Path.Combine(outside.Path, "result.txt");
        var result = await Invoke(client, connected.Route, tools, "WriteFile",
            new JsonObject { ["path"] = target, ["content"] = "approved-content" });
        Assert.Equal(writes, File.Exists(target));
        Assert.Equal(writes, result.Success);
        Assert.Equal(mode == RemoteToolAuthorization.FullAccess ? 0 : 1, owner.Requests.Count);
        Assert.Equal(0, inviter.RequestCount);
        if (!writes) Assert.Equal(RemoteToolErrorCodes.ApprovalDeclined, result.Error?.Code);
    }

    [Fact]
    public async Task Preferred_WorkspaceWriteDoesNotPrompt_ButCommandWithoutPresenterIsDenied()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = Setup(home.Path, workspace.Path, RemoteToolAuthorization.WorkspacePreferred);
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        var tools = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. tools.Select(tool => tool.Definition)]);
        var connected = await client.ConnectAsync("thread", server.PeerId, "repo");
        var file = await Invoke(client, connected.Route, tools, "WriteFile",
            new JsonObject { ["path"] = "result.txt", ["content"] = "value" });
        Assert.True(file.Success, file.Error?.Message);
        var command = await Invoke(client, connected.Route, tools, "Exec",
            new JsonObject { ["command"] = "echo must-not-run" });
        Assert.False(command.Success);
        Assert.Equal(RemoteToolErrorCodes.ApprovalDeclined, command.Error?.Code);
    }

    [Fact]
    public async Task LegacyPeerCannotAcquireWorkspace()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = Setup(home.Path, workspace.Path, null);
        await using var server = new RemoteToolHostTestServer(storage);
        await using var client = server.CreateClient(new ApproveService());
        await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await client.ConnectAsync("thread", server.PeerId, "repo"));
        Assert.False(server.Leases.HasActiveLease);
    }

    [Fact]
    public async Task CancelledOwnerRequestCannotWriteAfterLateAcceptance()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var owner = new PendingOwner();
        var storage = Setup(home.Path, workspace.Path, RemoteToolAuthorization.WorkspacePreferred);
        var server = new RemoteToolHostTestServer(storage, ownerApprovals: owner);
        await using var client = server.CreateClient(new ApproveService());
        var tools = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. tools.Select(tool => tool.Definition)]);
        var route = (await client.ConnectAsync("thread", server.PeerId, "repo")).Route;
        using var cancellation = new CancellationTokenSource();
        var target = Path.Combine(outside.Path, "never.txt");
        var running = Invoke(client, route, tools, "WriteFile",
            new JsonObject { ["path"] = target, ["content"] = "never" }, cancellation.Token).AsTask();
        await owner.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        try { await running; } catch (OperationCanceledException) { }
        owner.Decision.TrySetResult(true);
        await server.DisposeAsync();
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task ExplicitDenyOverridesFullAccess()
    {
        using var home = new TemporaryDirectory();
        using var workspace = new TemporaryDirectory();
        var storage = Setup(home.Path, workspace.Path, RemoteToolAuthorization.FullAccess);
        storage.SaveHostState(storage.LoadHostState()! with { ToolPolicies = new() { ["WriteFile"] = "deny" } });
        var owner = new Owner(true);
        await using var server = new RemoteToolHostTestServer(storage, ownerApprovals: owner);
        await using var client = server.CreateClient(new ApproveService());
        var tools = await RemoteToolHostTestHost.AgentRegistrationsAsync(workspace.Path, home.Path);
        client.UpdateRemoteToolDefinitions([.. tools.Select(tool => tool.Definition)]);
        var route = (await client.ConnectAsync("thread", server.PeerId, "repo")).Route;
        var result = await Invoke(client, route, tools, "WriteFile", new JsonObject { ["path"] = "no.txt", ["content"] = "no" });
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, result.Error?.Code);
        Assert.False(File.Exists(Path.Combine(workspace.Path, "no.txt")));
        Assert.Empty(owner.Requests);
    }

    private static RemoteToolHostStorage Setup(string home, string workspace, string? mode)
    {
        var storage = new RemoteToolHostStorage(home, new MemoryCredentialStore());
        var state = RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = workspace });
        storage.SaveHostState(state with { Peers = [new RemoteToolHubPeer
        {
            PeerId = "sat_test", HubHost = "localhost", HubPort = 1, CredentialReference = "test",
            HubLabel = "Ann", WorkspaceId = "repo", AuthorizationMode = mode, AuthorizationRevision = 7
        }] });
        return storage;
    }

    private static ValueTask<ToolExecutionResult> Invoke(RemoteToolHostClient client, RemoteToolRoute route,
        IReadOnlyList<ToolRegistration> tools, string name, JsonObject args, CancellationToken ct = default)
    {
        var tool = tools.Single(item => item.Definition.Name.Name == name);
        return client.InvokeAsync(route, tool.Definition, RemoteToolContractHasher.Compute(tool.Definition),
            new ToolInvocationContext("thread", "turn", Guid.NewGuid().ToString("N"), ToolInvocationAudience.Model,
                tool.Definition.Name, tool.Definition.Id, tool.Binding.Id, 1, DateTimeOffset.UtcNow), args, ct);
    }

    private sealed class Owner(bool accepts) : IRemoteToolApprovalPresenter
    {
        public List<RemoteToolApprovalRequest> Requests { get; } = [];
        public Task<bool> RequestAsync(RemoteToolApprovalRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(accepts);
        }
    }

    private sealed class PendingOwner : IRemoteToolApprovalPresenter
    {
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> RequestAsync(RemoteToolApprovalRequest request, CancellationToken cancellationToken)
        {
            Requested.TrySetResult();
            return Decision.Task;
        }
    }
}
