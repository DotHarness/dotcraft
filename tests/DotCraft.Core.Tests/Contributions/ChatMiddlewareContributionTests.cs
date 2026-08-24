using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ChatMiddlewareContributionTests
{
    [Fact]
    public void AgentPipeline_MatchesTheBuilderChain_WithTracingAndDeferredTools()
    {
        var host = CreateHost(tracing: true, deferredTools: true);
        var expected = BuildExpectedChain(builder =>
        {
            builder.Use(inner => new TracingChatClient(inner, host.TraceCollector!));
            builder.Use(inner => host.CreateFunctionInvokingClient!(inner));
            builder.Use(inner => new DynamicToolInjectionChatClient(
                inner, host.DeferredTools!, host.TraceCollector, host.HookRunner));
            builder.Use(inner => new ImageContentSanitizingChatClient(inner));
        });

        AssertChain(expected, Compose(ChatPipelineKind.Agent, host));
    }

    [Fact]
    public void AgentPipeline_MatchesTheBuilderChain_WithoutTracingOrDeferredTools()
    {
        var host = CreateHost(tracing: false, deferredTools: false);
        var expected = BuildExpectedChain(builder =>
        {
            builder.Use(inner => host.CreateFunctionInvokingClient!(inner));
            builder.Use(inner => new ImageContentSanitizingChatClient(inner));
        });

        var actual = Compose(ChatPipelineKind.Agent, host);

        AssertChain(expected, actual);
        Assert.DoesNotContain(nameof(TracingChatClient), ChainTypes(actual));
        Assert.DoesNotContain(nameof(DynamicToolInjectionChatClient), ChainTypes(actual));
    }

    [Fact]
    public void SubAgentPipeline_MatchesTheBuilderChain()
    {
        var host = CreateHost(tracing: true, deferredTools: true, subAgentProgress: true);
        var expected = BuildExpectedChain(builder =>
        {
            builder.Use(inner => host.CreateFunctionInvokingClient!(inner));
            builder.Use(inner => new SubAgentProgressChatClient(inner, host.SubAgentProgress!));
            builder.Use(inner => new TracingChatClient(inner, host.TraceCollector!));
        });

        AssertChain(expected, Compose(ChatPipelineKind.SubAgent, host));
    }

    [Fact]
    public void SubAgentPipeline_WithoutProgressEntry_OmitsTheProgressClient()
    {
        var host = CreateHost(tracing: true, deferredTools: false);
        var expected = BuildExpectedChain(builder =>
        {
            builder.Use(inner => host.CreateFunctionInvokingClient!(inner));
            builder.Use(inner => new TracingChatClient(inner, host.TraceCollector!));
        });

        AssertChain(expected, Compose(ChatPipelineKind.SubAgent, host));
    }

    [Fact]
    public void DeferredTools_OutsideSimulatedMode_OmitDynamicInjection()
    {
        var host = new ChatPipelineHostInputs
        {
            DeferredTools = new DeferredToolActivationIndex(
                Array.Empty<DeferredToolEntry>(),
                DeferredToolLoadingMode.Native)
        };

        Assert.DoesNotContain(
            nameof(DynamicToolInjectionChatClient),
            ChainTypes(Compose(ChatPipelineKind.Agent, host)));
    }

    [Fact]
    public void Contribution_LandsAtItsOrderPosition()
    {
        var registry = CreateRegistry();
        // Straddle the built-in image sanitizer, which is registered at order 700.
        registry.Add<IChatMiddleware>(new MarkerMiddleware("outer"), new ContributionOptions(Order: 650));
        registry.Add<IChatMiddleware>(new MarkerMiddleware("inner"), new ContributionOptions(Order: 750));

        var chain = ChainTypes(Compose(ChatPipelineKind.Agent, CreateHost(), registry));

        AssertOrder(
            chain,
            $"{nameof(MarkerChatClient)}:outer",
            nameof(ImageContentSanitizingChatClient),
            $"{nameof(MarkerChatClient)}:inner");
    }

    [Fact]
    public void Contribution_AppliesOnlyToTheKindsItAccepts()
    {
        var registry = CreateRegistry();
        registry.Add<IChatMiddleware>(
            new MarkerMiddleware("agent-only", ChatPipelineKind.Agent),
            new ContributionOptions(Order: 50));

        Assert.Contains(
            $"{nameof(MarkerChatClient)}:agent-only",
            ChainTypes(Compose(ChatPipelineKind.Agent, CreateHost(), registry)));
        Assert.DoesNotContain(
            $"{nameof(MarkerChatClient)}:agent-only",
            ChainTypes(Compose(ChatPipelineKind.SubAgent, CreateHost(), registry)));
    }

    [Fact]
    public async Task ShortCircuitingContribution_NeverCallsTheInnerPipeline()
    {
        var registry = CreateRegistry();
        registry.Add<IChatMiddleware>(new ShortCircuitMiddleware(), new ContributionOptions(Order: 50));
        var baseClient = new ProbeChatClient();

        var pipeline = ChatMiddlewareCatalog.Compose(
            registry,
            baseClient,
            new ChatPipelineContext(ChatPipelineKind.Agent) { Host = CreateHost() });
        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("short-circuited", response.Text);
        Assert.Equal(0, baseClient.Calls);
    }

    [Fact]
    public void ThreadScopedContribution_AppliesToThatThreadOnly()
    {
        var registry = CreateRegistry();
        registry.Add<IChatMiddleware>(
            new MarkerMiddleware("thread-a"),
            ContributionOptions.ForThread("thread-a", order: 50));

        Assert.Contains(
            $"{nameof(MarkerChatClient)}:thread-a",
            ChainTypes(Compose(ChatPipelineKind.Agent, CreateHost(), registry, "thread-a")));
        Assert.DoesNotContain(
            $"{nameof(MarkerChatClient)}:thread-a",
            ChainTypes(Compose(ChatPipelineKind.Agent, CreateHost(), registry, "thread-b")));
        Assert.DoesNotContain(
            $"{nameof(MarkerChatClient)}:thread-a",
            ChainTypes(Compose(ChatPipelineKind.Agent, CreateHost(), registry)));
    }

    [Fact]
    public void Replacement_ShadowsTheNamedBuiltInUntilDisposed()
    {
        var registry = CreateRegistry();
        var handle = registry.Add<IChatMiddleware>(
            new MarkerMiddleware("sanitizer-swap"),
            new ContributionOptions(Order: 700, ReplaceTarget: ChatMiddlewareNames.ImageSanitizing));

        var replaced = ChainTypes(Compose(ChatPipelineKind.Agent, CreateHost(), registry));
        Assert.Contains($"{nameof(MarkerChatClient)}:sanitizer-swap", replaced);
        Assert.DoesNotContain(nameof(ImageContentSanitizingChatClient), replaced);

        handle.Dispose();

        var restored = ChainTypes(Compose(ChatPipelineKind.Agent, CreateHost(), registry));
        Assert.Contains(nameof(ImageContentSanitizingChatClient), restored);
        Assert.DoesNotContain($"{nameof(MarkerChatClient)}:sanitizer-swap", restored);
    }

    private static ContributionRegistry CreateRegistry()
    {
        var registry = new ContributionRegistry();
        ChatMiddlewareCatalog.RegisterBuiltIns(registry);
        return registry;
    }

    private static ChatPipelineHostInputs CreateHost(
        bool tracing = false,
        bool deferredTools = false,
        bool subAgentProgress = false) =>
        new()
        {
            TraceCollector = tracing ? new TraceCollector(new TraceStore()) : null,
            DeferredTools = deferredTools ? new DeferredToolActivationIndex([]) : null,
            SubAgentProgress = subAgentProgress ? new SubAgentProgressBridge.ProgressEntry() : null,
            CreateFunctionInvokingClient = inner => new StreamingFunctionInvokingChatClient(inner)
        };

    private static IChatClient Compose(
        ChatPipelineKind kind,
        ChatPipelineHostInputs host,
        IContributionView? contributions = null,
        string? threadId = null) =>
        ChatMiddlewareCatalog.Compose(
            contributions,
            new ProbeChatClient(),
            new ChatPipelineContext(kind, threadId) { Host = host });

    private static IChatClient BuildExpectedChain(Action<ChatClientBuilder> register)
    {
        var builder = new ChatClientBuilder(new ProbeChatClient());
        register(builder);
        return builder.Build();
    }

    private static void AssertChain(IChatClient expected, IChatClient actual) =>
        Assert.Equal(ChainTypes(expected), ChainTypes(actual));

    private static void AssertOrder(IReadOnlyList<string> chain, params string[] outerToInner)
    {
        var positions = outerToInner.Select(name =>
        {
            var index = chain.ToList().IndexOf(name);
            Assert.True(index >= 0, $"{name} is missing from [{string.Join(", ", chain)}]");
            return index;
        }).ToList();

        Assert.Equal(positions.OrderBy(position => position).ToList(), positions);
    }

    // Outermost client inwards.
    private static IReadOnlyList<string> ChainTypes(IChatClient client)
    {
        var names = new List<string>();
        var current = client;
        while (current is not null)
        {
            names.Add(current is MarkerChatClient marker
                ? $"{nameof(MarkerChatClient)}:{marker.Label}"
                : current.GetType().Name);
            current = current is DelegatingChatClient delegating ? InnerOf(delegating) : null;
        }

        return names;
    }

    private static IChatClient? InnerOf(DelegatingChatClient client) =>
        typeof(DelegatingChatClient)
            .GetProperty("InnerClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(client) as IChatClient;

    private sealed class MarkerMiddleware(string label, ChatPipelineKind? only = null) : IChatMiddleware
    {
        public string Name => label;

        public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) =>
            only is null || only == context.Kind ? new MarkerChatClient(inner, label) : inner;
    }

    private sealed class MarkerChatClient(IChatClient inner, string label) : DelegatingChatClient(inner)
    {
        public string Label => label;
    }

    private sealed class ShortCircuitMiddleware : IChatMiddleware
    {
        public string Name => "short-circuit";

        public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) => new ShortCircuitChatClient();
    }

    private sealed class ShortCircuitChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "short-circuited")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "short-circuited");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class ProbeChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "inner")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "inner");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
