using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Fact]
    public async Task CompletedTurn_ChecksOffOnlyTheFinalInProgressTaskIfTheRestAreDone()
    {
        using var chatClient = new TodoWriteChatClient(WriteTodos(
            ("Read the code", PlanTodoStatus.Completed),
            ("Change it", PlanTodoStatus.InProgress)));
        var planStore = new PlanStore(_tempDir);
        await using var agentFactory = CreateAgentFactory(chatClient, planStore: planStore);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("do the work")]))
            .WaitAsync(TimeSpan.FromSeconds(10));

        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.NotNull(plan);
        Assert.Equal([PlanTodoStatus.Completed, PlanTodoStatus.Completed], plan.Todos.Select(todo => todo.Status));
    }

    [Fact]
    public async Task CompletedTurn_LeavesUnfinishedEarlierTasksOpenAfterWrapUp()
    {
        using var chatClient = new TodoWriteChatClient(
            WriteTodos(
                ("Read the code", PlanTodoStatus.Completed),
                ("Run tests", PlanTodoStatus.Pending),
                ("Change it", PlanTodoStatus.InProgress)),
            WrapUp());
        var planStore = new PlanStore(_tempDir);
        var recorder = new ToolInvocationRecorderRouter();
        await using var agentFactory = CreateAgentFactory(
            chatClient, [new WrapUpToolSource()],
            toolDispatcher: new ToolDispatcher(recorder: recorder), planStore: planStore);
        var service = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("do the work")]))
            .WaitAsync(TimeSpan.FromSeconds(10));

        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.Equal(
            [PlanTodoStatus.Completed, PlanTodoStatus.Pending, PlanTodoStatus.InProgress],
            plan!.Todos.Select(todo => todo.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedTurn_LeavesEligibleFinalTaskOpenWhenLatestSuccessfulWrapUpIsBlocking(bool writeAfterWrapUp)
    {
        var write = WriteTodos(("Read the code", PlanTodoStatus.Completed), ("Run tests", PlanTodoStatus.InProgress));
        var blocked = WrapUp("call-blocked", blocked: true);
        using var chatClient = new TodoWriteChatClient(writeAfterWrapUp
            ? [WrapUp(), blocked, write]
            : [write, WrapUp(), blocked]);
        var planStore = new PlanStore(_tempDir);
        var recorder = new ToolInvocationRecorderRouter();
        await using var agentFactory = CreateAgentFactory(
            chatClient, [new WrapUpToolSource()],
            toolDispatcher: new ToolDispatcher(recorder: recorder), planStore: planStore);
        var service = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("do the work")], ct: timeout.Token))
            .WaitAsync(timeout.Token);

        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.Equal([PlanTodoStatus.Completed, PlanTodoStatus.InProgress], plan!.Todos.Select(todo => todo.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedTurn_WithoutAValidTodoWrite_LeavesEarlierTasksOpen(bool rejectsWrite)
    {
        var planStore = new PlanStore(_tempDir);
        using var chatClient = new TodoWriteChatClient(rejectsWrite ? [WriteTodos((" ", PlanTodoStatus.Pending))] : []);
        await using var agentFactory = CreateAgentFactory(chatClient, planStore: planStore);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        await planStore.SaveStructuredPlanAsync(thread.Id, new StructuredPlan
        {
            Todos = [new PlanTodo { Id = "todo-1", Content = "Earlier step", Status = PlanTodoStatus.InProgress }]
        });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("just answer")]))
            .WaitAsync(TimeSpan.FromSeconds(10));

        if (rejectsWrite)
        {
            var updated = await service.GetThreadAsync(thread.Id);
            var result = Assert.Single(Assert.Single(updated.Turns).Items
                .Select(item => item.Payload).OfType<ToolResultPayload>());
            Assert.False(result.Success);
            Assert.Equal(ToolErrorCodes.InputInvalid, result.ErrorCode);
        }
        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.Equal(PlanTodoStatus.InProgress, Assert.Single(plan!.Todos).Status);
    }

    [Fact]
    public async Task CompletedTurn_ChecksOffTasksWhileAQueuedSourceControlUpdateCanFinish()
    {
        var beforeReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var chatClient = new TodoWriteChatClient(WriteTodos(("Finish work", PlanTodoStatus.InProgress)))
        {
            BeforeReply = ct =>
            {
                beforeReply.TrySetResult();
                return allowReply.Task.WaitAsync(ct);
            }
        };
        var planStore = new PlanStore(_tempDir);
        await using var agentFactory = CreateAgentFactory(chatClient, planStore: planStore);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("do the work")], ct: timeout.Token));
        await beforeReply.Task.WaitAsync(timeout.Token);
        var update = service.UpdateThreadSourceControlTargetAsync(thread.Id,
            new ThreadSourceControlTarget { Provider = "perforce", Changelist = "123" }, timeout.Token);
        Assert.False(update.IsCompleted);
        allowReply.TrySetResult();

        var updated = await update.WaitAsync(timeout.Token);
        Assert.Equal("123", updated.Metadata[ThreadSourceControlMetadata.PerforceChangelistKey]);
        Assert.Contains(await execution.WaitAsync(timeout.Token), evt => evt.EventType == SessionEventType.TurnCompleted);
        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.Equal(PlanTodoStatus.Completed, Assert.Single(plan!.Todos).Status);
    }

    private static FunctionCallContent WrapUp(string callId = "call-wrap", bool blocked = false) =>
        new(callId, ProviderToolProjector.Project([WrapUpToolSource.Name])[WrapUpToolSource.Name],
            blocked
                ? new Dictionary<string, object?>
                {
                    ["recap"] = "Ready to run the tests.",
                    ["ask"] = "Run the tests?",
                    ["actions"] = new[] { new { label = "Run tests" } },
                    ["blocked"] = true
                }
                : new Dictionary<string, object?> { ["recap"] = "The work is done." });

    private static FunctionCallContent WriteTodos(params (string Content, string Status)[] todos) =>
        new("call-todos", "TodoWrite", new Dictionary<string, object?>
        {
            ["scope"] = "first_task",
            ["todos"] = todos.Select(todo => new Dictionary<string, object?>
            {
                ["content"] = todo.Content,
                ["status"] = todo.Status
            }).ToArray()
        });

    private sealed class TodoWriteChatClient(params FunctionCallContent[] calls) : IChatClient
    {
        private int _requestCount;

        public Func<CancellationToken, Task>? BeforeReply { get; init; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("done")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var index = Interlocked.Increment(ref _requestCount) - 1;
            if (index < calls.Length)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [calls[index]]);
                yield break;
            }

            if (BeforeReply is not null)
                await BeforeReply(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Done.")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class WrapUpToolSource : IToolSource
    {
        public static readonly ToolName Name = new("desktop_conversation", "WrapUp");

        public string SourceId => "runtime-dynamic-wrap-up-test";

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context, CancellationToken cancellationToken = default)
        {
            var definitionId = new ToolDefinitionId(ToolSourceKind.RuntimeDynamic, SourceId,
                new SourceToolId(Name.ToString()));
            var definition = new ToolDefinition(
                definitionId, Name, "Summarize the turn.", JsonSerializer.SerializeToElement(new { type = "object" }),
                provenance: new ToolProvenance(ToolSourceKind.RuntimeDynamic, SourceId, "thread"));
            var binding = new ToolRuntimeBinding(new RuntimeBindingId(SourceId), definitionId,
                new WrapUpRuntime(), ToolBindingLeases.AlwaysAvailable, SourceId, context.Revision);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
                [new ToolRegistration(definition, binding, ToolProjectionShape.DynamicLifecycle,
                    ToolExposure.Direct, ToolInvocationAudience.Model)]);
        }
    }

    private sealed class WrapUpRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("Wrapped up."));
    }
}
