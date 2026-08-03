using DotCraft.Configuration;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Xunit;

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

    [Fact]
    public async Task ExecuteAsync_BackendFailuresTripOnlySelectedBackendBreaker()
    {
        var pipeline = CreatePipeline(maxFailures: 2);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        var backend = new ThrowingBackend("selected", new IOException("remote failed"));
        var coordinator = new CompactionCoordinator(
            pipeline,
            _ => backend,
            _ => tracker);
        var request = CreateRequest(CompactionTrigger.Auto);

        await Assert.ThrowsAsync<IOException>(
            () => coordinator.ExecuteAsync(request, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(
            () => coordinator.ExecuteAsync(request, CancellationToken.None));
        var result = await coordinator.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal(CompactionOutcome.Skipped, result.Status.Outcome);
        Assert.Equal("circuit_breaker_tripped", result.Status.FailureReason);
        Assert.Equal(2, backend.ExecutionCount);
        Assert.True(tracker.IsTripped(request.ThreadId));
        Assert.False(pipeline.Failures.IsTripped(request.ThreadId));
    }

    [Theory]
    [InlineData(0, CompactionOutcome.Skipped)]
    [InlineData(1, CompactionOutcome.Failed)]
    [InlineData(2, CompactionOutcome.Failed)]
    public async Task ExecuteAsync_TrippedBreakerMapsOutcomeByTrigger(
        int triggerValue,
        CompactionOutcome expectedOutcome)
    {
        var trigger = (CompactionTrigger)triggerValue;
        var pipeline = CreatePipeline(maxFailures: 1);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        tracker.RecordFailure("thread_1");
        var backend = new RecordingBackend(CreateNativeResult(pipeline));
        var coordinator = new CompactionCoordinator(
            pipeline,
            _ => backend,
            _ => tracker);

        var result = await coordinator.ExecuteAsync(
            CreateRequest(trigger),
            CancellationToken.None);

        Assert.Equal(expectedOutcome, result.Status.Outcome);
        Assert.Equal("circuit_breaker_tripped", result.Status.FailureReason);
        Assert.Equal(0, backend.ExecutionCount);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessDoesNotResetBreakerBeforeNativeInstall()
    {
        var pipeline = CreatePipeline(maxFailures: 2);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        tracker.RecordFailure("thread_1");
        var backend = new RecordingBackend(CreateNativeResult(pipeline));
        var coordinator = new CompactionCoordinator(
            pipeline,
            _ => backend,
            _ => tracker);

        var result = await coordinator.ExecuteAsync(
            CreateRequest(CompactionTrigger.Auto),
            CancellationToken.None);
        tracker.RecordFailure("thread_1");

        Assert.True(result.Status.Success);
        Assert.True(tracker.IsTripped("thread_1"));
    }

    [Fact]
    public async Task ExecuteAsync_FailedStatusCountsAsBackendFailure()
    {
        var pipeline = CreatePipeline(maxFailures: 1);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        var threshold = pipeline.EvaluateThreshold(100);
        var backend = new RecordingBackend(
            new CompactionExecutionResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    100,
                    100,
                    threshold,
                    threshold,
                    FailureReason: "provider_compaction_unavailable"),
                "selected",
                Replacement: null));
        var coordinator = new CompactionCoordinator(
            pipeline,
            _ => backend,
            _ => tracker);

        await coordinator.ExecuteAsync(
            CreateRequest(CompactionTrigger.Manual),
            CancellationToken.None);
        var result = await coordinator.ExecuteAsync(
            CreateRequest(CompactionTrigger.Manual),
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Failed, result.Status.Outcome);
        Assert.Equal("circuit_breaker_tripped", result.Status.FailureReason);
        Assert.Equal(1, backend.ExecutionCount);
    }

    [Fact]
    public async Task InstallProviderNativeAsync_ResetsBreakerOnlyAfterSuccessfulInstall()
    {
        var pipeline = CreatePipeline(maxFailures: 2);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        tracker.RecordFailure("thread_1");
        var coordinator = new CompactionCoordinator(
            pipeline,
            resolveFailureTracker: _ => tracker);
        var bridge = new RecordingBridge((_, _) => Task.CompletedTask);
        var replacement = Assert.IsType<CompactionReplacement.ProviderNative>(
            CreateNativeResult(pipeline).Replacement);

        await coordinator.InstallProviderNativeAsync(
            "thread_1",
            "selected",
            bridge,
            replacement,
            CancellationToken.None);
        tracker.RecordFailure("thread_1");

        Assert.Equal(1, bridge.InstallCount);
        Assert.False(tracker.IsTripped("thread_1"));
    }

    [Fact]
    public async Task InstallProviderNativeAsync_FailureTripsBreaker()
    {
        var pipeline = CreatePipeline(maxFailures: 1);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        var coordinator = new CompactionCoordinator(
            pipeline,
            resolveFailureTracker: _ => tracker);
        var bridge = new RecordingBridge((_, _) => throw new IOException("write failed"));
        var replacement = Assert.IsType<CompactionReplacement.ProviderNative>(
            CreateNativeResult(pipeline).Replacement);

        await Assert.ThrowsAsync<IOException>(async () =>
            await coordinator.InstallProviderNativeAsync(
                "thread_1",
                "selected",
                bridge,
                replacement,
                CancellationToken.None));

        Assert.True(tracker.IsTripped("thread_1"));
    }

    [Fact]
    public async Task CancellationAndEmptyInputDoNotCountAsBackendFailures()
    {
        var cancellationPipeline = CreatePipeline(maxFailures: 1);
        var cancellationTracker = cancellationPipeline.GetBackendFailureTracker("cancelled");
        var cancelledBackend = new ThrowingBackend(
            "cancelled",
            new OperationCanceledException());
        var cancellationCoordinator = new CompactionCoordinator(
            cancellationPipeline,
            _ => cancelledBackend,
            _ => cancellationTracker);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => cancellationCoordinator.ExecuteAsync(
                CreateRequest(CompactionTrigger.Auto),
                cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => cancellationCoordinator.ExecuteAsync(
                CreateRequest(CompactionTrigger.Auto),
                cancellation.Token));

        var emptyPipeline = CreatePipeline(maxFailures: 1);
        var emptyTracker = emptyPipeline.GetBackendFailureTracker("empty");
        var threshold = emptyPipeline.EvaluateThreshold(100);
        var emptyBackend = new RecordingBackend(
            new CompactionExecutionResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    100,
                    100,
                    threshold,
                    threshold,
                    FailureReason: "provider_compaction_empty_input"),
                "empty",
                Replacement: null));
        var emptyCoordinator = new CompactionCoordinator(
            emptyPipeline,
            _ => emptyBackend,
            _ => emptyTracker);

        await emptyCoordinator.ExecuteAsync(
            CreateRequest(CompactionTrigger.Auto),
            CancellationToken.None);
        await emptyCoordinator.ExecuteAsync(
            CreateRequest(CompactionTrigger.Auto),
            CancellationToken.None);

        Assert.Equal(2, cancelledBackend.ExecutionCount);
        Assert.False(cancellationTracker.IsTripped("thread_1"));
        Assert.Equal(2, emptyBackend.ExecutionCount);
        Assert.False(emptyTracker.IsTripped("thread_1"));
    }

    [Fact]
    public async Task InstallProviderNativeAsync_CancellationDoesNotCountAsFailure()
    {
        var pipeline = CreatePipeline(maxFailures: 1);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        var coordinator = new CompactionCoordinator(
            pipeline,
            resolveFailureTracker: _ => tracker);
        var bridge = new RecordingBridge(
            (_, _) => throw new OperationCanceledException());
        var replacement = Assert.IsType<CompactionReplacement.ProviderNative>(
            CreateNativeResult(pipeline).Replacement);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await coordinator.InstallProviderNativeAsync(
                    "thread_1",
                    "selected",
                    bridge,
                    replacement,
                    cancellation.Token));
        }

        Assert.Equal(2, bridge.InstallCount);
        Assert.False(tracker.IsTripped("thread_1"));
    }

    [Fact]
    public async Task OperationCancelledWithoutCallerCancellationCountsAsBackendFailure()
    {
        var pipeline = CreatePipeline(maxFailures: 1);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        var backend = new ThrowingBackend(
            "selected",
            new OperationCanceledException("transport timeout"));
        var coordinator = new CompactionCoordinator(
            pipeline,
            _ => backend,
            _ => tracker);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => coordinator.ExecuteAsync(
                CreateRequest(CompactionTrigger.Auto),
                CancellationToken.None));
        var result = await coordinator.ExecuteAsync(
            CreateRequest(CompactionTrigger.Auto),
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Skipped, result.Status.Outcome);
        Assert.True(tracker.IsTripped("thread_1"));
        Assert.Equal(1, backend.ExecutionCount);
    }

    [Fact]
    public void PipelineForget_ClearsLocalAndBackendBreakers()
    {
        var pipeline = CreatePipeline(maxFailures: 1);
        var tracker = pipeline.GetBackendFailureTracker("selected");
        pipeline.Failures.RecordFailure("thread_1");
        tracker.RecordFailure("thread_1");

        pipeline.Forget("thread_1");

        Assert.False(pipeline.Failures.IsTripped("thread_1"));
        Assert.False(tracker.IsTripped("thread_1"));
    }

    private static CompactionPipeline CreatePipeline(int maxFailures) =>
        new(
            new CompactionConfig { MaxConsecutiveFailures = maxFailures },
            new NoopChatClient());

    private static CompactionExecutionRequest CreateRequest(CompactionTrigger trigger) =>
        new(
            trigger,
            CompactionPhase.PreTurn,
            [new ChatMessage(ChatRole.User, "hello")],
            "thread_1",
            100,
            LastAssistantTimestampUtc: null);

    private static CompactionExecutionResult CreateNativeResult(CompactionPipeline pipeline)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"type":"compaction","encrypted_content":"YWJj"}""");
        var before = pipeline.EvaluateThreshold(100);
        var after = pipeline.EvaluateThreshold(10);
        return new CompactionExecutionResult(
            new CompactionStatus(
                CompactionOutcome.Partial,
                100,
                10,
                before,
                after),
            "selected",
            new CompactionReplacement.ProviderNative(
                "openai_responses",
                [document.RootElement.Clone()],
                1,
                "turn_1",
                10));
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

    private sealed class ThrowingBackend(string id, Exception exception) : ICompactionBackend
    {
        public string Id => id;

        public int ExecutionCount { get; private set; }

        public Task<CompactionExecutionResult> ExecuteAsync(
            CompactionExecutionRequest request,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromException<CompactionExecutionResult>(exception);
        }
    }

    private sealed class RecordingBridge(
        Func<CompactionReplacement.ProviderNative, CancellationToken, Task> installAsync)
        : IProviderHistoryCompactionBridge
    {
        public int InstallCount { get; private set; }

        public ValueTask<ProviderCompactionInput> CaptureCompactionInputAsync(
            CompactionPhase phase,
            IReadOnlyList<ChatMessage> messages,
            ChatOptions? options,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask ReplaceNativeAsync(
            CompactionReplacement.ProviderNative replacement,
            CancellationToken cancellationToken)
        {
            InstallCount++;
            return new ValueTask(installAsync(replacement, cancellationToken));
        }

        public long EstimateNativeContextTokens(
            ProviderNativeSnapshot snapshot,
            IReadOnlyList<ChatMessage> pendingTail,
            ChatOptions? options) =>
            throw new NotSupportedException();
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
