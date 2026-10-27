using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.RemoteTools;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

/// <summary>Background commands on a Remote Tool Host notify the Agent Thread that started them.</summary>
public sealed class RemoteTerminalNoticeTests : IAsyncLifetime
{
    private const string ThreadId = "agent-thread";
    private readonly TemporaryDirectory _home = new();
    private readonly TemporaryDirectory _remote = new();
    private readonly TemporaryDirectory _local = new();
    private readonly ConcurrentQueue<BackgroundTerminalEvent> _events = new();
    private RemoteToolHostTestServer _server = null!;
    private RemoteToolHostClient _client = null!;
    private IReadOnlyList<ToolRegistration> _registrations = [];
    private RemoteToolRoute _route = null!;

    public async Task InitializeAsync()
    {
        var storage = new RemoteToolHostStorage(_home.Path);
        RemoteToolHostTestHost.Setup(storage, new Dictionary<string, string> { ["repo"] = _remote.Path });
        _server = new(storage);
        _client = _server.CreateClient();
        _client.TerminalEvent += _events.Enqueue;
        _registrations = await RemoteToolHostTestHost.AgentRegistrationsAsync(_local.Path, _home.Path);
        _client.UpdateRemoteToolSnapshot(ThreadId, new EffectiveToolSnapshotBuilder().Build(_registrations, 1), "agent");
        _route = (await _client.ConnectAsync(ThreadId, _server.PeerId, "repo")).Route;
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        _home.Dispose();
        _remote.Dispose();
        _local.Dispose();
    }

    [Fact]
    public async Task CommandThatFailsAtStartup_NotifiesTheThreadThatStartedIt()
    {
        var sessionId = await StartAsync("Start-Sleep -Milliseconds 300; exit 3");

        var notice = await NextNoticeAsync();
        Assert.Equal(BackgroundTerminalEventTypes.CompletionNotice, notice.EventType);
        Assert.Equal(sessionId, notice.Terminal.SessionId);
        Assert.Equal(ThreadId, notice.Terminal.ThreadId);
        Assert.Equal("turn", notice.Terminal.TurnId);
        Assert.Equal(BackgroundTerminalStatus.Failed, notice.Terminal.Status);
        Assert.Equal(3, notice.Terminal.ExitCode);
        Assert.Equal(BackgroundTerminalStatus.Failed, Assert.Single(_client.ListTerminals(ThreadId)).Status);
    }

    [Fact]
    public async Task Disconnect_EndsRunningCommandsAsStopped()
    {
        var sessionId = await StartAsync("Start-Sleep -Seconds 60");

        await _client.DisconnectAsync(ThreadId);

        var notice = await NextNoticeAsync();
        Assert.Equal(sessionId, notice.Terminal.SessionId);
        Assert.Equal(BackgroundTerminalStatus.Killed, notice.Terminal.Status);
        Assert.Empty(_client.ListTerminals(ThreadId));
    }

    [Fact]
    public async Task LostSession_EndsRunningCommandsAsLost()
    {
        var sessionId = await StartAsync("Start-Sleep -Seconds 60");

        _server.DropConnections();

        var notice = await NextNoticeAsync();
        Assert.Equal(sessionId, notice.Terminal.SessionId);
        Assert.Equal(BackgroundTerminalStatus.Lost, notice.Terminal.Status);
        Assert.Equal(BackgroundTerminalStatus.Lost, Assert.Single(_client.ListTerminals(ThreadId)).Status);
    }

    [Fact]
    public async Task StopTerminal_StopsOnlyTheOwningThreadsCommand()
    {
        var sessionId = await StartAsync("Start-Sleep -Seconds 60");
        Assert.True(_client.TryForkRoute(ThreadId, "sibling-thread"));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _client.StopTerminalAsync("sibling-thread", sessionId).AsTask());
        var stopped = await _client.StopTerminalAsync(ThreadId, sessionId);

        Assert.Equal(BackgroundTerminalStatus.Killed, stopped.Status);
        Assert.Equal(ThreadId, stopped.ThreadId);
    }

    private async Task<string> StartAsync(string command)
    {
        var result = await InvokeAsync(ThreadId, "Exec", new()
        {
            ["command"] = command, ["shell"] = "pwsh", ["runInBackground"] = true, ["yieldTimeMs"] = 1
        });
        Assert.True(result.Success, result.Error?.Message);
        return result.Content!.Split('\n').Single(line => line.StartsWith("Session ID:", StringComparison.Ordinal))["Session ID:".Length..].Trim();
    }

    private async Task<BackgroundTerminalEvent> NextNoticeAsync()
    {
        await WaitUntilAsync(() => !_events.IsEmpty);
        Assert.True(_events.TryDequeue(out var notice));
        return notice;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition())
            await Task.Delay(50, deadline.Token);
    }

    private async Task<ToolExecutionResult> InvokeAsync(string threadId, string name, JsonObject arguments)
    {
        var tool = _registrations.Single(item => item.Definition.Name.Name == name);
        return await _client.InvokeAsync(_route, tool.Definition, RemoteToolContractHasher.Compute(tool.Definition),
            new(threadId, "turn", "call-" + Guid.NewGuid().ToString("N"), ToolInvocationAudience.Model,
                tool.Definition.Name, tool.Definition.Id, tool.Binding.Id, 1, DateTimeOffset.UtcNow, WorkspacePath: _local.Path), arguments);
    }
}
