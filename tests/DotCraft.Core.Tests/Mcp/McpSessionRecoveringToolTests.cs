using DotCraft.Mcp;
using Microsoft.Extensions.AI;
using System.Net;

namespace DotCraft.Tests.Mcp;

public sealed class McpSessionRecoveringToolTests
{
    [Fact]
    public async Task InvokeAsync_StaleStreamableHttpSession_RefreshesAndRetriesOnce()
    {
        var staleException = CreateStaleSessionException();
        var oldTool = new ScriptedFunction("demo_tool", staleException);
        var newTool = new ScriptedFunction("demo_tool", "ok");
        var coordinator = new FakeCoordinator
        {
            CurrentTarget = Target(oldTool, generation: 7, transport: "streamableHttp"),
            RefreshTarget = Target(newTool, generation: 8, transport: "streamableHttp")
        };
        var wrapper = new McpSessionRecoveringTool(coordinator, "demo-server", oldTool);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        Assert.Equal("ok", result);
        Assert.Equal(1, oldTool.CallCount);
        Assert.Equal(1, newTool.CallCount);
        Assert.Equal(1, coordinator.RefreshCount);
        Assert.Equal(7, coordinator.ObservedGeneration);
    }

    [Fact]
    public async Task InvokeAsync_Bare404WithKnownSession_RefreshesAndRetriesOnce()
    {
        var staleException = new HttpRequestException(
            "Response status code does not indicate success: 404 (Not Found).",
            inner: null,
            HttpStatusCode.NotFound);
        var oldTool = new ScriptedFunction("demo_tool", staleException);
        var newTool = new ScriptedFunction("demo_tool", "ok");
        var coordinator = new FakeCoordinator
        {
            CurrentTarget = Target(oldTool, generation: 7, transport: "streamableHttp", hasSessionId: true),
            RefreshTarget = Target(newTool, generation: 8, transport: "streamableHttp", hasSessionId: true)
        };
        var wrapper = new McpSessionRecoveringTool(coordinator, "demo-server", oldTool);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments());

        Assert.Equal("ok", result);
        Assert.Equal(1, coordinator.RefreshCount);
    }

    [Fact]
    public async Task InvokeAsync_NonStaleFailure_DoesNotRefresh()
    {
        var failure = new InvalidOperationException("boom");
        var tool = new ScriptedFunction("demo_tool", failure);
        var coordinator = new FakeCoordinator
        {
            CurrentTarget = Target(tool, generation: 3, transport: "streamableHttp"),
            RefreshTarget = Target(new ScriptedFunction("demo_tool", "unused"), generation: 4, transport: "streamableHttp")
        };
        var wrapper = new McpSessionRecoveringTool(coordinator, "demo-server", tool);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await wrapper.InvokeAsync(new AIFunctionArguments()));

        Assert.Same(failure, thrown);
        Assert.Equal(0, coordinator.RefreshCount);
    }

    [Fact]
    public async Task InvokeAsync_StaleFailureOnNonStreamableTransport_DoesNotRefresh()
    {
        var staleException = CreateStaleSessionException();
        var tool = new ScriptedFunction("demo_tool", staleException);
        var coordinator = new FakeCoordinator
        {
            CurrentTarget = Target(tool, generation: 5, transport: "stdio"),
            RefreshTarget = Target(new ScriptedFunction("demo_tool", "unused"), generation: 6, transport: "stdio")
        };
        var wrapper = new McpSessionRecoveringTool(coordinator, "demo-server", tool);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await wrapper.InvokeAsync(new AIFunctionArguments()));

        Assert.Same(staleException, thrown);
        Assert.Equal(0, coordinator.RefreshCount);
    }

    [Fact]
    public async Task InvokeAsync_ToolTimeout_ThrowsTimeoutExceptionWithoutRefresh()
    {
        var tool = new DelayedFunction("demo_tool");
        var coordinator = new FakeCoordinator
        {
            CurrentTarget = Target(
                tool,
                generation: 6,
                transport: "streamableHttp",
                hasSessionId: true,
                toolTimeout: TimeSpan.FromMilliseconds(25))
        };
        var wrapper = new McpSessionRecoveringTool(coordinator, "demo-server", tool);

        var thrown = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await wrapper.InvokeAsync(new AIFunctionArguments()));

        Assert.Contains("timed out", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, coordinator.RefreshCount);
    }

    [Fact]
    public async Task InvokeAsync_RetryFailure_DoesNotRefreshAgain()
    {
        var staleException = CreateStaleSessionException();
        var retryFailure = new InvalidOperationException("retry failed");
        var oldTool = new ScriptedFunction("demo_tool", staleException);
        var newTool = new ScriptedFunction("demo_tool", retryFailure);
        var coordinator = new FakeCoordinator
        {
            CurrentTarget = Target(oldTool, generation: 10, transport: "streamableHttp"),
            RefreshTarget = Target(newTool, generation: 11, transport: "streamableHttp")
        };
        var wrapper = new McpSessionRecoveringTool(coordinator, "demo-server", oldTool);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await wrapper.InvokeAsync(new AIFunctionArguments()));

        Assert.Same(retryFailure, thrown);
        Assert.Equal(1, oldTool.CallCount);
        Assert.Equal(1, newTool.CallCount);
        Assert.Equal(1, coordinator.RefreshCount);
    }

    private static HttpRequestException CreateStaleSessionException() =>
        new(
            "Response status code does not indicate success: 404 (Not Found). Response body: {\"error\":{\"code\":-32001,\"message\":\"Session not found\"}}",
            inner: null,
            HttpStatusCode.NotFound);

    private static McpToolInvocationTarget Target(
        AIFunction tool,
        long generation,
        string transport,
        bool hasSessionId = false,
        TimeSpan? toolTimeout = null) =>
        new("demo-server", tool.Name, transport, generation, tool, hasSessionId, toolTimeout);

    private sealed class FakeCoordinator : IMcpToolInvocationCoordinator
    {
        public McpToolInvocationTarget? CurrentTarget { get; init; }
        public McpToolInvocationTarget? RefreshTarget { get; init; }
        public int RefreshCount { get; private set; }
        public long? ObservedGeneration { get; private set; }

        public Task<McpToolInvocationTarget?> TryGetInvocationTargetAsync(
            string serverName,
            string toolName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentTarget);

        public Task<McpToolInvocationTarget?> RefreshToolAfterStaleSessionAsync(
            string serverName,
            string toolName,
            long observedGeneration,
            CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            ObservedGeneration = observedGeneration;
            return Task.FromResult(RefreshTarget);
        }
    }

    private sealed class DelayedFunction(string name) : AIFunction
    {
        public override string Name { get; } = name;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }

    private sealed class ScriptedFunction : AIFunction
    {
        private readonly Queue<object?> _steps;

        public ScriptedFunction(string name, params object?[] steps)
        {
            Name = name;
            _steps = new Queue<object?>(steps);
        }

        public override string Name { get; }
        public int CallCount { get; private set; }

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var next = _steps.Count == 0 ? null : _steps.Dequeue();
            if (next is Exception exception)
                throw exception;

            return ValueTask.FromResult(next);
        }
    }
}
