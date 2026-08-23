using System.Text.Json.Nodes;
using DotCraft.Contributions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ToolDispatchStageContributionTests
{
    [Fact]
    public async Task PolicyChain_IsMostRestrictiveAndReportsTheDenyingStageReason()
    {
        var registry = new ContributionRegistry();
        var allowing = new RecordingPolicy("allow", allow: true);
        var denying = new RecordingPolicy("deny", allow: false);
        var behind = new RecordingPolicy("behind", allow: true);
        registry.Add<IToolPolicyEvaluator>(allowing, new ContributionOptions(Order: 100));
        registry.Add<IToolPolicyEvaluator>(denying, new ContributionOptions(Order: 200));
        registry.Add<IToolPolicyEvaluator>(behind, new ContributionOptions(Order: 300));

        var result = await Dispatch(registry);

        Assert.False(result.Success);
        Assert.Equal("policy:deny", result.Error?.Code);
        Assert.Equal(1, allowing.Calls);
        Assert.Equal(1, denying.Calls);
        Assert.Equal(0, behind.Calls);
    }

    [Fact]
    public async Task PolicyChain_AllowsOnlyWhenEveryStageAllows()
    {
        var registry = new ContributionRegistry();
        var first = new RecordingPolicy("first", allow: true);
        var second = new RecordingPolicy("second", allow: true);
        registry.Add<IToolPolicyEvaluator>(first, new ContributionOptions(Order: 100));
        registry.Add<IToolPolicyEvaluator>(second, new ContributionOptions(Order: 200));

        var result = await Dispatch(registry);

        Assert.True(result.Success);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task ApprovalChain_IsMostRestrictiveAndShortCircuits()
    {
        var registry = new ContributionRegistry();
        var denying = new RecordingApproval("reject", allow: false);
        var behind = new RecordingApproval("behind", allow: true);
        registry.Add<IToolApprovalEvaluator>(denying, new ContributionOptions(Order: 100));
        registry.Add<IToolApprovalEvaluator>(behind, new ContributionOptions(Order: 200));

        var result = await Dispatch(registry);

        Assert.False(result.Success);
        Assert.Equal("approval:reject", result.Error?.Code);
        Assert.Equal(0, behind.Calls);
    }

    [Fact]
    public async Task RecorderChain_FansOutAndOneFailureDoesNotStarveTheOthers()
    {
        var registry = new ContributionRegistry();
        var ahead = new RecordingRecorder("ahead");
        var faulty = new RecordingRecorder("faulty", throws: true);
        var behind = new RecordingRecorder("behind");
        registry.Add<IToolInvocationRecorder>(ahead, new ContributionOptions(Order: 100));
        registry.Add<IToolInvocationRecorder>(faulty, new ContributionOptions(Order: 200));
        registry.Add<IToolInvocationRecorder>(behind, new ContributionOptions(Order: 300));

        var result = await Dispatch(registry);

        Assert.True(result.Success);
        Assert.Equal(new[] { "started", "terminal" }, ahead.Phases);
        Assert.Equal(new[] { "started", "terminal" }, behind.Phases);
        Assert.Equal(2, faulty.Attempts);
    }

    [Fact]
    public async Task RecorderChain_ContainsAThrowingRecorderEvenWhenItIsTheOnlyOne()
    {
        var registry = new ContributionRegistry();
        var faulty = new RecordingRecorder("faulty", throws: true);
        registry.Add<IToolInvocationRecorder>(faulty, new ContributionOptions(Order: 100));

        var result = await Dispatch(registry);

        Assert.True(result.Success);
        Assert.Equal(2, faulty.Attempts);
    }

    [Fact]
    public async Task NormalizerChain_FoldsInAscendingOrder()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolResultNormalizer>(new SuffixNormalizer("-first"), new ContributionOptions(Order: 100));
        registry.Add<IToolResultNormalizer>(new SuffixNormalizer("-second"), new ContributionOptions(Order: 200));

        var result = await Dispatch(registry);

        Assert.Equal("ok-first-second", result.Content);
    }

    [Fact]
    public async Task ReplacingANamedDefault_ShadowsItAndRestoresItOnDispose()
    {
        var registry = new ContributionRegistry();
        var builtIn = new RecordingPolicy("built-in", allow: true);
        ToolDispatchStageCatalog.RegisterBuiltIns(registry, builtIn);

        var replacement = registry.Add<IToolPolicyEvaluator>(
            new RecordingPolicy("replacement", allow: false),
            new ContributionOptions { ReplaceTarget = ToolDispatchStageNames.PolicyDefault });

        var replaced = await Dispatch(registry);
        Assert.False(replaced.Success);
        Assert.Equal("policy:replacement", replaced.Error?.Code);
        Assert.Equal(0, builtIn.Calls);

        replacement.Dispose();

        Assert.True((await Dispatch(registry)).Success);
        Assert.Equal(1, builtIn.Calls);
    }

    [Fact]
    public async Task ThreadScopedStage_AppliesToThatThreadOnly()
    {
        var registry = new ContributionRegistry();
        registry.Add<IToolPolicyEvaluator>(
            new RecordingPolicy("thread-only", allow: false),
            ContributionOptions.ForThread("thread-a"));

        Assert.False((await Dispatch(registry, threadId: "thread-a")).Success);
        Assert.True((await Dispatch(registry, threadId: "thread-b")).Success);
    }

    [Fact]
    public async Task EmptyContributionPoint_FallsBackToTheConstructorStageUnchanged()
    {
        var registry = new ContributionRegistry();
        var fallback = new RecordingPolicy("fallback", allow: false);

        var withView = await Dispatch(registry, fallbackPolicy: fallback);
        var withoutView = await Dispatch(contributions: null, fallbackPolicy: fallback);

        Assert.Equal(withoutView.Success, withView.Success);
        Assert.Equal(withoutView.Error?.Code, withView.Error?.Code);
        Assert.Equal("policy:fallback", withView.Error?.Code);
    }

    [Fact]
    public async Task SingleContributedStage_BehavesExactlyLikeTheHardwiredStage()
    {
        var contributed = new RecordingPolicy("only", allow: false);
        var registry = new ContributionRegistry();
        registry.Add<IToolPolicyEvaluator>(contributed);

        var throughRegistry = await Dispatch(registry);
        var hardwired = await Dispatch(
            contributions: null,
            fallbackPolicy: new RecordingPolicy("only", allow: false));

        Assert.Equal(hardwired.Success, throughRegistry.Success);
        Assert.Equal(hardwired.Error?.Code, throughRegistry.Error?.Code);
        Assert.Equal(hardwired.Error?.Message, throughRegistry.Error?.Message);
    }

    [Fact]
    public async Task ContributedStages_AreResolvedPerDispatchNotAtConstruction()
    {
        var registry = new ContributionRegistry();
        var dispatcher = CreateDispatcher(registry);

        Assert.True((await Dispatch(dispatcher)).Success);

        var handle = registry.Add<IToolPolicyEvaluator>(new RecordingPolicy("late", allow: false));

        Assert.False((await Dispatch(dispatcher)).Success);
        handle.Dispose();
        Assert.True((await Dispatch(dispatcher)).Success);
    }

    private static ToolDispatcher CreateDispatcher(
        IContributionView? contributions,
        IToolPolicyEvaluator? fallbackPolicy = null) =>
        new(
            policyEvaluator: fallbackPolicy,
            contributions: contributions);

    private static Task<ToolExecutionResult> Dispatch(
        IContributionView? contributions,
        IToolPolicyEvaluator? fallbackPolicy = null,
        string threadId = "thread") =>
        Dispatch(CreateDispatcher(contributions, fallbackPolicy), threadId);

    private static async Task<ToolExecutionResult> Dispatch(
        ToolDispatcher dispatcher,
        string threadId = "thread")
    {
        var registration = Registration();
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);
        return await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            new ToolInvocationRequest(threadId, "turn", "call", ToolInvocationAudience.Model));
    }

    private static ToolRegistration Registration() =>
        ContributionTools.Registration(
            new ToolName("workspace", "probe"),
            sourceId: "source-probe",
            audiences: ToolInvocationAudience.Model | ToolInvocationAudience.Host);

    private sealed class RecordingPolicy(string name, bool allow) : IToolPolicyEvaluator
    {
        public int Calls { get; private set; }

        public ValueTask<ToolDispatchDecision> EvaluateAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(allow
                ? ToolDispatchDecision.Allow
                : ToolDispatchDecision.Deny($"policy:{name}", $"Denied by {name}."));
        }
    }

    private sealed class RecordingApproval(string name, bool allow) : IToolApprovalEvaluator
    {
        public int Calls { get; private set; }

        public ValueTask<ToolDispatchDecision> RequestAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(allow
                ? ToolDispatchDecision.Allow
                : ToolDispatchDecision.Deny($"approval:{name}", $"Rejected by {name}."));
        }
    }

    private sealed class RecordingRecorder(string name, bool throws = false) : IToolInvocationRecorder
    {
        public List<string> Phases { get; } = [];

        public int Attempts { get; private set; }

        public ValueTask RecordStartedAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            JsonObject arguments,
            CancellationToken cancellationToken = default) => Record("started");

        public ValueTask RecordTerminalAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            TimeSpan duration,
            CancellationToken cancellationToken = default) => Record("terminal");

        private ValueTask Record(string phase)
        {
            Attempts++;
            if (throws)
                throw new InvalidOperationException($"Recorder {name} failed on {phase}.");
            Phases.Add(phase);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SuffixNormalizer(string suffix) : IToolResultNormalizer
    {
        public ValueTask<ToolExecutionResult> NormalizeAsync(
            ToolInvocationContext context,
            ToolRegistration registration,
            ToolExecutionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded(result.Content + suffix));
    }
}
