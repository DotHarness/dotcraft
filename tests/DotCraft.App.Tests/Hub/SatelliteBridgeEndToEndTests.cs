using System.Text.Json.Nodes;
using DotCraft.Hub;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class SatelliteBridgeEndToEndTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSatelliteBridge_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Bridge_EndToEnd_JoinServeConnectInvokeDisconnect()
    {
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile);
        var approvals = new CountingApprovalService();
        await using var client = new RemoteToolHostClient(scenario.Directory, approvals);
        var registrations = await scenario.AgentRegistrationsAsync();
        client.UpdateRemoteToolDefinitions([.. registrations.Select(item => item.Definition)]);

        var connected = await client.ConnectAsync("thread", scenario.PeerId, scenario.WorkspaceId);
        Assert.Contains("ReadFile", connected.MatchedTools);
        Assert.Equal(scenario.PeerId, connected.Route.HostId);

        var read = registrations.Single(item => item.Definition.Name.Name == "ReadFile");
        var readResult = await InvokeAsync(client, connected.Route, read, "read", new JsonObject
        {
            ["path"] = "remote.txt"
        });
        Assert.True(readResult.Success, readResult.Error?.Message);
        Assert.Contains("remote-value", readResult.Content, StringComparison.Ordinal);

        var write = registrations.Single(item => item.Definition.Name.Name == "WriteFile");
        var writeResult = await InvokeAsync(client, connected.Route, write, "write", new JsonObject
        {
            ["path"] = "approved.txt",
            ["content"] = "approved"
        });
        Assert.True(writeResult.Success, writeResult.Error?.Message);
        Assert.Equal(1, approvals.RequestCount);
        Assert.Equal(
            "approved",
            await File.ReadAllTextAsync(Path.Combine(scenario.WorkspacePath, "approved.txt")));

        scenario.DenyTool("WriteFile");
        var denied = await InvokeAsync(client, connected.Route, write, "denied", new JsonObject
        {
            ["path"] = "denied.txt",
            ["content"] = "denied"
        });
        Assert.False(denied.Success);
        Assert.Equal(RemoteToolErrorCodes.RemotePolicyDenied, denied.Error?.Code);
        Assert.Equal(1, approvals.RequestCount);

        var spilled = await InvokeAsync(client, connected.Route, read, "spill", new JsonObject
        {
            ["path"] = "large.txt"
        });
        var artifactPath = spilled.Meta!.Value.GetProperty("remoteArtifactPath").GetString()!;
        Assert.StartsWith(scenario.ArtifactsRootPath, artifactPath, StringComparison.Ordinal);
        var reread = await InvokeAsync(client, connected.Route, read, "reread", new JsonObject
        {
            ["path"] = artifactPath,
            ["limit"] = 3
        });
        Assert.True(reread.Success, reread.Error?.Message);
        Assert.Contains("line-000000", reread.Content, StringComparison.Ordinal);

        Assert.True((await client.DisconnectAsync("thread")).Disconnected);

        var revoked = await scenario.Hub.DeleteAsync($"/v1/satellites/{scenario.PeerId}");
        revoked.EnsureSuccessStatusCode();
        await scenario.Running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites"));
    }

    [Fact]
    public async Task Bridge_AgentSeesSatelliteOffline_WhenHostProcessStops()
    {
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile);
        await using var client = new RemoteToolHostClient(scenario.Directory, new CountingApprovalService());

        await scenario.Runtime.StopAsync();
        await WaitUntilAsync(async () =>
            (await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites")).Single().Online == false);

        var error = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await client.ConnectAsync("thread", scenario.PeerId, scenario.WorkspaceId));

        Assert.Equal(RemoteToolErrorCodes.HostOffline, error.Code);
        var listed = Assert.Single((await client.ListAsync("thread")).Hosts);
        Assert.False(listed.Online);
        Assert.Equal(RemoteToolErrorCodes.SatelliteOffline, listed.ErrorCode);
    }

    [Fact]
    public async Task Bridge_LeaseSurvivesDataConnectionDrop_UntilTtl()
    {
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile);
        var recording = new RecordingDirectory(scenario.Directory);
        await using var owner = new RemoteToolHostClient(recording, new CountingApprovalService());
        await owner.ConnectAsync("thread", scenario.PeerId, scenario.WorkspaceId);

        await recording.DropLastConnectionAsync();

        await using var second = new RemoteToolHostClient(scenario.Directory, new CountingApprovalService());
        var busy = await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await second.ConnectAsync("other-thread", scenario.PeerId, scenario.WorkspaceId));
        Assert.Equal(RemoteToolErrorCodes.WorkspaceBusy, busy.Code);
    }

    public void Dispose()
    {
        try { Directory.Delete(_userProfile, recursive: true); }
        catch (Exception) { }
    }

    private static ValueTask<ToolExecutionResult> InvokeAsync(
        RemoteToolHostClient client,
        RemoteToolRoute route,
        ToolRegistration registration,
        string callId,
        JsonObject arguments) => client.InvokeAsync(
            route,
            registration.Definition,
            RemoteToolContractHasher.Compute(registration.Definition),
            new ToolInvocationContext(
                "thread",
                "turn",
                callId,
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                1,
                DateTimeOffset.UtcNow),
            arguments);

    internal static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(100, timeout.Token);
        }
    }

    private sealed class RecordingDirectory(IRemoteToolHostDirectory inner) : IRemoteToolHostDirectory
    {
        private RemoteToolHostConnection? _last;

        public string? CurrentEndpoint => inner.CurrentEndpoint;

        public ValueTask<IReadOnlyList<RemoteToolHostDescriptor>> ListAsync(CancellationToken cancellationToken) =>
            inner.ListAsync(cancellationToken);

        public async ValueTask<RemoteToolHostConnection> ConnectAsync(
            string hostId,
            CancellationToken cancellationToken)
        {
            _last = await inner.ConnectAsync(hostId, cancellationToken);
            return _last;
        }

        public async Task DropLastConnectionAsync()
        {
            if (_last is not null)
                await _last.DisposeAsync();
        }
    }
}

internal sealed class CountingApprovalService : DotCraft.Security.IApprovalService
{
    public int RequestCount { get; private set; }

    public Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        DotCraft.Security.ApprovalContext? context = null) => Task.FromResult(true);

    public Task<bool> RequestShellApprovalAsync(
        string command,
        string? workingDir,
        DotCraft.Security.ApprovalContext? context = null) => Task.FromResult(true);

    public Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        DotCraft.Security.ApprovalContext? context = null)
    {
        RequestCount++;
        return Task.FromResult(true);
    }
}
