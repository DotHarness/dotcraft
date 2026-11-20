using DotCraft.Memory;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Fact]
    public async Task CompletedTurn_ThatWroteTodos_ChecksOffOpenTasks()
    {
        var planStore = new PlanStore(_tempDir);
        using var chatClient = new TodoWriteChatClient(writeTodos: true);
        await using var agentFactory = CreateAgentFactory(chatClient, planStore: planStore);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("do the work")]))
            .WaitAsync(TimeSpan.FromSeconds(10));

        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.NotNull(plan);
        Assert.Equal(3, plan.Todos.Count);
        Assert.All(plan.Todos, todo => Assert.Equal(PlanTodoStatus.Completed, todo.Status));
    }

    [Fact]
    public async Task CompletedTurn_WithoutTodoWrite_LeavesEarlierTasksOpen()
    {
        var planStore = new PlanStore(_tempDir);
        using var chatClient = new TodoWriteChatClient(writeTodos: false);
        await using var agentFactory = CreateAgentFactory(chatClient, planStore: planStore);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        await planStore.SaveStructuredPlanAsync(thread.Id, new StructuredPlan
        {
            Todos = [new PlanTodo { Id = "todo-1", Content = "Earlier step", Status = PlanTodoStatus.Pending }]
        });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("just answer")]))
            .WaitAsync(TimeSpan.FromSeconds(10));

        var plan = await planStore.LoadStructuredPlanAsync(thread.Id);
        Assert.Equal(PlanTodoStatus.Pending, Assert.Single(plan!.Todos).Status);
    }

    private sealed class TodoWriteChatClient(bool writeTodos) : IChatClient
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
            if (writeTodos && Interlocked.Increment(ref _requestCount) == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent(
                        "call-todos",
                        "TodoWrite",
                        new Dictionary<string, object?>
                        {
                            ["todos"] = new[]
                            {
                                new Dictionary<string, object?> { ["content"] = "Read the code", ["status"] = "completed" },
                                new Dictionary<string, object?> { ["content"] = "Change it", ["status"] = "in_progress" },
                                new Dictionary<string, object?> { ["content"] = "Run tests", ["status"] = "pending" }
                            }
                        })
                ]);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Done.")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
