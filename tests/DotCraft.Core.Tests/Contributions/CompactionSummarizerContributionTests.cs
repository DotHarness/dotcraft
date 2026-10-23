using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class CompactionSummarizerContributionTests
{
    private const string ThreadId = "thread-a";
    private const string OtherThreadId = "thread-b";

    [Fact]
    public Task AutoCompact_LegacyPartialSummary_IsIdenticalAcrossViews() =>
        AssertIdenticalAcrossViews(RunAutoLegacyPartialAsync, run =>
        {
            Assert.Equal(CompactionOutcome.Partial, run.Status.Outcome);
            Assert.Contains("important context", run.Messages);
            Assert.Contains("\"mode\":\"legacy\"", run.Traces);
            Assert.Equal(1, run.ChatCalls);
        });

    [Fact]
    public Task ManualCompact_SummaryCallThrowing_FailsAndTripsTheBreakerIdenticallyAcrossViews() =>
        AssertIdenticalAcrossViews(RunManualProviderTimeoutAsync, run =>
        {
            Assert.Equal(CompactionOutcome.Failed, run.Status.Outcome);
            Assert.Equal("provider_timeout", run.Status.FailureReason);
            Assert.True(run.BreakerTripped);
        });

    [Fact]
    public async Task ReplacingTheBuiltInMidSession_SubstitutesTheSummaryAndDisposalRestoresIt()
    {
        var registry = CreateRegistry();
        var pipeline = CreatePartialPipeline(new SequenceChatClient(Summary("built-in summary")), registry);
        var before = await ManualCompactAsync(pipeline);
        var replacement = new StubSummarizer("contributed summary", [new ChatMessage(ChatRole.User, "kept verbatim")]);
        var handle = registry.Add<ICompactionSummarizer>(
            replacement,
            new ContributionOptions(Order: 200, ReplaceTarget: CompactionSummarizerCatalog.BuiltInTargetName));

        var replaced = await ManualCompactAsync(pipeline);
        handle.Dispose();
        var restored = await ManualCompactAsync(pipeline);

        Assert.Contains("built-in summary", before.Messages[0].Text);
        Assert.Equal(CompactionOutcome.Partial, replaced.Status.Outcome);
        Assert.Equal(2, replaced.Messages.Count);
        Assert.Equal(ChatRole.User, replaced.Messages[0].Role);
        Assert.Contains("contributed summary", replaced.Messages[0].Text);
        Assert.Equal("kept verbatim", replaced.Messages[1].Text);
        Assert.Equal(1, replacement.Calls);
        Assert.Contains("built-in summary", restored.Messages[0].Text);
    }

    [Fact]
    public async Task ALaterContribution_TakesAuthorityWithoutNamingTheTarget()
    {
        var registry = CreateRegistry();
        var pipeline = CreatePartialPipeline(new SequenceChatClient(Summary("built-in summary")), registry);
        registry.Add<ICompactionSummarizer>(
            new StubSummarizer("first"),
            new ContributionOptions(Order: 200));
        registry.Add<ICompactionSummarizer>(
            new StubSummarizer("last"),
            new ContributionOptions(Order: 300));

        var result = await ManualCompactAsync(pipeline);

        Assert.Contains("last", result.Messages[0].Text);
    }

    [Fact]
    public async Task AThreadScopedReplacement_AppliesToThatThreadOnly()
    {
        var registry = CreateRegistry();
        var pipeline = CreatePartialPipeline(new SequenceChatClient(Summary("built-in summary")), registry);
        registry.Add<ICompactionSummarizer>(
            new StubSummarizer("scoped summary"),
            ContributionOptions.ForThread(ThreadId, order: 200));

        var scoped = await ManualCompactAsync(pipeline);
        var other = await ManualCompactAsync(pipeline, OtherThreadId);

        Assert.Contains("scoped summary", scoped.Messages[0].Text);
        Assert.Contains("built-in summary", other.Messages[0].Text);
    }

    [Fact]
    public async Task AReplacementDecliningThePartialScope_IsAskedForTheFullScope()
    {
        var registry = CreateRegistry();
        var pipeline = CreatePartialPipeline(new SequenceChatClient(Summary("unused")), registry);
        var replacement = new DecliningPartialSummarizer("whole history summary");
        registry.Add<ICompactionSummarizer>(
            replacement,
            new ContributionOptions(Order: 200, ReplaceTarget: CompactionSummarizerCatalog.BuiltInTargetName));

        var result = await ManualCompactAsync(pipeline);

        Assert.Equal(
            [CompactionSummaryScope.Partial, CompactionSummaryScope.Full],
            replacement.Scopes);
        Assert.Equal(CompactionOutcome.Partial, result.Status.Outcome);
        Assert.Contains("whole history summary", result.Messages[0].Text);
    }

    [Fact]
    public async Task AThrowingReplacement_FailsTheCompactionAndLeavesTheHistoryAlone()
    {
        var registry = CreateRegistry();
        var pipeline = CreatePartialPipeline(new SequenceChatClient(Summary("unused")), registry);
        registry.Add<ICompactionSummarizer>(
            new ThrowingSummarizer(new InvalidOperationException("contribution exploded")),
            new ContributionOptions(Order: 200, ReplaceTarget: CompactionSummarizerCatalog.BuiltInTargetName));
        var messages = MultiRoundMessages();

        var result = await pipeline.TryManualCompactHistoryAsync(
            messages,
            ThreadId,
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        Assert.Equal(CompactionOutcome.Failed, result.Status.Outcome);
        Assert.Equal("contribution exploded", result.Status.FailureReason);
        Assert.Equal(messages.Count, result.Messages.Count);
    }

    [Fact]
    public async Task ProviderNativeCompaction_NeverReachesTheContributionPoint()
    {
        var registry = CreateRegistry();
        var pipeline = CreatePartialPipeline(new SequenceChatClient(Summary("unused")), registry);
        var replacement = new StubSummarizer("contributed summary");
        registry.Add<ICompactionSummarizer>(
            replacement,
            new ContributionOptions(Order: 200, ReplaceTarget: CompactionSummarizerCatalog.BuiltInTargetName));
        var threshold = pipeline.EvaluateThreshold(100);
        var backend = new StubBackend(
            new CompactionExecutionResult(
                new CompactionStatus(CompactionOutcome.Partial, 100, 10, threshold, threshold),
                "provider_native",
                Replacement: null));
        var coordinator = new CompactionCoordinator(pipeline, _ => backend);

        var native = await coordinator.ExecuteAsync(
            new CompactionExecutionRequest(
                CompactionTrigger.Manual,
                CompactionPhase.Manual,
                MultiRoundMessages(),
                ThreadId,
                InputTokenHint: 100,
                LastAssistantTimestampUtc: null),
            CancellationToken.None);
        var local = await new CompactionCoordinator(pipeline).ExecuteAsync(
            new CompactionExecutionRequest(
                CompactionTrigger.Manual,
                CompactionPhase.Manual,
                MultiRoundMessages(),
                ThreadId,
                InputTokenHint: 100,
                LastAssistantTimestampUtc: null),
            CancellationToken.None);

        Assert.Equal("provider_native", native.BackendId);
        Assert.Equal(1, backend.Executions);
        Assert.Equal(CompactionBackendIds.LocalSummary, local.BackendId);
        Assert.Equal(1, replacement.Calls);
    }

    private static async Task AssertIdenticalAcrossViews(
        Func<IContributionView?, Task<CompactionRun>> scenario,
        Action<CompactionRun> assertShape)
    {
        var withoutRegistry = await scenario(null);
        var emptyRegistry = await scenario(new ContributionRegistry());
        var registeredBuiltIn = await scenario(CreateRegistry());

        assertShape(withoutRegistry);
        Assert.Equal(withoutRegistry, emptyRegistry);
        Assert.Equal(withoutRegistry, registeredBuiltIn);
    }

    private static async Task<CompactionRun> RunAutoLegacyPartialAsync(IContributionView? contributions)
    {
        var store = new TraceStore();
        var chat = new SequenceChatClient(Summary("important context"));
        var pipeline = CreatePartialPipeline(chat, contributions, store);

        var result = await pipeline.TryAutoCompactHistoryAsync(
            MultiRoundMessages(),
            ThreadId,
            inputTokenHint: 180_000,
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        return Capture(result, pipeline, chat, store);
    }

    private static async Task<CompactionRun> RunManualProviderTimeoutAsync(IContributionView? contributions)
    {
        var config = PartialConfig();
        config.MaxConsecutiveFailures = 1;
        var store = new TraceStore();
        var chat = new SequenceChatClient();
        var pipeline = new CompactionPipeline(config, chat, new TraceCollector(store), contributions: contributions);

        var result = await pipeline.TryManualCompactHistoryAsync(
            MultiRoundMessages(),
            ThreadId,
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

        return Capture(result, pipeline, chat, store);
    }

    private static Task<CompactionHistoryResult> ManualCompactAsync(
        CompactionPipeline pipeline,
        string threadId = ThreadId) =>
        pipeline.TryManualCompactHistoryAsync(
            MultiRoundMessages(),
            threadId,
            lastAssistantTimestampUtc: null,
            CancellationToken.None);

    private static CompactionRun Capture(
        CompactionHistoryResult result,
        CompactionPipeline pipeline,
        SequenceChatClient chat,
        TraceStore store) =>
        new(
            result.Status,
            string.Join("\n--\n", result.Messages.Select(message => $"{message.Role}:{message.Text}")),
            string.Join(
                "\n",
                store.GetEvents(ThreadId).Select(
                    trace => $"{trace.Type}|{trace.ToolName}|{trace.Content}|{trace.MetadataJson}")),
            chat.CallCount,
            pipeline.Failures.IsTripped(ThreadId));

    private static CompactionPipeline CreatePartialPipeline(
        SequenceChatClient chat,
        IContributionView? contributions,
        TraceStore? store = null) =>
        new(
            PartialConfig(),
            chat,
            store is null ? null : new TraceCollector(store),
            contributions: contributions);

    private static CompactionConfig PartialConfig() => new()
    {
        ContextWindow = 200_000,
        SummaryReserveTokens = 20_000,
        AutoCompactBufferTokens = 13_000,
        WarningBufferTokens = 20_000,
        ErrorBufferTokens = 10_000,
        ManualCompactBufferTokens = 3_000,
        MaxConsecutiveFailures = 3,
        MicrocompactEnabled = false,
        KeepRecentMinTokens = 1,
        KeepRecentMinGroups = 1,
        KeepRecentMaxTokens = 100_000
    };

    private static string Summary(string text) => $"<summary>{text}</summary>";

    private static List<ChatMessage> MultiRoundMessages()
    {
        var messages = new List<ChatMessage>();
        for (var index = 0; index < 4; index++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user {index} " + new string('u', 1_200)));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant {index} " + new string('a', 1_200)));
        }

        return messages;
    }

    private static ContributionRegistry CreateRegistry()
    {
        var registry = new ContributionRegistry();
        CompactionSummarizerCatalog.RegisterBuiltIns(registry);
        return registry;
    }

    private sealed record CompactionRun(
        CompactionStatus Status,
        string Messages,
        string Traces,
        int ChatCalls,
        bool BreakerTripped);

    private sealed class StubSummarizer(string summary, IReadOnlyList<ChatMessage>? tail = null) : ICompactionSummarizer
    {
        public int Calls { get; private set; }

        public Task<CompactionSummaryAttempt> SummarizeAsync(
            CompactionSummaryRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new CompactionSummaryAttempt(
                new CompactionSummary(summary, tail ?? []),
                null));
        }
    }

    private sealed class DecliningPartialSummarizer(string fullSummary) : ICompactionSummarizer
    {
        public List<CompactionSummaryScope> Scopes { get; } = [];

        public Task<CompactionSummaryAttempt> SummarizeAsync(
            CompactionSummaryRequest request,
            CancellationToken cancellationToken)
        {
            Scopes.Add(request.Scope);
            return Task.FromResult(request.Scope == CompactionSummaryScope.Full
                ? new CompactionSummaryAttempt(new CompactionSummary(fullSummary, []), null)
                : new CompactionSummaryAttempt(null, "no_summarizable_prefix"));
        }
    }

    private sealed class ThrowingSummarizer(Exception exception) : ICompactionSummarizer
    {
        public Task<CompactionSummaryAttempt> SummarizeAsync(
            CompactionSummaryRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<CompactionSummaryAttempt>(exception);
    }

    private sealed class StubBackend(CompactionExecutionResult result) : ICompactionBackend
    {
        public string Id => result.BackendId;

        public int Executions { get; private set; }

        public Task<CompactionExecutionResult> ExecuteAsync(
            CompactionExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Executions++;
            return Task.FromResult(result);
        }
    }

    private sealed class SequenceChatClient(params string[] responseTexts) : IChatClient
    {
        private int _index;

        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (responseTexts.Length == 0)
                throw new OperationCanceledException("provider stalled");

            var text = _index < responseTexts.Length ? responseTexts[_index++] : responseTexts[^1];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
