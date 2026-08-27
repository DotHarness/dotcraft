using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

/// <summary>
/// One assertion per contribution point the sample covers, each checking the consequence the Host's own
/// reader produces rather than the fact that something was registered.
/// </summary>
/// <remarks>The expected strings are duplicated from the sample rather than shared with it: the bundle is
/// loaded as compiled bytes into its own load context, so breaking a contribution has to break an assertion.</remarks>
internal static class DotNetPluginSampleEffects
{
    internal const string ThreadId = "sample-thread";
    internal const string ProviderId = "acme.review-core";
    internal const string ConsumerId = "acme.review-consumer";

    /// <summary>A line of the built-in <c>response-style</c> section, absent while the replacement holds.</summary>
    private const string BuiltInResponseStyle = "Be concise, direct, and useful.";

    /// <summary>Prompt sections, the Tier-B replacement, the Tier-C takeover, and the two context bridges.</summary>
    internal static void AssertPromptEffects(DotNetPluginSampleHost host, SampleCoverageLedger ledger)
    {
        var prompt = host.BuildPrompt(ThreadId);

        Assert.Contains("## Review checklist", prompt, StringComparison.Ordinal);
        Assert.Contains("The review.normalize Tool shares the review-core checklist.", prompt, StringComparison.Ordinal);
        // The Tier-B replacement holds the built-in's slot: its text is in, and the built-in's is gone.
        Assert.Contains("## Review Response Style", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(BuiltInResponseStyle, prompt, StringComparison.Ordinal);
        ledger.Prove<ISystemPromptSection>();

        Assert.EndsWith("<!-- assembled by acme.review-core -->", prompt, StringComparison.Ordinal);
        ledger.Prove<ISystemPromptAssembler>();

        Assert.Contains("Review checklist has 3 items", prompt, StringComparison.Ordinal);
        ledger.Prove<IChatContextProvider>();

        Assert.Contains("Review plugin is active for this thread", prompt, StringComparison.Ordinal);
        ledger.Prove<IThreadSystemPromptContextProvider>();
    }

    /// <summary>The section is sized from the plugin's settings snapshot.</summary>
    internal static void AssertChecklistFollowsSettings(DotNetPluginSampleHost host)
    {
        Assert.Equal(2, ChecklistItemCount(host.BuildPrompt(ThreadId)));
    }

    /// <summary>The Tier-B summary replacement, and the policy that makes plugin Tool results prunable.</summary>
    internal static async Task AssertCompactionEffectsAsync(
        DotNetPluginSampleHost host,
        SampleCoverageLedger ledger)
    {
        var attempt = await CompactionSummarizerCatalog.Resolve(host.Registry, ThreadId).SummarizeAsync(
            new CompactionSummaryRequest(
                CompactionSummaryScope.Partial,
                [new ChatMessage(ChatRole.User, "first"), new ChatMessage(ChatRole.Assistant, "second")],
                ThreadId),
            CancellationToken.None);

        Assert.Null(attempt.Reason);
        Assert.NotNull(attempt.Result);
        Assert.StartsWith("## Review summary", attempt.Result!.FormattedSummary, StringComparison.Ordinal);
        Assert.Single(attempt.Result.PreservedTail);
        ledger.Prove<ICompactionSummarizer>();

        // The built-in allow-list defers on a plugin Tool name, so only the contributed policy can allow it.
        Assert.True(CompactableToolPolicyCatalog.IsCompactable(host.Registry, ThreadId, "review.summary"));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(host.Registry, ThreadId, "acme.unclaimed"));
        ledger.Prove<ICompactableToolPolicy>();
    }

    /// <summary>The contributed middleware observes a real model call.</summary>
    internal static async Task AssertModelPipelineEffectsAsync(
        DotNetPluginSampleHost host,
        SampleCoverageLedger ledger)
    {
        // ChatMiddlewareCatalog.Compose is internal to DotCraft.Core, so the resolved list is folded here
        // the way the kernel folds it: lowest order outermost, around the client the pipeline ends at.
        var inner = new CountingChatClient();
        var pipeline = ContributionRead.Fold(
            host.Registry.Resolve<IChatMiddleware>(ThreadId),
            (IChatClient)inner,
            (client, middleware) =>
                middleware.Wrap(client, new ChatPipelineContext(ChatPipelineKind.Agent, ThreadId)) ?? client,
            reverse: true);

        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(1, inner.Calls);
        Assert.Equal(CountingChatClient.Reply, response.Text);
        await host.AssertJournalContainsAsync(
            ProviderId,
            "model call on Agent pipeline");
        ledger.Prove<IChatMiddleware>();
    }

    /// <summary>Both bundles' Tools dispatch, and the four dispatch stages change what dispatch does.</summary>
    internal static async Task AssertToolEffectsAsync(
        DotNetPluginSampleHost host,
        DotNetPluginRuntimeManager manager,
        SampleCoverageLedger ledger)
    {
        var planning = PlanningContext(1, ThreadId);
        var snapshot = await host.BuildSnapshotAsync(manager, planning);
        Assert.Equal(
            "acme.review-normalize",
            snapshot.Registrations[new ToolName("review", "normalize")]
                .Definition.Presentation?.Id.Value);

        var summary = await host.Dispatcher.DispatchAsync(
            snapshot,
            new ToolName("review", "summary"),
            Arguments(("text", "  spaced   text  ")),
            Request("call-summary"));

        Assert.True(summary.Success, summary.Error?.Message);
        Assert.Contains("spaced text", summary.Content!, StringComparison.Ordinal);
        ledger.Prove<IToolSource>();

        // The normalizer folds, so the plugin's stamp sits on what the Host's own normalizer produced.
        Assert.EndsWith("[reviewed by acme.review-core]", summary.Content!.TrimEnd(), StringComparison.Ordinal);
        ledger.Prove<IToolResultNormalizer>();

        // The recorder fans out: the Host's recorder and the plugin's both saw the same dispatch.
        Assert.Contains("review.summary", host.HostRecorder.Terminal);
        await host.AssertJournalContainsAsync(ProviderId, "dispatch finished success=True");
        ledger.Prove<IToolInvocationRecorder>();

        var normalized = await host.Dispatcher.DispatchAsync(
            snapshot,
            new ToolName("review", "normalize"),
            Arguments(("text", "  consumer   text  ")),
            Request("call-normalize"));
        Assert.True(normalized.Success, normalized.Error?.Message);
        Assert.Contains("consumer text", normalized.Content!, StringComparison.Ordinal);

        await AssertApprovalStageAsync(host, snapshot, ledger);
        await AssertPolicyStageAsync(host, snapshot, ledger);
        await AssertRestrictionAsync(host, manager, planning, ledger);
    }

    /// <summary>The Tier-B suggestion generators, the contributed SubAgent runtime, the command, and the sink.</summary>
    internal static async Task AssertSessionAndSurfaceEffectsAsync(
        DotNetPluginSampleHost host,
        SampleCoverageLedger ledger)
    {
        var commit = await new ContributedCommitMessageSuggestService(host.Registry, host.BuiltInCommitSuggester)
            .SuggestAsync(new CommitMessageSuggestionRequest { ThreadId = ThreadId, Paths = ["src/a.cs"] });
        Assert.StartsWith("review: summarize the staged change", commit.Message, StringComparison.Ordinal);
        ledger.Prove<ICommitMessageSuggester>();

        var welcome = await new ContributedWelcomeSuggestionService(host.Registry, host.BuiltInWelcomeSuggester)
            .SuggestAsync(new WelcomeSuggestionRequest());
        Assert.Equal(ProviderId, welcome.Source);
        Assert.NotEmpty(welcome.Items);
        ledger.Prove<IWelcomeSuggester>();

        var catalog = SubAgentProfileCatalog.Resolve(host.Registry, ThreadId);
        Assert.Contains("acme-review-pass", catalog.KnownRuntimeTypes);
        Assert.True(catalog.CreateRegistry(null).TryGet("review-pass", out _));
        ledger.Prove<ISubAgentRuntimeSource>();

        var commands = CommandContributions.List(host.Registry, ThreadId);
        var contributed = Assert.Single(commands, command => command.Name == "/review");
        Assert.Contains("/rv", contributed.Aliases);
        var expanded = CommandContributions.Expand(host.Registry, "/rv", "the pasted diff", ThreadId);
        Assert.NotNull(expanded);
        Assert.Contains("the pasted diff", expanded!, StringComparison.Ordinal);
        ledger.Prove<ICodeCommand>();

        host.Traces.Record(new TraceEvent { Type = TraceEventType.Request, SessionKey = "sample-session" });
        Assert.True(host.TraceSinks.WaitForPendingSinks(TimeSpan.FromSeconds(10)));
        await host.AssertJournalContainsAsync(ProviderId, "trace event Request");
        ledger.Prove<ITraceSink>();

        host.RuntimeSignals.Publish(ThreadId, SessionThreadRuntimeSignal.ContextCompacted);
        Assert.True(host.RuntimeSignals.WaitForPendingSignals(TimeSpan.FromSeconds(10)));
        await host.AssertJournalContainsAsync(ProviderId, "runtime signal ContextCompacted");
        ledger.Prove<IThreadRuntimeSignalContributor>();

        const string privateSentinel = "<private-callback-sentinel>";
        var turnObserver = Assert.Single(
            host.Registry.ResolveEntries<ITurnLifecycleContributor>(),
            entry => entry.Origin.Kind == ContributionOriginKind.Plugin).Contribution;
        await turnObserver.OnTurnEndedAsync(new TurnLifecycleContext(privateSentinel, privateSentinel)
        {
            Status = TurnStatus.Failed,
            Error = privateSentinel
        });
        await host.AssertJournalContainsAsync(ProviderId, "turn ended status=Failed failed=True");
        host.AssertJournalExcludes(ProviderId, privateSentinel);
    }

    /// <summary>The contribution points whose only Host reader this harness cannot reach; see the coverage table.</summary>
    /// <remarks>These get the weaker claim on purpose: the sample contributes, and nothing here asserts an effect.</remarks>
    internal static void AssertRegistrationOnlyPoints(DotNetPluginSampleHost host, SampleCoverageLedger ledger)
    {
        AssertPluginContributed<IAgentContextSource>(host, ledger);
        AssertPluginContributed<IThreadLifecycleContributor>(host, ledger);
        AssertPluginContributed<ITurnLifecycleContributor>(host, ledger);
    }

    /// <summary>The contributions the kernel restores to their built-ins once the provider stops.</summary>
    internal static void AssertBuiltInsRestored(DotNetPluginSampleHost host)
    {
        var prompt = host.BuildPrompt(ThreadId);
        Assert.Contains(BuiltInResponseStyle, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Review Response Style", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<!-- assembled by acme.review-core -->", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Review checklist", prompt, StringComparison.Ordinal);

        Assert.Same(
            CompactionSummarizerCatalog.Resolve(null),
            CompactionSummarizerCatalog.Resolve(host.Registry, ThreadId));
        Assert.Same(SubAgentProfileCatalog.BuiltIn, SubAgentProfileCatalog.Resolve(host.Registry, ThreadId));
        Assert.Empty(CommandContributions.List(host.Registry, ThreadId));
        Assert.False(CompactableToolPolicyCatalog.IsCompactable(host.Registry, ThreadId, "review.summary"));
    }

    internal static JsonElement SettingsBag(int checklistLimit) =>
        JsonSerializer.Deserialize<JsonElement>(
            $$"""{"checklistLimit":{{checklistLimit}},"tone":"coaching","maxInputLength":32}""");

    private static async Task AssertApprovalStageAsync(
        DotNetPluginSampleHost host,
        EffectiveToolSnapshot snapshot,
        SampleCoverageLedger ledger)
    {
        var refused = await host.Dispatcher.DispatchAsync(
            snapshot,
            new ToolName("review", "publish"),
            new JsonObject(),
            Request("call-publish-refused"));
        Assert.False(refused.Success);
        Assert.Equal("ReviewPublishUnapproved", refused.Error!.Code);

        var acknowledged = await host.Dispatcher.DispatchAsync(
            snapshot,
            new ToolName("review", "publish"),
            new JsonObject { ["approved"] = true },
            Request("call-publish-approved"));
        Assert.True(acknowledged.Success, acknowledged.Error?.Message);
        ledger.Prove<IToolApprovalEvaluator>();
    }

    private static async Task AssertPolicyStageAsync(
        DotNetPluginSampleHost host,
        EffectiveToolSnapshot snapshot,
        SampleCoverageLedger ledger)
    {
        var denied = await host.Dispatcher.DispatchAsync(
            snapshot,
            new ToolName("review", "summary"),
            Arguments(("text", new string('x', 33))),
            Request("call-too-long"));

        Assert.False(denied.Success);
        Assert.Equal("ReviewInputTooLong", denied.Error!.Code);
        ledger.Prove<IToolPolicyEvaluator>();

        var allowed = await host.Dispatcher.DispatchAsync(
            snapshot,
            new ToolName("review", "summary"),
            Arguments(("text", "short")),
            Request("call-short"));
        Assert.True(allowed.Success, allowed.Error?.Message);
    }

    private static async Task AssertRestrictionAsync(
        DotNetPluginSampleHost host,
        DotNetPluginRuntimeManager manager,
        ToolPlanningContext planning,
        SampleCoverageLedger ledger)
    {
        var collected = await host.CollectToolsAsync(manager, planning);
        Assert.Contains(collected, entry => entry.Definition.Name.ToString() == "review.publish");

        var restricted = ToolRestrictionApplier.Apply(
            collected,
            host.Registry.Resolve<IToolRestriction>(ThreadId),
            planning);

        // A mask removes the registration; a rewrite leaves the Tool dispatchable with new text.
        Assert.DoesNotContain(restricted, entry => entry.Definition.Name.ToString() == "review.publish");
        var summary = Assert.Single(restricted, entry => entry.Definition.Name.ToString() == "review.summary");
        Assert.Contains("coaching", summary.Definition.Description, StringComparison.Ordinal);

        var restrictedSnapshot = new EffectiveToolSnapshotBuilder().Build(restricted, planning.Revision);
        var masked = await host.Dispatcher.DispatchAsync(
            restrictedSnapshot,
            new ToolName("review", "publish"),
            new JsonObject { ["approved"] = true },
            Request("call-masked"));
        Assert.False(masked.Success);
        Assert.Equal(ToolErrorCodes.NotFound, masked.Error!.Code);
        ledger.Prove<IToolRestriction>();
    }

    private static void AssertPluginContributed<TContract>(
        DotNetPluginSampleHost host,
        SampleCoverageLedger ledger)
        where TContract : class, IContributionContract
    {
        var origins = host.Registry.ResolveEntries<TContract>(ThreadId)
            .Where(static entry => entry.Origin.Kind == ContributionOriginKind.Plugin)
            .Select(static entry => entry.Origin.Name!)
            .ToArray();
        Assert.True(origins.Length > 0, $"The sample contributed nothing to {typeof(TContract).Name}.");
        ledger.Registered<TContract>();
    }

    private static int ChecklistItemCount(string prompt)
    {
        var start = prompt.IndexOf("## Review checklist", StringComparison.Ordinal);
        Assert.True(start >= 0, "The review-checklist section is missing from the assembled prompt.");
        var end = prompt.IndexOf("\n\n---\n\n", start, StringComparison.Ordinal);
        var section = end < 0 ? prompt[start..] : prompt[start..end];
        return section
            .Split('\n')
            .Count(static line => line.StartsWith("- ", StringComparison.Ordinal));
    }

    private static JsonObject Arguments(params (string Name, string Value)[] values)
    {
        var arguments = new JsonObject();
        foreach (var (name, value) in values)
            arguments[name] = value;
        return arguments;
    }

    private static ToolInvocationRequest Request(string callId) =>
        new(ThreadId, "sample-turn", callId, ToolInvocationAudience.Model);

    /// <summary>The client a composed pipeline ends at, so a middleware that swallows the call is visible.</summary>
    private sealed class CountingChatClient : IChatClient
    {
        internal const string Reply = "inner-client-reply";

        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
