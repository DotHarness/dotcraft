using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>
/// The Host-side composition the sample's contributions are asserted through: the kernel's own built-in
/// catalogs seeded into the harness registry, plus the readers that consume each contribution point.
/// </summary>
/// <remarks>Everything here is a Host reader, not a stand-in for one. The two exceptions are the stages the
/// composition root supplies from services this harness does not build — a recorder and the two suggestion
/// generators — which are probes registered under the same target names the runtime uses.</remarks>
internal sealed class DotnetPluginSampleHost : IDisposable
{
    private readonly PluginRuntimeHarness _harness;
    private readonly WorkspaceContributionScope _scope;

    internal DotnetPluginSampleHost(PluginRuntimeHarness harness)
    {
        _harness = harness;
        _scope = new WorkspaceContributionScope(harness.Registry);
        _scope.RegisterBuiltInCatalogs();
        _scope.RegisterKernelContributions(
            policyEvaluator: null,
            approvalEvaluator: null,
            recorder: HostRecorder,
            resultNormalizer: new DefaultToolResultNormalizer());
        _scope.RegisterSessionContributions(BuiltInCommitSuggester, BuiltInWelcomeSuggester);

        // The agent layer registers these two behind an AgentFactory the plugin harness does not build.
        CompactableToolPolicyCatalog.RegisterBuiltIns(harness.Registry);
        CompactionSummarizerCatalog.RegisterBuiltIns(harness.Registry);

        RuntimeSignals = _scope.AttachRuntimeSignals();
        TraceSinks = new TraceSinkDispatcher(harness.Registry);
        Traces = new TraceStore(maxEventsPerSession: 32, synchronousPersist: true, sinkDispatcher: TraceSinks);
        Dispatcher = new ToolDispatcher(contributions: harness.Registry);
    }

    internal IContributionRegistry Registry => _harness.Registry;

    internal RecordingToolRecorder HostRecorder { get; } = new();

    internal StubCommitSuggester BuiltInCommitSuggester { get; } = new();

    internal StubWelcomeSuggester BuiltInWelcomeSuggester { get; } = new();

    internal ThreadRuntimeSignalDispatcher RuntimeSignals { get; }

    internal TraceSinkDispatcher TraceSinks { get; }

    internal TraceStore Traces { get; }

    internal ToolDispatcher Dispatcher { get; }

    /// <summary>Assembles the complete system prompt exactly as the prompt builder does for a live thread.</summary>
    internal string BuildPrompt(string? threadId)
    {
        var craftPath = Path.Combine(_harness.Workspace, ".craft");
        return new PromptBuilder(
            new MemoryStore(craftPath),
            new SkillsLoader(craftPath),
            craftPath,
            _harness.Workspace,
            contributions: _harness.Registry).BuildSystemPrompt(threadId);
    }

    /// <summary>Collects the plugin tool sources' registrations for one planning context.</summary>
    internal ValueTask<IReadOnlyList<ToolRegistration>> CollectToolsAsync(
        DotnetPluginRuntimeManager manager,
        ToolPlanningContext planning) =>
        new EffectiveToolSnapshotBuilder().CollectAsync([manager.ToolSource], planning);

    /// <summary>Builds the snapshot the dispatcher resolves a call against.</summary>
    internal async Task<EffectiveToolSnapshot> BuildSnapshotAsync(
        DotnetPluginRuntimeManager manager,
        ToolPlanningContext planning) =>
        new EffectiveToolSnapshotBuilder().Build(
            await CollectToolsAsync(manager, planning),
            planning.Revision);

    /// <summary>Waits for the plugin's own activity log to carry a line containing the fragment.</summary>
    internal async Task AssertJournalContainsAsync(string pluginId, string fragment)
    {
        var path = _harness.DataPath(pluginId, "activity.log");
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (PluginLogFile.ReadLines(path).Any(line => line.Contains(fragment, StringComparison.Ordinal)))
                return;
            await Task.Delay(10);
        }

        Assert.Fail(
            $"'{pluginId}' never journalled a line containing '{fragment}'. "
            + $"Observed: {string.Join(" | ", PluginLogFile.ReadLines(path))}");
    }

    /// <summary>Asserts that the sample journal did not persist a private callback value.</summary>
    internal void AssertJournalExcludes(string pluginId, string fragment)
    {
        var path = _harness.DataPath(pluginId, "activity.log");
        Assert.DoesNotContain(
            PluginLogFile.ReadLines(path),
            line => line.Contains(fragment, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        TraceSinks.Dispose();
        RuntimeSignals.Dispose();
        _scope.Dispose();
    }

    /// <summary>Stands in for the composition root's Session lifecycle recorder, under the same target name.</summary>
    internal sealed class RecordingToolRecorder : IToolInvocationRecorder
    {
        private readonly List<string> _terminal = [];

        internal IReadOnlyList<string> Terminal
        {
            get
            {
                lock (_terminal)
                    return [.. _terminal];
            }
        }

        public ValueTask RecordStartedAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask RecordTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            lock (_terminal)
                _terminal.Add(registration.Definition.Name.ToString());
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Stands in for the workspace runtime's source-control summary generator.</summary>
    internal sealed class StubCommitSuggester : ICommitMessageSuggester
    {
        internal const string Message = "built-in commit summary";

        public Task<CommitMessageSuggestionResult> SuggestAsync(
            CommitMessageSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CommitMessageSuggestionResult(Message));
    }

    /// <summary>Stands in for the workspace runtime's welcome suggestion generator.</summary>
    internal sealed class StubWelcomeSuggester : IWelcomeSuggester
    {
        internal const string SourceName = "built-in";

        public Task<WelcomeSuggestionSnapshot> SuggestAsync(
            WelcomeSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WelcomeSuggestionSnapshot { Source = SourceName });

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null)
        {
        }

        public void ClearWorkspaceCache(string workspacePath)
        {
        }
    }
}
