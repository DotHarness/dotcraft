using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Fact]
    public async Task QueuedFollowup_DoesNotStartAutomaticallyWhilePlanAwaitsConfirmation()
    {
        using var chatClient = new PlanThenCleanupChatClient();
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            toolProviders: [new PlanConfirmationCleanupToolSource()],
            planStore: new PlanStore(_tempDir));
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);
        await service.SetThreadModeAsync(thread.Id, "plan");

        var signals = new List<SessionThreadRuntimeSignal>();
        var followupCompleted = new TaskCompletionSource<SessionTurn>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadRuntimeSignalForBroadcast = (threadId, signal, turn) =>
        {
            if (threadId != thread.Id)
                return;
            signals.Add(signal);
            if (signal == SessionThreadRuntimeSignal.TurnCompleted && turn is not null)
                followupCompleted.TrySetResult(turn);
        };

        var planning = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("create a plan")]));
        TurnStartOrEnqueueAdmission followup;
        try
        {
            await chatClient.PlanningStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            followup = await service.StartOrEnqueueTurnAsync(thread.Id, [new TextContent("follow up")]);
        }
        finally
        {
            chatClient.ResumePlanning.TrySetResult();
        }
        await planning.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(
            [
                SessionThreadRuntimeSignal.TurnStarted,
                SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
            ],
            signals);
        var completedThread = await service.GetThreadAsync(thread.Id);
        Assert.True(service.GetThreadRuntimeSnapshot(completedThread).WaitingOnPlanConfirmation);
        var queuedInput = Assert.Single(completedThread.QueuedInputs);
        Assert.Equal(followup.QueuedInput?.Id, queuedInput.Id);

        var started = await service.StartQueuedTurnAsync(thread.Id, queuedInput.Id);
        var turn = await followupCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(started, turn.Id);
        Assert.Equal("follow up", turn.Input?.AsUserMessage?.Text);
        Assert.Empty(completedThread.QueuedInputs);
    }

    private sealed class PlanConfirmationCleanupToolSource : AIFunctionToolSource
    {
        public override string SourceId => "plan-confirmation-cleanup";

        protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context)
        {
            yield return AIFunctionFactory.Create(
                () => "Scratch removed.",
                name: "CleanUpScratch",
                description: "Remove scratch files.");
        }
    }

    private sealed class PlanThenCleanupChatClient : IChatClient
    {
        private int _requestCount;
        public TaskCompletionSource PlanningStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumePlanning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            switch (Interlocked.Increment(ref _requestCount))
            {
                case 1:
                    PlanningStarted.TrySetResult();
                    await ResumePlanning.Task.WaitAsync(cancellationToken);
                    Assert.Contains(options?.Tools ?? [], tool => string.Equals(tool.Name, "CreatePlan", StringComparison.Ordinal));
                    yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "call-plan",
                            "CreatePlan",
                            new Dictionary<string, object?>
                            {
                                ["plan"] = "# Cleanup-safe plan\n\n## Summary\n\nKeep confirmation pending.",
                                ["todos"] = Array.Empty<PlanTodoInput>()
                            })
                    ]);
                    break;
                case 2:
                    Assert.Contains(
                        chatMessages.SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
                        result => string.Equals(result.CallId, "call-plan", StringComparison.Ordinal));
                    yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "call-close",
                            "CleanUpScratch",
                            new Dictionary<string, object?>())
                    ]);
                    break;
                default:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Plan ready.")]);
                    break;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
