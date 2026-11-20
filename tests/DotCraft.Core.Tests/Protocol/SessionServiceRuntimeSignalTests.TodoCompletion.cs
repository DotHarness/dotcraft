using System.Text.Json;
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

    [Theory]
    [InlineData("none", false)]
    [InlineData("before_write", false)]
    [InlineData("failed", false)]
    [InlineData("after_write", true)]
    public async Task CompletedTurn_ChecksOffPendingTasksOnlyAfterSuccessfulWrapUpOfTheLatestWrite(
        string wrapUp, bool completesAll)
    {
        var write = WriteTodos(
            ("Read the code", PlanTodoStatus.Completed),
            ("Run tests", PlanTodoStatus.Pending),
            ("Change it", PlanTodoStatus.InProgress));
        var wrap = new FunctionCallContent(
            "call-wrap", ProviderToolProjector.Project([WrapUpToolSource.Name])[WrapUpToolSource.Name],
            new Dictionary<string, object?>());
        using var chatClient = new TodoWriteChatClient(wrapUp switch
        {
            "none" => [write],
            "before_write" => [wrap, write],
            _ => [write, wrap]
        });
        var planStore = new PlanStore(_tempDir);
        var recorder = new ToolInvocationRecorderRouter();
        await using var agentFactory = CreateAgentFactory(
            chatClient, [new WrapUpToolSource(wrapUp != "failed")],
            toolDispatcher: new ToolDispatcher(recorder: recorder), planStore: planStore);
        var service = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        recorder.Bind(service);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("do the work")]))
            .WaitAsync(TimeSpan.FromSeconds(10));

        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.NotNull(plan);
        Assert.Equal(
            completesAll
                ? [PlanTodoStatus.Completed, PlanTodoStatus.Completed, PlanTodoStatus.Completed]
                : [PlanTodoStatus.Completed, PlanTodoStatus.Pending, PlanTodoStatus.InProgress],
            plan.Todos.Select(todo => todo.Status));
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
    public async Task CompletedTurn_RetainsTheExecutionGateUntilItsChecklistIsSaved()
    {
        var planStore = new PlanStore(_tempDir);
        using var chatClient = new TodoWriteChatClient(WriteTodos(("Finish work", PlanTodoStatus.InProgress)));
        await using var agentFactory = CreateAgentFactory(chatClient, planStore: planStore);
        var gate = new SessionGate();
        var service = new SessionService(agentFactory, chatClient.AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)), gate);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        Task<IDisposable>? competingLease = null;
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? gateHeldAtSave = null;
        service.ThreadReadStateChangedForBroadcast = (threadId, _) =>
        {
            competingLease ??= gate.AcquireAsync(threadId);
            probeStarted.TrySetResult();
        };
        planStore.Saved += (_, plan) =>
        {
            if (plan.Todos is [{ Status: PlanTodoStatus.Completed }])
                gateHeldAtSave = competingLease is { IsCompleted: false };
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var drain = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("do the work")], ct: timeout.Token));
        await probeStarted.Task.WaitAsync(timeout.Token);
        (await competingLease!.WaitAsync(timeout.Token)).Dispose();
        await drain.WaitAsync(timeout.Token);
        Assert.True(gateHeldAtSave);
    }

    private static FunctionCallContent WriteTodos(params (string Content, string Status)[] todos) =>
        new("call-todos", "TodoWrite", new Dictionary<string, object?>
        {
            ["todos"] = todos.Select(todo => new Dictionary<string, object?>
            {
                ["content"] = todo.Content,
                ["status"] = todo.Status
            }).ToArray()
        });

    private sealed class TodoWriteChatClient(params FunctionCallContent[] calls) : IChatClient
    {
        private int _requestCount;

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
            yield return index < calls.Length
                ? new ChatResponseUpdate(ChatRole.Assistant, [calls[index]])
                : new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Done.")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class WrapUpToolSource(bool success) : IToolSource
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
            var result = success
                ? ToolExecutionResult.Succeeded("Wrapped up.")
                : ToolExecutionResult.Failed(new ToolError(ToolErrorCodes.InputInvalid, "Wrap up was rejected."));
            var binding = new ToolRuntimeBinding(new RuntimeBindingId(SourceId), definitionId,
                new DynamicProjectionRuntime(result), ToolBindingLeases.AlwaysAvailable, SourceId, context.Revision);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(
                [new ToolRegistration(definition, binding, ToolProjectionShape.DynamicLifecycle,
                    ToolExposure.Direct, ToolInvocationAudience.Model)]);
        }
    }
}
