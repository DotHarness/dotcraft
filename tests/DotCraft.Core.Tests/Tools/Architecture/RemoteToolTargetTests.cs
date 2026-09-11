using System.Text.Json.Nodes;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools.Architecture;

public sealed partial class RemoteToolHostCoreTests
{
    [Fact]
    public async Task Explicit_local_call_keeps_remote_route_and_strips_routing_argument()
    {
        var definition = Definition("test", """{"type":"object","properties":{"path":{"type":"string"}}}""");
        var local = new TargetRecordingRuntime();
        var client = new FakeRemoteClient();
        client.SetRoute("thread-1");
        var runtime = new RemoteRoutableToolRuntime(definition, local, client);
        var arguments = new JsonObject { ["target"] = "local", ["path"] = "same.txt" };
        var result = await runtime.InvokeAsync(Invocation(definition), arguments);
        Assert.True(result.Success);
        Assert.False(local.Arguments!.ContainsKey("target"));
        Assert.Equal("same.txt", local.Arguments["path"]!.GetValue<string>());
        Assert.True(arguments.ContainsKey("target"));
        Assert.True(client.TryGetRoute("thread-1", out _));
        Assert.Equal(0, client.RemoteCalls);
        Assert.Equal("local", result.Meta!.Value.GetProperty("executionTarget").GetString());
    }

    [Fact]
    public async Task Captured_remote_route_does_not_fall_back_after_disconnect()
    {
        var definition = Definition("test", """{"type":"object"}""");
        var local = new TargetRecordingRuntime();
        var client = new FakeRemoteClient();
        client.SetRoute("thread-1");
        var runtime = new RemoteRoutableToolRuntime(definition, local, client);
        var context = runtime.Prepare(Invocation(definition), new JsonObject());
        await client.DisconnectAsync("thread-1");
        var result = await runtime.InvokeAsync(context, new JsonObject());
        Assert.False(result.Success);
        Assert.Equal(RemoteToolErrorCodes.LeaseLost, result.Error!.Code);
        Assert.Null(local.Arguments);
    }

    [Fact]
    public async Task Explicit_remote_without_connection_fails_before_runtime()
    {
        var definition = Definition("test", """{"type":"object"}""");
        var local = new TargetRecordingRuntime();
        var runtime = new RemoteRoutableToolRuntime(definition, local, new FakeRemoteClient());
        await Assert.ThrowsAsync<RemoteToolHostException>(async () =>
            await runtime.InvokeAsync(Invocation(definition), new JsonObject { ["target"] = "remote" }));
        Assert.Null(local.Arguments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_target_is_rejected_without_local_or_remote_execution(bool connected)
    {
        var registration = Registration(Definition("read", """{"type":"object"}"""));
        var local = Assert.IsType<RecordingRuntime>(registration.Binding.Runtime);
        var client = new FakeRemoteClient();
        if (connected) client.SetRoute("thread-1");
        var snapshot = new EffectiveToolSnapshotBuilder().Build(
            RemoteToolRegistrationRouter.Wrap([registration], client), 1);

        var result = await new ToolDispatcher().DispatchAsync(snapshot, registration.Definition.Name,
            new JsonObject { ["target"] = "remtoe" },
            new ToolInvocationRequest("thread-1", "turn-1", "call-1", ToolInvocationAudience.Model));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error?.Code);
        Assert.Equal(0, local.Calls);
        Assert.Equal(0, client.RemoteCalls);
    }

    private sealed class TargetRecordingRuntime : IToolRuntime
    {
        internal JsonObject? Arguments { get; private set; }
        public ValueTask<ToolExecutionResult> InvokeAsync(ToolInvocationContext context, JsonObject arguments, CancellationToken ct = default)
        {
            Arguments = arguments;
            return ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
        }
    }
}
