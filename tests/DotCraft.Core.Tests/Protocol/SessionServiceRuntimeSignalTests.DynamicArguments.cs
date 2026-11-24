using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeDynamicCall_PublishesCompleteArgumentsWhileCallbackWaits(bool deferred)
    {
        using var client = new DynamicArgumentsChatClient(deferred);
        var callback = new WaitingDynamicRuntime();
        var recorder = new ToolInvocationRecorderRouter();
        await using var factory = CreateAgentFactory(client, [new WaitingDynamicToolSource(callback, deferred)],
            configureConfig: config => config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Simulated,
            toolDispatcher: new ToolDispatcher(recorder: recorder));
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        var run = CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Call the fixture tool")], ct: ct));
        var expected = deferred ? new JsonObject { ["initiation"] = "offer" } : new JsonObject();
        string liveItemId;
        try
        {
            if (await Task.WhenAny(callback.Invoked.Task, run).WaitAsync(ct) == run)
            {
                var outcome = (await service.GetThreadAsync(thread.Id, ct)).Turns.Single();
                Assert.Fail($"Turn settled before the callback was invoked: {outcome.Error ?? outcome.Status.ToString()}");
            }

            var callbackArgs = await callback.Invoked.Task.WaitAsync(ct);
            Assert.True(JsonNode.DeepEquals(expected, callbackArgs));
            using var observation = await service.ObserveThreadAsync(thread.Id, ThreadObservationView.Full, ct: ct);
            var turn = Assert.IsType<SessionTurn>(observation.Snapshot.Turn);
            Assert.Equal(TurnStatus.Running, turn.Status);
            var liveItem = Assert.Single(turn.Items, item => item.AsDynamicToolCall?.CallId == WaitingDynamicToolSource.CallId);
            liveItemId = liveItem.Id;
            var liveCall = Assert.IsType<DynamicToolCallPayload>(liveItem.Payload);
            Assert.Equal("inProgress", liveCall.Status);
            Assert.True(JsonNode.DeepEquals(expected, liveCall.Arguments),
                "The published item must carry the complete arguments while the callback waits.");
        }
        finally
        {
            callback.Release.TrySetResult();
        }

        var events = await run.WaitAsync(ct);
        var started = Assert.Single(events, evt => evt.EventType == SessionEventType.ItemStarted
            && evt.ItemPayload?.AsDynamicToolCall?.CallId == WaitingDynamicToolSource.CallId);
        var completed = Assert.Single(events, evt => evt.EventType == SessionEventType.ItemCompleted
            && evt.ItemPayload?.AsDynamicToolCall?.CallId == WaitingDynamicToolSource.CallId);
        Assert.Equal(liveItemId, started.ItemId);
        Assert.Equal(liveItemId, completed.ItemId);
        Assert.True(JsonNode.DeepEquals(expected, started.ItemPayload!.AsDynamicToolCall!.Arguments));
        Assert.Equal("completed", completed.ItemPayload!.AsDynamicToolCall!.Status);
        Assert.DoesNotContain(events, evt => evt.ToolCallArgumentsDeltaPayload?.CallId == WaitingDynamicToolSource.CallId);
    }

    private sealed class WaitingDynamicToolSource(WaitingDynamicRuntime callback, bool deferred) : IToolSource
    {
        public const string CallId = "dynamic-arguments-call";
        public const string ProviderName = "fixture__ConfirmAction";
        private static readonly ToolName Name = new("fixture", "ConfirmAction");

        public string SourceId => "runtime-dynamic-arguments-test";

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context, CancellationToken cancellationToken = default)
        {
            var id = new ToolDefinitionId(ToolSourceKind.RuntimeDynamic, SourceId, new SourceToolId(Name.ToString()));
            var definition = new ToolDefinition(id, Name, "Confirm an action.",
                JsonSerializer.SerializeToElement(new { type = "object" }),
                provenance: new ToolProvenance(ToolSourceKind.RuntimeDynamic, SourceId, "thread"));
            var binding = new ToolRuntimeBinding(new RuntimeBindingId(SourceId), id, callback,
                ToolBindingLeases.AlwaysAvailable, SourceId, context.Revision);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([
                new ToolRegistration(definition, binding, ToolProjectionShape.DynamicLifecycle,
                    deferred ? ToolExposure.Deferred : ToolExposure.Direct, ToolInvocationAudience.Model,
                    deferred ? new DeferredToolDescriptor("fixture", "Confirm an action.") : null)
            ]);
        }
    }

    private sealed class WaitingDynamicRuntime : IToolRuntime
    {
        public TaskCompletionSource<JsonObject> Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            Invoked.SetResult(arguments);
            await Release.Task.WaitAsync(cancellationToken);
            return ToolExecutionResult.Succeeded("Confirmed");
        }
    }

    private sealed class DynamicArgumentsChatClient(bool deferred) : IChatClient
    {
        private int _requests;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = Interlocked.Increment(ref _requests);
            var toolName = WaitingDynamicToolSource.ProviderName;
            if (deferred && request == 1)
            {
                Assert.DoesNotContain(options!.Tools!, tool => tool.Name == toolName);
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(
                    "fixture-search", "SearchTools", new Dictionary<string, object?> { ["query"] = "ConfirmAction" })]);
            }
            else if (request == (deferred ? 2 : 1))
            {
                Assert.Contains(options!.Tools!, tool => tool.Name == toolName);
                var arguments = deferred ? new Dictionary<string, object?> { ["initiation"] = "offer" } : [];
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new ToolCallArgumentsDeltaContent
                {
                    ToolCallIndex = 0,
                    ToolName = toolName,
                    CallId = WaitingDynamicToolSource.CallId,
                    ArgumentsDelta = JsonSerializer.Serialize(arguments)
                }]);
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent(WaitingDynamicToolSource.CallId, toolName, arguments)]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Done")]);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
