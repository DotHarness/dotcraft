using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Tracing;

namespace DotCraft.Runtime;

/// <summary>The explicit set of contribution contracts admitted across the collectible boundary.</summary>
internal static class PluginContributionAdapters
{
    public static TContract Adapt<TContract>(TContract target, PluginInvocation invocation)
        where TContract : class, IContributionContract
    {
        object adapted = target switch
        {
            ISystemPromptSection value when typeof(TContract) == typeof(ISystemPromptSection) =>
                new SystemPromptSectionAdapter(value, invocation),
            ISystemPromptAssembler value when typeof(TContract) == typeof(ISystemPromptAssembler) =>
                new SystemPromptAssemblerAdapter(value, invocation),
            IChatContextProvider value when typeof(TContract) == typeof(IChatContextProvider) =>
                new ChatContextProviderAdapter(value, invocation),
            IThreadSystemPromptContextProvider value
                when typeof(TContract) == typeof(IThreadSystemPromptContextProvider) =>
                new ThreadPromptContextAdapter(value, invocation),
            IAgentContextSource value when typeof(TContract) == typeof(IAgentContextSource) =>
                new PluginAgentContextSourceAdapter(value, invocation),
            ICompactionSummarizer value when typeof(TContract) == typeof(ICompactionSummarizer) =>
                new CompactionSummarizerAdapter(value, invocation),
            ICompactableToolPolicy value when typeof(TContract) == typeof(ICompactableToolPolicy) =>
                new CompactableToolPolicyAdapter(value, invocation),
            IChatMiddleware value when typeof(TContract) == typeof(IChatMiddleware) =>
                new PluginChatMiddlewareAdapter(value, invocation),
            IToolSource value when typeof(TContract) == typeof(IToolSource) => value,
            IToolPolicyEvaluator value when typeof(TContract) == typeof(IToolPolicyEvaluator) =>
                new ToolPolicyAdapter(value, invocation),
            IToolApprovalEvaluator value when typeof(TContract) == typeof(IToolApprovalEvaluator) =>
                new ToolApprovalAdapter(value, invocation),
            IToolInvocationRecorder value when typeof(TContract) == typeof(IToolInvocationRecorder) =>
                new ToolRecorderAdapter(value, invocation),
            IToolResultNormalizer value when typeof(TContract) == typeof(IToolResultNormalizer) =>
                new ToolNormalizerAdapter(value, invocation),
            IToolRestriction value when typeof(TContract) == typeof(IToolRestriction) =>
                new ToolRestrictionAdapter(value, invocation),
            IThreadLifecycleContributor value when typeof(TContract) == typeof(IThreadLifecycleContributor) =>
                new ThreadLifecycleAdapter(value, invocation),
            ITurnLifecycleContributor value when typeof(TContract) == typeof(ITurnLifecycleContributor) =>
                new TurnLifecycleAdapter(value, invocation),
            IThreadRuntimeSignalContributor value
                when typeof(TContract) == typeof(IThreadRuntimeSignalContributor) =>
                new ThreadRuntimeSignalAdapter(value, invocation),
            ICommitMessageSuggester value when typeof(TContract) == typeof(ICommitMessageSuggester) =>
                new CommitMessageSuggesterAdapter(value, invocation),
            IWelcomeSuggester value when typeof(TContract) == typeof(IWelcomeSuggester) =>
                new WelcomeSuggesterAdapter(value, invocation),
            ISubAgentRuntimeSource value when typeof(TContract) == typeof(ISubAgentRuntimeSource) =>
                new PluginSubAgentRuntimeSourceAdapter(value, invocation),
            ICodeCommand value when typeof(TContract) == typeof(ICodeCommand) =>
                new CodeCommandAdapter(value, invocation),
            ITraceSink value when typeof(TContract) == typeof(ITraceSink) =>
                new TraceSinkAdapter(value, invocation),
            _ => throw Unsupported(typeof(TContract), "The contract is not in the plugin contribution catalog.")
        };
        return (TContract)adapted;
    }

    private static InvalidOperationException Unsupported(Type contract, string reason) =>
        new($"Plugin contribution contract '{contract.FullName}' is not supported. {reason}");

    private static TResult HostOwned<TResult>(TResult result, string description)
    {
        PluginObjectGraphGuard.EnsureHostOwnedGraph(result, description);
        return result;
    }

    private sealed class SystemPromptSectionAdapter : ISystemPromptSection
    {
        private readonly PluginTarget<ISystemPromptSection> _target;
        private readonly PluginInvocation _invocation;

        public SystemPromptSectionAdapter(ISystemPromptSection target, PluginInvocation invocation)
        {
            _target = invocation.Capture(target);
            _invocation = invocation;
            Name = invocation.Invoke(() => target.Name);
        }

        public string Name { get; }

        public string? GetContent(SystemPromptSectionContext context) =>
            _invocation.Invoke(() => _target.Value.GetContent(context));
    }

    private sealed class SystemPromptAssemblerAdapter(
        ISystemPromptAssembler target,
        PluginInvocation invocation) : ISystemPromptAssembler
    {
        private readonly PluginTarget<ISystemPromptAssembler> _target = invocation.Capture(target);

        public string Assemble(string prompt, SystemPromptSectionContext context) =>
            invocation.Invoke(() => _target.Value.Assemble(prompt, context));
    }

    private sealed class ChatContextProviderAdapter(
        IChatContextProvider target,
        PluginInvocation invocation) : IChatContextProvider
    {
        private readonly PluginTarget<IChatContextProvider> _target = invocation.Capture(target);

        public string? GetSystemPromptSection() =>
            invocation.Invoke(_target.Value.GetSystemPromptSection);

        public IEnumerable<string> GetRuntimeContextLines() =>
            invocation.Snapshot(_target.Value.GetRuntimeContextLines);
    }

    private sealed class ThreadPromptContextAdapter : IThreadSystemPromptContextProvider
    {
        private readonly PluginTarget<IThreadSystemPromptContextProvider> _target;
        private readonly PluginInvocation _invocation;

        public ThreadPromptContextAdapter(
            IThreadSystemPromptContextProvider target,
            PluginInvocation invocation)
        {
            _invocation = invocation;
            ContextPageKey = invocation.Invoke(() => target.ContextPageKey);
            var placement = invocation.Invoke(() => target.Placement);
            if (placement != ThreadPromptPlacement.BaseInstructions)
            {
                throw Unsupported(
                    typeof(IThreadSystemPromptContextProvider),
                    "Collectible plugins can contribute only base-instruction thread context in this host version.");
            }
            _target = invocation.Capture(target);
        }

        public ContextPageKey ContextPageKey { get; }

        public ThreadPromptPlacement Placement => ThreadPromptPlacement.BaseInstructions;

        public string? GetSystemPromptSection(ThreadSystemPromptContext context) =>
            _invocation.Invoke(() => _target.Value.GetSystemPromptSection(context));
    }

    private sealed class CompactionSummarizerAdapter(
        ICompactionSummarizer target,
        PluginInvocation invocation) : ICompactionSummarizer
    {
        private readonly PluginTarget<ICompactionSummarizer> _target = invocation.Capture(target);

        public Task<CompactionSummaryAttempt> SummarizeAsync(
            CompactionSummaryRequest request,
            CancellationToken cancellationToken) =>
            invocation.InvokeAsync(new Func<Task<CompactionSummaryAttempt>>(async () =>
                HostOwned(
                    await _target.Value.SummarizeAsync(request, cancellationToken).ConfigureAwait(false),
                    "compaction result")));
    }

    private sealed class CompactableToolPolicyAdapter : ICompactableToolPolicy
    {
        private readonly PluginTarget<ICompactableToolPolicy> _target;
        private readonly PluginInvocation _invocation;

        public CompactableToolPolicyAdapter(ICompactableToolPolicy target, PluginInvocation invocation)
        {
            _target = invocation.Capture(target);
            _invocation = invocation;
            Name = invocation.Invoke(() => target.Name);
        }

        public string Name { get; }

        public bool? IsCompactable(string toolName) =>
            _invocation.Invoke(() => _target.Value.IsCompactable(toolName));
    }

    private sealed class ToolPolicyAdapter(
        IToolPolicyEvaluator target,
        PluginInvocation invocation) : IToolPolicyEvaluator
    {
        private readonly PluginTarget<IToolPolicyEvaluator> _target = invocation.Capture(target);

        public ValueTask<ToolDispatchDecision> EvaluateAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(new Func<ValueTask<ToolDispatchDecision>>(async () =>
                HostOwned(
                    await _target.Value
                        .EvaluateAsync(context, registration, arguments, cancellationToken)
                        .ConfigureAwait(false),
                    "tool policy decision")));
    }

    private sealed class ToolApprovalAdapter(
        IToolApprovalEvaluator target,
        PluginInvocation invocation) : IToolApprovalEvaluator
    {
        private readonly PluginTarget<IToolApprovalEvaluator> _target = invocation.Capture(target);

        public ValueTask<ToolDispatchDecision> RequestAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(new Func<ValueTask<ToolDispatchDecision>>(async () =>
                HostOwned(
                    await _target.Value
                        .RequestAsync(context, registration, arguments, cancellationToken)
                        .ConfigureAwait(false),
                    "tool approval decision")));
    }

    private sealed class ToolRecorderAdapter(
        IToolInvocationRecorder target,
        PluginInvocation invocation) : IToolInvocationRecorder
    {
        private readonly PluginTarget<IToolInvocationRecorder> _target = invocation.Capture(target);

        public ValueTask RecordStartedAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() =>
                _target.Value.RecordStartedAsync(context, registration, arguments, cancellationToken));

        public ValueTask RecordTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            TimeSpan duration,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() =>
                _target.Value.RecordTerminalAsync(context, registration, result, duration, cancellationToken));
    }

    private sealed class ToolNormalizerAdapter(
        IToolResultNormalizer target,
        PluginInvocation invocation) : IToolResultNormalizer
    {
        private readonly PluginTarget<IToolResultNormalizer> _target = invocation.Capture(target);

        public ValueTask<ToolExecutionResult> NormalizeAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(new Func<ValueTask<ToolExecutionResult>>(async () =>
                HostOwned(
                    await _target.Value
                        .NormalizeAsync(context, registration, result, cancellationToken)
                        .ConfigureAwait(false),
                    "normalized tool result")));
    }

    private sealed class ToolRestrictionAdapter : IToolRestriction
    {
        private readonly PluginTarget<IToolRestriction> _target;
        private readonly PluginInvocation _invocation;

        public ToolRestrictionAdapter(IToolRestriction target, PluginInvocation invocation)
        {
            _target = invocation.Capture(target);
            _invocation = invocation;
            Name = invocation.Invoke(() => target.Name);
        }

        public string Name { get; }

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
            _invocation.Invoke(() =>
                HostOwned(_target.Value.Restrict(context), "tool restriction edit"));
    }

    private sealed class ThreadLifecycleAdapter(
        IThreadLifecycleContributor target,
        PluginInvocation invocation) : IThreadLifecycleContributor
    {
        private readonly PluginTarget<IThreadLifecycleContributor> _target = invocation.Capture(target);

        public Task OnThreadStartedAsync(
            SessionThread thread,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() => _target.Value.OnThreadStartedAsync(thread, cancellationToken));

        public Task OnThreadResumedAsync(
            SessionThread thread,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() => _target.Value.OnThreadResumedAsync(thread, cancellationToken));

        public Task OnThreadDeletingAsync(
            SessionThread thread,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() => _target.Value.OnThreadDeletingAsync(thread, cancellationToken));
    }

    private sealed class TurnLifecycleAdapter(
        ITurnLifecycleContributor target,
        PluginInvocation invocation) : ITurnLifecycleContributor
    {
        private readonly PluginTarget<ITurnLifecycleContributor> _target = invocation.Capture(target);

        public Task OnTurnStartedAsync(
            TurnLifecycleContext context,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() => _target.Value.OnTurnStartedAsync(context, cancellationToken));

        public Task OnTurnEndedAsync(
            TurnLifecycleContext context,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() => _target.Value.OnTurnEndedAsync(context, cancellationToken));
    }

    private sealed class ThreadRuntimeSignalAdapter(
        IThreadRuntimeSignalContributor target,
        PluginInvocation invocation) : IThreadRuntimeSignalContributor
    {
        private readonly PluginTarget<IThreadRuntimeSignalContributor> _target = invocation.Capture(target);

        public Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(() => _target.Value.OnThreadRuntimeSignalAsync(context, cancellationToken));
    }

    private sealed class CommitMessageSuggesterAdapter(
        ICommitMessageSuggester target,
        PluginInvocation invocation) : ICommitMessageSuggester
    {
        private readonly PluginTarget<ICommitMessageSuggester> _target = invocation.Capture(target);

        public Task<CommitMessageSuggestionResult> SuggestAsync(
            CommitMessageSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(new Func<Task<CommitMessageSuggestionResult>>(async () =>
                HostOwned(
                    await _target.Value.SuggestAsync(parameters, cancellationToken).ConfigureAwait(false),
                    "commit message suggestion")));
    }

    private sealed class WelcomeSuggesterAdapter(
        IWelcomeSuggester target,
        PluginInvocation invocation) : IWelcomeSuggester
    {
        private readonly PluginTarget<IWelcomeSuggester> _target = invocation.Capture(target);

        public Task<WelcomeSuggestionSnapshot> SuggestAsync(
            WelcomeSuggestionRequest parameters,
            CancellationToken cancellationToken = default) =>
            invocation.InvokeAsync(new Func<Task<WelcomeSuggestionSnapshot>>(async () =>
                HostOwned(
                    await _target.Value.SuggestAsync(parameters, cancellationToken).ConfigureAwait(false),
                    "welcome suggestion")));

        public void ScheduleRefresh(string workspacePath, string? triggerThreadId = null) =>
            invocation.Invoke(() => _target.Value.ScheduleRefresh(workspacePath, triggerThreadId));

        public void ClearWorkspaceCache(string workspacePath) =>
            invocation.Invoke(() => _target.Value.ClearWorkspaceCache(workspacePath));
    }

    private sealed class CodeCommandAdapter : ICodeCommand
    {
        private readonly PluginTarget<ICodeCommand> _target;
        private readonly PluginInvocation _invocation;

        public CodeCommandAdapter(ICodeCommand target, PluginInvocation invocation)
        {
            _target = invocation.Capture(target);
            _invocation = invocation;
            Name = invocation.Invoke(() => target.Name);
            Description = invocation.Invoke(() => target.Description);
            Aliases = invocation.Snapshot(() => target.Aliases);
        }

        public string Name { get; }

        public string Description { get; }

        public IReadOnlyList<string> Aliases { get; }

        public string? Expand(CommandInvocation invocation) =>
            _invocation.Invoke(() => _target.Value.Expand(invocation));
    }

    private sealed class TraceSinkAdapter : ITraceSink
    {
        private readonly PluginTarget<ITraceSink> _target;
        private readonly PluginInvocation _invocation;

        public TraceSinkAdapter(ITraceSink target, PluginInvocation invocation)
        {
            _target = invocation.Capture(target);
            _invocation = invocation;
            Name = invocation.Invoke(() => target.Name);
        }

        public string Name { get; }

        public void Record(TraceEvent evt) =>
            _invocation.Invoke(() => _target.Value.Record(evt));
    }
}
