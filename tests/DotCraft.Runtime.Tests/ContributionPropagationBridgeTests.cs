using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Runtime.Tests;

/// <summary>Propagation of registry mutations into per-thread materialized state: captured contribution points
/// trigger one invalidation per coalesced batch, per-call ones trigger none.</summary>
public sealed class ContributionPropagationBridgeTests
{
    [Fact]
    public void ToolSourceMutation_InvalidatesThreadAgents()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        using var bridge = new ContributionPropagationBridge(registry, refresh);

        var handle = registry.Add<IToolSource>(new StubToolSource("late"));
        Assert.Equal(1, refresh.Invalidations);

        handle.Dispose();
        Assert.Equal(2, refresh.Invalidations);
    }

    [Fact]
    public void ACoalescedBatch_CostsExactlyOneInvalidation()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        using var bridge = new ContributionPropagationBridge(registry, refresh);

        using (registry.BeginBatch())
        {
            registry.Add<IToolSource>(new StubToolSource("a"));
            registry.Add<IToolSource>(new StubToolSource("b"));
            registry.Add<IChatMiddleware>(new StubMiddleware());
            registry.Add<ISystemPromptSection>(new StubSection());
        }

        Assert.Equal(1, refresh.Invalidations);
    }

    [Fact]
    public void AgentContextMutation_InvalidatesThreadAgents()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        using var bridge = new ContributionPropagationBridge(registry, refresh);

        // Captured into the agent's provider list at build time, so a live thread must be rebuilt.
        registry.Add<IAgentContextSource>(new StubContextContribution());

        Assert.Equal(1, refresh.Invalidations);
    }

    [Fact]
    public void ToolRestrictionMutation_InvalidatesThreadAgents()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        using var bridge = new ContributionPropagationBridge(registry, refresh);

        // Folded into the frozen tool snapshot, so a live thread must be rebuilt.
        var handle = registry.Add<IToolRestriction>(new StubRestriction());
        Assert.Equal(1, refresh.Invalidations);

        handle.Dispose();
        Assert.Equal(2, refresh.Invalidations);
    }

    [Fact]
    public void SubAgentRuntimeMutation_InvalidatesThreadAgents()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        using var bridge = new ContributionPropagationBridge(registry, refresh);

        // The profiles section is captured into the agent's instructions, so a live thread must be rebuilt.
        var handle = registry.Add<ISubAgentRuntimeSource>(new StubSubAgentRuntimeContribution());
        Assert.Equal(1, refresh.Invalidations);

        handle.Dispose();
        Assert.Equal(2, refresh.Invalidations);
    }

    [Fact]
    public void CommandMutation_ReleasesTheMemoizedSummaryWithoutInvalidatingAgents()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        var pages = new ContextPageManager();
        using var bridge = new ContributionPropagationBridge(registry, refresh, pages);
        var key = ContextPageKeys.CustomCommandsSummary("craft");
        var loads = 0;
        string Load() => $"summary-{++loads}";

        Assert.Equal("summary-1", Page(pages, key, Load));
        var handle = registry.Add<ICodeCommand>(new StubCommand());
        Assert.Equal(0, refresh.Invalidations);
        Assert.Equal("summary-2", Page(pages, key, Load));

        handle.Dispose();
        Assert.Equal(0, refresh.Invalidations);
        Assert.Equal("summary-3", Page(pages, key, Load));
    }

    [Fact]
    public void ThreadPromptMutation_ReleasesCurrentProviderPagesWithoutInvalidatingAgents()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        var pages = new ContextPageManager();
        using var bridge = new ContributionPropagationBridge(registry, refresh, pages);
        var key = new ContextPageKey("plugin", "thread-prompt", "");
        var loads = 0;
        string Load() => $"prompt-{++loads}";

        registry.Add<IThreadSystemPromptContextProvider>(new StubThreadPromptProvider(key));
        Assert.Equal("prompt-1", Page(pages, key, Load));

        registry.Add<IThreadSystemPromptContextProvider>(new StubThreadPromptProvider(key));

        Assert.Equal(0, refresh.Invalidations);
        Assert.Equal("prompt-2", Page(pages, key, Load));
    }

    [Fact]
    public void ContributionPointsEvaluatedPerCall_DoNotInvalidateAnything()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        using var bridge = new ContributionPropagationBridge(registry, refresh);

        registry.Add<IToolResultNormalizer>(new StubNormalizer());
        registry.Add<ICompactableToolPolicy>(new StubCompactablePolicy());
        registry.Add<ICompactionSummarizer>(new StubSummarizer());
        registry.Add<ITraceSink>(new StubTraceSink());
        registry.Add<ICommitMessageSuggester>(new StubCommitService());
        registry.Add<IWelcomeSuggester>(new StubWelcomeService());
        registry.Add<IThreadRuntimeSignalContributor>(new StubSignalContributor());
        registry.Add<ISystemPromptSection>(new StubSection());
        registry.Add<ISystemPromptAssembler>(new StubAssembler());

        Assert.Equal(0, refresh.Invalidations);
    }

    [Fact]
    public void DisposingTheBridge_StopsListening()
    {
        var registry = new ContributionRegistry();
        var refresh = new RecordingRefreshService();
        var bridge = new ContributionPropagationBridge(registry, refresh);

        bridge.Dispose();
        bridge.Dispose();
        registry.Add<IToolSource>(new StubToolSource("after-dispose"));

        Assert.Equal(0, refresh.Invalidations);
    }

    private static string Page(IContextPageManager pages, ContextPageKey key, Func<string> load) =>
        pages.GetOrAdd("thread-a", key, ContextPageLifecycle.StableUntilCompaction, load).Content;

    private sealed class StubCommand : ICodeCommand
    {
        public string Name => "/stub-command";

        public string Description => "stub";

        public string? Expand(CommandInvocation invocation) => null;
    }

    private sealed class StubSignalContributor : IThreadRuntimeSignalContributor
    {
        public Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubSubAgentRuntimeContribution : ISubAgentRuntimeSource
    {
        public ISubAgentRuntime Runtime { get; } = new StubSubAgentRuntime();
    }

    private sealed class StubSubAgentRuntime : ISubAgentRuntime
    {
        public string RuntimeType => "stub-runtime";

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));

        public Task<DotCraft.Agents.SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DotCraft.Agents.SubAgentRunResult { Text = request.Task });

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubRestriction : IToolRestriction
    {
        public string Name => "stub-restriction";

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) => null;
    }

    private sealed class StubTraceSink : ITraceSink
    {
        public string Name => "stub-sink";

        public void Record(TraceEvent evt)
        {
        }
    }

    private sealed class StubCommitService : ICommitMessageSuggester
    {
        public Task<CommitMessageSuggestionResult> SuggestAsync(
            CommitMessageSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommitMessageSuggestionResult("stub"));
    }

    private sealed class StubWelcomeService : IWelcomeSuggester
    {
        public Task<WelcomeSuggestionSnapshot> SuggestAsync(
            WelcomeSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WelcomeSuggestionSnapshot());

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
        {
        }

        public void ClearWorkspaceCache(string workspacePath)
        {
        }
    }

    private sealed class StubCompactablePolicy : ICompactableToolPolicy
    {
        public string Name => "stub-compactable";

        public bool? IsCompactable(string toolName) => null;
    }

    private sealed class StubSummarizer : ICompactionSummarizer
    {
        public Task<CompactionSummaryAttempt> SummarizeAsync(
            CompactionSummaryRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CompactionSummaryAttempt(null, "stub"));
    }

    private sealed class RecordingRefreshService : IThreadAgentRefreshService
    {
        public int Invalidations { get; private set; }

        public Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public void InvalidateThreadAgents() => Invalidations++;
    }

    private sealed class StubToolSource(string sourceId) : IToolSource
    {
        public string SourceId { get; } = sourceId;

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);
    }

    private sealed class StubMiddleware : IChatMiddleware
    {
        public string Name => "stub";

        public Microsoft.Extensions.AI.IChatClient Wrap(
            Microsoft.Extensions.AI.IChatClient inner,
            ChatPipelineContext context) => inner;
    }

    private sealed class StubContextContribution : IAgentContextSource
    {
        public DotCraft.Agents.AIContextProvider? CreateProvider(AgentContextRequest request) => null;
    }

    private sealed class StubSection : ISystemPromptSection
    {
        public string Name => "stub";

        public string? GetContent(SystemPromptSectionContext context) => null;
    }

    private sealed class StubAssembler : ISystemPromptAssembler
    {
        public string Assemble(string prompt, SystemPromptSectionContext context) => prompt;
    }

    private sealed class StubThreadPromptProvider(ContextPageKey key) : IThreadSystemPromptContextProvider
    {
        public ContextPageKey ContextPageKey { get; } = key;

        public string? GetSystemPromptSection(ThreadSystemPromptContext context) => null;
    }

    private sealed class StubNormalizer : IToolResultNormalizer
    {
        public ValueTask<ToolExecutionResult> NormalizeAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }
}
