using DotCraft.Configuration;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_UsesResolvedBackendExactlyOnce()
    {
        var pipeline = new CompactionPipeline(new CompactionConfig(), new NoopChatClient());
        var threshold = pipeline.EvaluateThreshold(100);
        var backend = new RecordingBackend(
            new CompactionExecutionResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    100,
                    100,
                    threshold,
                    threshold,
                    FailureReason: "selected_backend_failed"),
                "selected",
                Replacement: null));
        var resolverCalls = 0;
        var coordinator = new CompactionCoordinator(
            pipeline,
            request =>
            {
                resolverCalls++;
                Assert.Equal(CompactionTrigger.Auto, request.Trigger);
                return backend;
            });

        var result = await coordinator.ExecuteAsync(
            new CompactionExecutionRequest(
                CompactionTrigger.Auto,
                CompactionPhase.PreTurn,
                [new ChatMessage(ChatRole.User, "hello")],
                "thread_1",
                100,
                LastAssistantTimestampUtc: null),
            CancellationToken.None);

        Assert.Equal("selected", result.BackendId);
        Assert.Equal("selected_backend_failed", result.Status.FailureReason);
        Assert.Equal(1, resolverCalls);
        Assert.Equal(1, backend.ExecutionCount);
    }

    private sealed class RecordingBackend(CompactionExecutionResult result) : ICompactionBackend
    {
        public string Id => result.BackendId;

        public int ExecutionCount { get; private set; }

        public Task<CompactionExecutionResult> ExecuteAsync(
            CompactionExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
