using DotCraft.Mcp;
using Microsoft.Extensions.AI;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Mcp;

public sealed class McpClientManagerStaleSessionRecoveryTests
{
    [Fact]
    public async Task RefreshToolAfterStaleSessionAsync_ReconnectsAndReplacesTarget()
    {
        var oldTool = new FakeFunction("demo_tool", "old");
        var newTool = new FakeFunction("demo_tool", "new");
        var oldClient = new FakeClient();
        var newClient = new FakeClient();
        var factory = new FakeConnectionFactory(
            _ => Task.FromResult(new McpConnectionResult(oldClient, [oldTool])),
            _ => Task.FromResult(new McpConnectionResult(newClient, [newTool])));
        await using var manager = new McpClientManager(factory.ConnectAsync);

        await manager.ConnectAsync([StreamableServer()]);
        await manager.WaitForStartupCompletionAsync();
        var initialTarget = await manager.TryGetInvocationTargetAsync("demo-server", "demo_tool");
        Assert.NotNull(initialTarget);

        var refreshedTarget = await manager.RefreshToolAfterStaleSessionAsync(
            "demo-server",
            "demo_tool",
            initialTarget.Generation);
        Assert.NotNull(refreshedTarget);

        Assert.Same(newTool, refreshedTarget.Tool);
        Assert.True(refreshedTarget.Generation > initialTarget.Generation);
        Assert.Equal(2, factory.CallCount);
        Assert.True(oldClient.Disposed);
        Assert.False(newClient.Disposed);
        Assert.Single(manager.Tools);
        Assert.Equal("demo-server", manager.ToolServerMap["demo_tool"]);
    }

    [Fact]
    public async Task RefreshToolAfterStaleSessionAsync_ObservedGenerationAlreadyChanged_DoesNotReconnect()
    {
        var tool = new FakeFunction("demo_tool", "ok");
        var factory = new FakeConnectionFactory(
            _ => Task.FromResult(new McpConnectionResult(new FakeClient(), [tool])));
        await using var manager = new McpClientManager(factory.ConnectAsync);

        await manager.ConnectAsync([StreamableServer()]);
        await manager.WaitForStartupCompletionAsync();
        var currentTarget = await manager.TryGetInvocationTargetAsync("demo-server", "demo_tool");
        Assert.NotNull(currentTarget);

        var returnedTarget = await manager.RefreshToolAfterStaleSessionAsync(
            "demo-server",
            "demo_tool",
            currentTarget.Generation - 1);
        Assert.NotNull(returnedTarget);

        Assert.Same(tool, returnedTarget.Tool);
        Assert.Equal(currentTarget.Generation, returnedTarget.Generation);
        Assert.Equal(1, factory.CallCount);
    }

    [Fact]
    public async Task RefreshToolAfterStaleSessionAsync_ConcurrentCallsShareOneReconnect()
    {
        var oldTool = new FakeFunction("demo_tool", "old");
        var newTool = new FakeFunction("demo_tool", "new");
        var refreshGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeConnectionFactory(
            _ => Task.FromResult(new McpConnectionResult(new FakeClient(), [oldTool])),
            async ct =>
            {
                await refreshGate.Task.WaitAsync(ct);
                return new McpConnectionResult(new FakeClient(), [newTool]);
            });
        await using var manager = new McpClientManager(factory.ConnectAsync);

        await manager.ConnectAsync([StreamableServer()]);
        await manager.WaitForStartupCompletionAsync();
        var initialTarget = await manager.TryGetInvocationTargetAsync("demo-server", "demo_tool");
        Assert.NotNull(initialTarget);

        var firstRefresh = manager.RefreshToolAfterStaleSessionAsync("demo-server", "demo_tool", initialTarget.Generation);
        var secondRefresh = manager.RefreshToolAfterStaleSessionAsync("demo-server", "demo_tool", initialTarget.Generation);
        await WaitUntilAsync(() => factory.CallCount == 2);
        refreshGate.SetResult();

        var targets = await Task.WhenAll(firstRefresh, secondRefresh);

        Assert.All(targets, target =>
        {
            Assert.NotNull(target);
            Assert.Same(newTool, target.Tool);
        });
        Assert.Equal(2, factory.CallCount); // one startup connect + one shared stale-session reconnect
    }

    private static McpServerConfig StreamableServer() =>
        new()
        {
            Name = "demo-server",
            Enabled = true,
            Transport = "streamableHttp",
            Url = "http://localhost/mcp",
            StartupTimeoutSec = 2
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("Condition was not met before the deadline.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeConnectionFactory
    {
        private readonly Queue<Func<CancellationToken, Task<McpConnectionResult>>> _steps;
        private readonly object _gate = new();

        public FakeConnectionFactory(params Func<CancellationToken, Task<McpConnectionResult>>[] steps)
        {
            _steps = new Queue<Func<CancellationToken, Task<McpConnectionResult>>>(steps);
        }

        public int CallCount { get; private set; }

        public Task<McpConnectionResult> ConnectAsync(McpServerConfig config, CancellationToken cancellationToken)
        {
            Func<CancellationToken, Task<McpConnectionResult>> next;
            lock (_gate)
            {
                CallCount++;
                next = _steps.Dequeue();
            }

            return next(cancellationToken);
        }
    }

    private sealed class FakeClient : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeFunction(string name, object? result) : AIFunction
    {
        public override string Name { get; } = name;

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }
}
