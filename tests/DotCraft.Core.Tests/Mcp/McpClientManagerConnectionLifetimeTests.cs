using System.Collections.Concurrent;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpClientManagerConnectionLifetimeTests
{
    [Fact]
    public async Task Reconcile_AddRemoveAndReplace_PreservesUnaffectedSession()
    {
        var connections = new ConcurrentDictionary<string, List<Client>>(StringComparer.Ordinal);
        await using var manager = new McpClientManager((config, _) =>
        {
            var client = new Client();
            connections.AddOrUpdate(config.Name, _ => [client], (_, clients) => [.. clients, client]);
            return Task.FromResult(new McpConnectionResult(client, []));
        });
        var first = Server("first");
        var second = Server("second");
        await manager.ConnectAsync([first, second]);
        await manager.WaitForStartupCompletionAsync();
        var firstGeneration = await manager.GetGenerationAsync(first.Name);
        var oldSecond = Assert.Single(connections[second.Name]);

        second.Url += "/changed";
        await manager.ConnectAsync([first, second]);
        await manager.WaitForStartupCompletionAsync();

        Assert.Equal(firstGeneration, await manager.GetGenerationAsync(first.Name));
        Assert.False(Assert.Single(connections[first.Name]).Disposed);
        Assert.True(oldSecond.Disposed);
        Assert.Equal(2, connections[second.Name].Count);

        await manager.ConnectAsync([first, Server("third")]);
        await manager.WaitForStartupCompletionAsync();
        Assert.Equal(firstGeneration, await manager.GetGenerationAsync(first.Name));
        Assert.True(connections[second.Name][1].Disposed);
        Assert.Single(connections["third"]);
        Assert.Null(await manager.GetGenerationAsync(second.Name));
    }

    [Fact]
    public async Task Reconcile_PolicyAndReadyStartupTimeoutChanges_KeepConnection()
    {
        var client = new Client();
        var calls = 0;
        await using var manager = new McpClientManager((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new McpConnectionResult(client, []));
        });
        var config = Server("stateful");
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
        var generation = await manager.GetGenerationAsync(config.Name);

        config.ToolTimeoutSec = 17;
        config.StartupTimeoutSec = 42;
        config.ToolApprovalMode = McpToolApprovalMode.Prompt;
        await manager.ConnectAsync([config]);

        Assert.Equal(1, calls);
        Assert.Equal(generation, await manager.GetGenerationAsync(config.Name));
        Assert.False(client.Disposed);
    }

    [Fact]
    public async Task Dispose_CancelsStartup_AndAwaitsLateClientDisposal()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<McpConnectionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client();
        var manager = new McpClientManager(async (_, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            started.SetResult();
            return await result.Task;
        });
        await manager.ConnectAsync([Server("late")]);
        await started.Task;

        var firstDispose = manager.DisposeAsync().AsTask();
        var secondDispose = manager.DisposeAsync().AsTask();
        await cancelled.Task;
        Assert.False(firstDispose.IsCompleted);

        result.SetResult(new McpConnectionResult(client, []));
        await Task.WhenAll(firstDispose, secondDispose);

        Assert.True(client.Disposed);
    }

    [Fact]
    public async Task Reconcile_PendingStartupTimeoutChange_CancelsOldAttempt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new Client();
        var calls = 0;
        await using var manager = new McpClientManager(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    cancelled.TrySetResult();
                }
            }
            return new McpConnectionResult(ready, []);
        });
        var config = Server("pending");
        await manager.ConnectAsync([config]);
        await started.Task;
        config.StartupTimeoutSec = 10;
        await manager.ConnectAsync([config]);
        await cancelled.Task;
        await manager.WaitForStartupCompletionAsync();

        Assert.Equal(2, calls);
        Assert.Equal("ready", Assert.Single(await manager.ListStatusesAsync()).StartupState);
        Assert.False(ready.Disposed);
    }

    [Fact]
    public async Task Dispose_AwaitsReconciliationCleanupAlreadyInProgress()
    {
        var disposing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldClient = new Client(async () =>
        {
            disposing.SetResult();
            await released.Task;
        });
        var calls = 0;
        var manager = new McpClientManager((_, _) => Task.FromResult(
            new McpConnectionResult(Interlocked.Increment(ref calls) == 1 ? oldClient : new Client(), [])));
        var config = Server("retiring");
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
        config.Url += "/replacement";
        var reconcile = manager.ConnectAsync([config]);
        await disposing.Task;

        var shutdown = manager.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        released.SetResult();
        await Task.WhenAll(reconcile, shutdown);

        Assert.True(oldClient.Disposed);
    }

    [Fact]
    public async Task Dispose_CancelsPendingProtocolCall_AndDrainsItsLease()
    {
        var handler = new WaitingInputHandler();
        await using var manager = new McpClientManager(elicitationHandler: handler);
        var config = StdioServer();
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
        var context = new ToolInvocationContext("thread", null, "call", ToolInvocationAudience.Host,
            new ToolName("inputs", "choose"),
            new ToolDefinitionId(ToolSourceKind.Mcp, "inputs", new SourceToolId("choose")),
            new RuntimeBindingId("binding"), 1, DateTimeOffset.UtcNow);
        var call = manager.CallToolAsync(config.Name, "choose",
            new Dictionary<string, object?> { ["owner"] = "thread" }, context: context);
        await handler.Requested.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(handler.CancellationToken.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<Exception>(() => call);
        Assert.Null(await manager.GetGenerationAsync(config.Name));
    }

    [Fact]
    public async Task Reconcile_ClosedTransport_ReplacesTheConnection()
    {
        await using var manager = new McpClientManager();
        var config = StdioServer();
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
        var generation = await manager.GetGenerationAsync(config.Name);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            manager.CallToolAsync(config.Name, "disconnect").WaitAsync(TimeSpan.FromSeconds(15)));

        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();

        Assert.NotEqual(generation, await manager.GetGenerationAsync(config.Name));
        Assert.Equal("ready", Assert.Single(await manager.ListStatusesAsync()).StartupState);
    }

    private static McpServerConfig StdioServer() => new()
    {
        Name = "inputs", Command = "dotnet", Arguments = [McpFixtureProcess.AssemblyPath, "stdio"], StartupTimeoutSec = 15
    };

    private sealed class WaitingInputHandler : IMcpElicitationHandler
    {
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken CancellationToken { get; private set; }

        public async ValueTask<ElicitResult> HandleAsync(ToolInvocationContext context, string serverName,
            ElicitRequestParams request, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            Requested.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new ElicitResult { Action = "decline" };
        }
    }

    private static McpServerConfig Server(string name) => new()
    {
        Name = name, Transport = "http", Url = $"https://{name}.example.test/mcp", StartupTimeoutSec = 5
    };

    private sealed class Client(Func<ValueTask>? dispose = null) : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public async ValueTask DisposeAsync()
        {
            if (dispose is not null)
                await dispose();
            Disposed = true;
        }
    }
}
