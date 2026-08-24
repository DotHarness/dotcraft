using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class AgentContextContributionTests : IDisposable
{
    private readonly ContributionAgentHost _host = new("AgentCtxContribution");

    [Fact]
    public void CreateAgentWithTools_OrdersContributedProvidersAroundTheMemoryEntry()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new StubContextContribution("after"),
            new ContributionOptions(Order: AgentContextSourceCatalog.MemoryOrder + 10));
        registry.Add<IAgentContextSource>(
            new StubContextContribution("before"),
            new ContributionOptions(Order: AgentContextSourceCatalog.MemoryOrder - 10));

        var agent = CreateFactory(registry).CreateAgentWithTools([]);

        // The memory entry is an ordinary ordered contribution now; it composes at Order 100 like any other.
        Assert.Equal(3, agent.AIContextProviders.Count);
        Assert.Equal("before", Assert.IsType<StubContextProvider>(agent.AIContextProviders[0]).Label);
        Assert.IsType<MemoryContextProvider>(agent.AIContextProviders[1]);
        Assert.Equal("after", Assert.IsType<StubContextProvider>(agent.AIContextProviders[2]).Label);
    }

    [Fact]
    public void CreateAgentWithTools_WithoutContributions_KeepsOnlyTheMemoryProvider()
    {
        var agent = CreateFactory(contributions: null).CreateAgentWithTools([]);

        Assert.Single(agent.AIContextProviders);
        Assert.IsType<MemoryContextProvider>(agent.AIContextProviders[0]);
    }

    [Fact]
    public void CreateAgentWithTools_WithARegistryThatNeverRegisteredTheBuiltIn_StillComposesTheMemoryProvider()
    {
        var registry = new ContributionRegistry();
        registry.Add<IAgentContextSource>(
            new StubContextContribution("only"),
            new ContributionOptions(Order: AgentContextSourceCatalog.MemoryOrder + 10));

        var agent = CreateFactory(registry).CreateAgentWithTools([]);

        Assert.Equal(2, agent.AIContextProviders.Count);
        Assert.IsType<MemoryContextProvider>(agent.AIContextProviders[0]);
        Assert.Equal("only", Assert.IsType<StubContextProvider>(agent.AIContextProviders[1]).Label);
    }

    [Fact]
    public void CreateAgentWithTools_AppliesAThreadScopedContributionToThatThreadOnly()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new StubContextContribution("thread-only"),
            ContributionOptions.ForThread("thread-1", order: AgentContextSourceCatalog.MemoryOrder + 10));

        var owning = CreateFactory(registry, threadId: "thread-1").CreateAgentWithTools([]);
        var other = CreateFactory(registry, threadId: "thread-2").CreateAgentWithTools([]);

        Assert.Equal(2, owning.AIContextProviders.Count);
        Assert.Equal("thread-only", Assert.IsType<StubContextProvider>(owning.AIContextProviders[1]).Label);
        Assert.Single(other.AIContextProviders);
    }

    [Fact]
    public void CreateAgentWithTools_SkipsAContributionThatThrowsOrDeclines()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new ThrowingContextContribution(),
            new ContributionOptions(Order: 201));
        registry.Add<IAgentContextSource>(
            new AgentContextSource(static _ => null),
            new ContributionOptions(Order: 202));
        registry.Add<IAgentContextSource>(
            new StubContextContribution("survivor"),
            new ContributionOptions(Order: 203));

        var agent = CreateFactory(registry).CreateAgentWithTools([]);

        Assert.Equal(2, agent.AIContextProviders.Count);
        Assert.IsType<MemoryContextProvider>(agent.AIContextProviders[0]);
        Assert.Equal("survivor", Assert.IsType<StubContextProvider>(agent.AIContextProviders[1]).Label);
    }

    [Fact]
    public async Task MemoryProvider_ComposesTheSamePromptFromTheRegistryAsFromTheFallback()
    {
        var withBuiltIn = new ContributionRegistry();
        SystemPromptSectionCatalog.RegisterBuiltIns(withBuiltIn);
        AgentContextSourceCatalog.RegisterBuiltIns(withBuiltIn);
        var withoutBuiltIn = new ContributionRegistry();
        SystemPromptSectionCatalog.RegisterBuiltIns(withoutBuiltIn);

        var registered = await InstructionsAsync(CreateFactory(withBuiltIn));
        var unregistered = await InstructionsAsync(CreateFactory(withoutBuiltIn));
        var viewless = await InstructionsAsync(CreateFactory(contributions: null));

        Assert.False(string.IsNullOrWhiteSpace(registered));
        Assert.Equal(registered, unregistered);
        Assert.Equal(registered, viewless);
    }

    [Fact]
    public void MemoryTarget_ReplacedByAContribution_SubstitutesTheBuiltInAndRestoresItOnDisposal()
    {
        var registry = CreateRegistry();
        var handle = registry.Add<IAgentContextSource>(
            new StubContextContribution("replacement"),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));
        var factory = CreateFactory(registry);

        var replaced = factory.CreateAgentWithTools([]);
        Assert.Equal(
            "replacement",
            Assert.IsType<StubContextProvider>(Assert.Single(replaced.AIContextProviders)).Label);

        handle.Dispose();

        Assert.IsType<MemoryContextProvider>(Assert.Single(factory.CreateAgentWithTools([]).AIContextProviders));
    }

    [Fact]
    public void MemoryTarget_ReplacementThatDeclines_FallsBackToTheBuiltIn()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new AgentContextSource(static _ => null),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));

        var agent = CreateFactory(registry).CreateAgentWithTools([]);

        // Without the fallback a declining replacement would ship an agent with no system prompt at all.
        Assert.IsType<MemoryContextProvider>(Assert.Single(agent.AIContextProviders));
    }

    [Fact]
    public void MemoryTarget_ThreadScopedReplacement_BeatsTheWorkspaceScopedOne()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new StubContextContribution("workspace"),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));
        registry.Add<IAgentContextSource>(
            new StubContextContribution("thread"),
            new ContributionOptions(
                ContributionScope.Thread,
                "thread-1",
                ReplaceTarget: AgentContextSourceNames.Memory));

        var owning = CreateFactory(registry, threadId: "thread-1").CreateAgentWithTools([]);
        var other = CreateFactory(registry, threadId: "thread-2").CreateAgentWithTools([]);

        Assert.Equal(
            "thread",
            Assert.IsType<StubContextProvider>(Assert.Single(owning.AIContextProviders)).Label);
        Assert.Equal(
            "workspace",
            Assert.IsType<StubContextProvider>(Assert.Single(other.AIContextProviders)).Label);
    }

    [Fact]
    public void MemoryTarget_TwoReplacements_LeaveExactlyOneEffectiveProvider()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new StubContextContribution("loser"),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));
        registry.Add<IAgentContextSource>(
            new StubContextContribution("winner"),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));

        var agent = CreateFactory(registry).CreateAgentWithTools([]);

        Assert.Equal(
            "winner",
            Assert.IsType<StubContextProvider>(Assert.Single(agent.AIContextProviders)).Label);
    }

    [Fact]
    public async Task PromptCacheBaseline_RecordsTheEffectivePromptWhenTheMemoryTargetIsReplaced()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new StubContextContribution("replacement"),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));
        var store = new TraceStore();
        var collector = new TraceCollector(store);
        var factory = CreateFactory(registry, traceCollector: collector);
        var sessionKey = "agentctx-" + Guid.NewGuid().ToString("N")[..8];
        var previous = TracingChatClient.CurrentSessionKey;

        try
        {
            TracingChatClient.CurrentSessionKey = sessionKey;
            await InvokeProvidersAsync(factory.CreateAgentWithTools([]));
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }

        // The baseline must be the prompt actually sent, not the host's, or drift detection compares two prompts.
        collector.RecordSessionMetadata("control", "replacement", []);
        Assert.Equal(store.GetSession("control")?.SystemPromptHash, store.GetSession(sessionKey)?.SystemPromptHash);
    }

    [Fact]
    public void MemoryTarget_Replacement_ReceivesTheBuildTimePromptInputs()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(
            new StubContextContribution("replacement"),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));

        var agent = CreateFactory(registry)
            .CreateAgentWithTools([AIFunctionFactory.Create(() => "ok", "Ping")]);

        var provider = Assert.IsType<StubContextProvider>(Assert.Single(agent.AIContextProviders));
        Assert.NotNull(provider.Request.PromptInputs);
        Assert.Equal(["Ping"], provider.Request.PromptInputs.ToolNames);
    }

    [Fact]
    public void CreateAgentWithTools_WithCallerInstructions_ComposesNoProvidersAtAll()
    {
        var registry = CreateRegistry();
        registry.Add<IAgentContextSource>(new StubContextContribution("ignored"));

        var factory = CreateFactory(registry);
        var agent = factory.CreateAgentWithTools(
            [],
            modeManager: null,
            toolContext: factory.RuntimeContext,
            instructions: "be terse");

        // The tool-profile-only path deliberately runs with ChatOptions.Instructions verbatim.
        Assert.Empty(agent.AIContextProviders);
        Assert.Equal("be terse", agent.Instructions);
    }

    [Fact]
    public void Contribution_RegisteredAfterTheAgentIsBuilt_AppliesToTheNextAgent()
    {
        var registry = CreateRegistry();
        var factory = CreateFactory(registry);

        Assert.Single(factory.CreateAgentWithTools([]).AIContextProviders);

        registry.Add<IAgentContextSource>(new StubContextContribution("late-arrival"));

        Assert.Equal(2, factory.CreateAgentWithTools([]).AIContextProviders.Count);
    }

    private static async Task InvokeProvidersAsync(ChatClientAgent agent)
    {
        var context = new AIContext();
        foreach (var provider in agent.AIContextProviders)
            context = await provider.InvokingAsync(new AIContextProvider.InvokingContext(agent, context));
    }

    private static async Task<string> InstructionsAsync(AgentFactory factory)
    {
        var provider = Assert.IsType<MemoryContextProvider>(
            factory.CreateAgentWithTools([]).AIContextProviders[0]);
        return await provider.ProvideInstructionsAsync();
    }

    /// <summary>A registry carrying the built-in memory entry, as a workspace runtime's does.</summary>
    private static ContributionRegistry CreateRegistry()
    {
        var registry = new ContributionRegistry();
        AgentContextSourceCatalog.RegisterBuiltIns(registry);
        return registry;
    }

    private AgentFactory CreateFactory(
        IContributionView? contributions,
        string? threadId = null,
        TraceCollector? traceCollector = null) =>
        _host.CreateFactory(contributions, threadId: threadId, traceCollector: traceCollector);

    public void Dispose() => _host.Dispose();

    private sealed class StubContextContribution(string label) : IAgentContextSource
    {
        public AIContextProvider CreateProvider(AgentContextRequest request) =>
            new StubContextProvider(label, request);
    }

    private sealed class ThrowingContextContribution : IAgentContextSource
    {
        public AIContextProvider CreateProvider(AgentContextRequest request) =>
            throw new InvalidOperationException("contribution is broken");
    }

    private sealed class StubContextProvider(string label, AgentContextRequest request) : AIContextProvider
    {
        public string Label { get; } = label;

        public AgentContextRequest Request { get; } = request;

        protected override ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AIContext { Instructions = Label });
    }
}
