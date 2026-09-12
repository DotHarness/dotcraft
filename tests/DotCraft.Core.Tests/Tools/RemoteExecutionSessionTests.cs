using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tests.Runtime.Plugins;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteExecutionSessionTests
{
    [Fact]
    public async Task IndependentSessionsShareLease_AndRemainingConnectionRenewsIt()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var first = await fixture.OpenAsync("first");
        await using var second = await fixture.OpenAsync("second");
        Assert.Equal(first.Route.LeaseId, second.Route.LeaseId);
        Assert.NotEqual(first.Route, second.Route);
        await using var otherOwner = new RemoteExecutionClient();
        var busy = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await otherOwner.OpenSessionAsync("third", fixture.Server.PeerId, "repo", await fixture.ConnectionAsync()));
        Assert.Equal(RemoteToolErrorCodes.WorkspaceBusy, busy.Code);

        await first.DisposeAsync();
        var before = fixture.Server.Leases.GetStatus("repo")!.ExpiresAt;
        await fixture.WaitAsync(() => fixture.Server.Leases.GetStatus("repo")!.ExpiresAt > before, TimeSpan.FromSeconds(20));
        Assert.True((await fixture.InvokeAsync(second, "WriteFile", new() { ["path"] = "shared.txt", ["content"] = "kept" })).Success);
        var captured = await second.InvokeAsync(first.Route, fixture.Tool("ReadFile").Definition,
            RemoteToolContractHasher.Compute(fixture.Tool("ReadFile").Definition),
            fixture.Context(fixture.Tool("ReadFile")), new() { ["path"] = "shared.txt" });
        Assert.Equal(RemoteToolErrorCodes.LeaseLost, captured.Error?.Code);
        await second.DisposeAsync();
        Assert.False(fixture.Server.Leases.HasActiveLease);
        Assert.Equal("kept", await File.ReadAllTextAsync(Path.Combine(fixture.Workspace.Path, "shared.txt")));
    }

    [Fact]
    public async Task ClosingSessionCancelsOwnerDecision_WithoutStoppingSibling()
    {
        var owner = new PendingOwner();
        await using var fixture = await RemoteExecutionFixture.CreateAsync(owner);
        var state = fixture.Storage.LoadHostState()!;
        fixture.Storage.SaveHostState(state with
        {
            Peers = [state.Peers.Single() with { AuthorizationMode = RemoteToolAuthorization.WorkspacePreferred }]
        });
        await using var first = await fixture.OpenAsync("first");
        await using var second = await fixture.OpenAsync("second");
        using var outside = new TemporaryDirectory();
        var target = Path.Combine(outside.Path, "not-written.txt");
        var pending = fixture.InvokeAsync(first, "WriteFile", new() { ["path"] = target, ["content"] = "not written" }).AsTask();
        await owner.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await first.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await owner.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.False((await pending).Success); } catch (OperationCanceledException) { }
        owner.Decision.TrySetResult(true);
        Assert.False(File.Exists(target));
        Assert.True((await fixture.InvokeAsync(second, "WriteFile", new() { ["path"] = "sibling.txt", ["content"] = "ok" })).Success);
    }

    [Fact]
    public async Task TerminalAndArtifactOwnershipSurvivesSharedLease_AndCleansOnlyClosedSession()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var first = await fixture.OpenAsync("first");
        await using var second = await fixture.OpenAsync("second");
        var a = await fixture.StartTerminalAsync(first);
        var b = await fixture.StartTerminalAsync(second);
        var denied = await fixture.InvokeAsync(first, "WriteStdin", new() { ["sessionId"] = b, ["input"] = "" });
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, denied.Error?.Code);
        var aFile = Assert.Single(Directory.GetFiles(fixture.Storage.RootPath, a + ".json", SearchOption.AllDirectories));
        var bFile = Assert.Single(Directory.GetFiles(fixture.Storage.RootPath, b + ".json", SearchOption.AllDirectories));
        var aPid = int.Parse(PluginLogFile.ReadText(Path.Combine(fixture.Workspace.Path, "first.pid")));
        var bPid = int.Parse(PluginLogFile.ReadText(Path.Combine(fixture.Workspace.Path, "second.pid")));
        var privateRead = await fixture.InvokeAsync(first, "ReadFile", new() { ["path"] = bFile });
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, privateRead.Error?.Code);

        await first.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(aFile));
        Assert.True(File.Exists(bFile));
        Assert.False(IsRunning(aPid));
        Assert.True(IsRunning(bPid));
        Assert.True((await fixture.InvokeAsync(second, "WriteStdin", new() { ["sessionId"] = b, ["input"] = "" })).Success);
        await second.DisposeAsync();
        Assert.False(IsRunning(bPid));
    }

    [Fact]
    public async Task NativeChildrenShareSession_ButReleasingOneDrainsOnlyItsThread()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync();
        await using var client = fixture.Server.CreateClient();
        var snapshot = new EffectiveToolSnapshotBuilder().Build(fixture.Tools, 1);
        client.UpdateRemoteToolSnapshot("root", snapshot, "agent");
        var route = (await client.ConnectAsync("root", fixture.Server.PeerId, "repo")).Route;
        Assert.True(client.TryForkRoute("root", "a"));
        Assert.True(client.TryForkRoute("root", "b"));
        var exec = fixture.Tool("Exec");
        var arguments = RemoteExecutionFixture.TerminalArguments();
        var a = await client.InvokeAsync(route, exec.Definition, RemoteToolContractHasher.Compute(exec.Definition),
            fixture.Context(exec, "a"), arguments);
        var b = await client.InvokeAsync(route, exec.Definition, RemoteToolContractHasher.Compute(exec.Definition),
            fixture.Context(exec, "b"), arguments);
        var aId = RemoteExecutionFixture.TerminalId(a);
        var bId = RemoteExecutionFixture.TerminalId(b);
        await client.PrepareTurnAsync("b", new EffectiveToolSnapshotBuilder().Build(fixture.Tools, 2), "agent");
        await client.DisconnectAsync("a");
        Assert.Empty(Directory.GetFiles(fixture.Storage.RootPath, aId + ".json", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(fixture.Storage.RootPath, bId + ".json", SearchOption.AllDirectories));
        Assert.True(client.TryGetRoute("b", out var child));
        Assert.Equal(route, child);
        await client.DisconnectAsync("root");
        Assert.True(client.TryGetRoute("b", out _));
        await client.DisconnectAsync("b");
        Assert.False(fixture.Server.Leases.HasActiveLease);
    }

    private static bool IsRunning(int pid)
    {
        try { using var process = System.Diagnostics.Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private sealed class PendingOwner : IRemoteToolApprovalPresenter
    {
        internal TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> RequestAsync(RemoteToolApprovalRequest request, CancellationToken cancellationToken)
        {
            Requested.TrySetResult();
            try { return await Decision.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
    }
}

internal sealed class RemoteExecutionFixture : IAsyncDisposable
{
    internal TemporaryDirectory Home { get; } = new();
    internal TemporaryDirectory Workspace { get; } = new();
    internal RemoteToolHostStorage Storage { get; }
    internal RemoteToolHostTestServer Server { get; }
    internal RemoteExecutionClient Client { get; } = new();
    internal IReadOnlyList<ToolRegistration> Tools { get; private set; } = [];

    private RemoteExecutionFixture(IRemoteToolApprovalPresenter? owner)
    {
        Storage = new(Home.Path, new MemoryCredentialStore());
        RemoteToolHostTestHost.Setup(Storage, new Dictionary<string, string> { ["repo"] = Workspace.Path });
        Server = new(Storage, ownerApprovals: owner);
    }

    internal static async Task<RemoteExecutionFixture> CreateAsync(IRemoteToolApprovalPresenter? owner = null, bool enableLsp = false)
    {
        var fixture = new RemoteExecutionFixture(owner);
        fixture.Tools = await RemoteToolHostTestHost.AgentRegistrationsAsync(fixture.Workspace.Path, fixture.Home.Path, enableLsp);
        return fixture;
    }

    internal ValueTask<RemoteToolHostConnection> ConnectionAsync() => Server.Directory.ConnectAsync(Server.PeerId, default);
    internal async Task<RemoteExecutionSession> OpenAsync(string id) =>
        await Client.OpenSessionAsync(id, Server.PeerId, "repo", await ConnectionAsync());
    internal ToolRegistration Tool(string name) => Tools.Single(tool => tool.Definition.Name.Name == name);
    internal ToolInvocationContext Context(ToolRegistration tool, string thread = "thread") => new(thread, "turn",
        Guid.NewGuid().ToString("N"), ToolInvocationAudience.Model, tool.Definition.Name, tool.Definition.Id, tool.Binding.Id, 1, DateTimeOffset.UtcNow);
    internal ValueTask<ToolExecutionResult> InvokeAsync(RemoteExecutionSession session, string tool, JsonObject args, string thread = "thread") =>
        session.InvokeAsync(session.Route, Tool(tool).Definition, RemoteToolContractHasher.Compute(Tool(tool).Definition), Context(Tool(tool), thread), args);
    internal async Task<string> StartTerminalAsync(RemoteExecutionSession session)
    {
        var arguments = TerminalArguments();
        var command = OperatingSystem.IsWindows()
            ? $"[IO.File]::WriteAllText('{session.Id}.pid', [string]$PID); Start-Sleep -Seconds 120"
            : $"echo $$ > {session.Id}.pid; sleep 120";
        arguments["command"] = command;
        var terminal = TerminalId(await InvokeAsync(session, "Exec", arguments));
        await WaitAsync(() => PluginLogFile.ReadLines(Path.Combine(Workspace.Path, session.Id + ".pid")).Length == 1, TimeSpan.FromSeconds(10));
        return terminal;
    }
    internal static JsonObject TerminalArguments() => new()
    {
        ["command"] = OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 120" : "sleep 120",
        ["runInBackground"] = true, ["interactive"] = true
    };
    internal static string TerminalId(ToolExecutionResult result)
    {
        Assert.True(result.Success, result.Error?.Message);
        return result.Content!.Split('\n').Single(line => line.StartsWith("Session ID:", StringComparison.Ordinal))["Session ID:".Length..].Trim();
    }
    internal async Task WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var stop = new CancellationTokenSource(timeout);
        while (!condition()) await Task.Delay(25, stop.Token);
    }
    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Server.DisposeAsync();
        Workspace.Dispose();
        Home.Dispose();
    }
}
