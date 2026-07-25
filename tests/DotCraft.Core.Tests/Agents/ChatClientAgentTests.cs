using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class ChatClientAgentTests
{
    [Fact]
    public async Task StreamingRunUsesExistingHistoryAndCommitsOnlyAfterCompletion()
    {
        using var client = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")])]);
        var runtime = new ChatClientAgent(
            client,
            new ChatOptions { Instructions = "base", ModelId = "model" },
            name: "DotCraft");
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "prior"),
            new(ChatRole.Assistant, "prior-answer")
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in runtime.RunStreamingAsync(
                           new ChatMessage(ChatRole.User, "next"),
                           history))
        {
            updates.Add(update);
            Assert.Equal(2, history.Count);
        }

        Assert.Equal(
            ["prior", "prior-answer", "next"],
            client.RequestMessages.Select(message => message.Text));
        Assert.Equal("base", client.Options?.Instructions);
        Assert.Equal("model", client.Options?.ModelId);
        Assert.Equal(4, history.Count);
        Assert.Equal("next", history[2].Text);
        Assert.Equal("answer", history[3].Text);
        Assert.Equal("DotCraft", Assert.Single(updates).AuthorName);
    }

    [Fact]
    public async Task FailedStreamingRunDoesNotMutateHistory()
    {
        using var client = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial")])],
            failAfterUpdates: true);
        var runtime = new ChatClientAgent(client);
        var history = new List<ChatMessage> { new(ChatRole.User, "prior") };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in runtime.RunStreamingAsync(
                               new ChatMessage(ChatRole.User, "next"),
                               history))
            {
            }
        });

        Assert.Single(history);
        Assert.Equal("prior", history[0].Text);
    }

    [Fact]
    public async Task StringEntryPointsCreateUserMessagesForAggregatedAndStreamingRuns()
    {
        using var client = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "answer")]);
        var agent = new ChatClientAgent(client);
        var aggregatedHistory = new List<ChatMessage>();

        await agent.RunAsync(
            "aggregated",
            aggregatedHistory,
            new ChatClientAgentRunOptions
            {
                ChatOptions = new ChatOptions { ModelId = "request-model" }
            });

        Assert.Equal(ChatRole.User, client.RequestMessages[0].Role);
        Assert.Equal("aggregated", client.RequestMessages[0].Text);
        Assert.Equal("request-model", client.Options?.ModelId);
        Assert.Equal(["aggregated", "answer"], aggregatedHistory.Select(message => message.Text));

        var streamingHistory = new List<ChatMessage>();
        await foreach (var _ in agent.RunStreamingAsync("streaming", streamingHistory))
        {
        }

        Assert.Equal(ChatRole.User, client.RequestMessages[0].Role);
        Assert.Equal("streaming", client.RequestMessages[0].Text);
        Assert.Equal(["streaming", "answer"], streamingHistory.Select(message => message.Text));
    }

    [Fact]
    public async Task AggregatedRunUsesRequestLocalTransformedClient()
    {
        using var originalClient = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "original")]);
        using var transformedClient = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "transformed")]);
        var factoryCallCount = 0;
        var agent = new ChatClientAgent(originalClient);
        var runOptions = new ChatClientAgentRunOptions
        {
            ChatClientFactory = client =>
            {
                factoryCallCount++;
                Assert.Same(originalClient, client);
                return transformedClient;
            }
        };

        var transformedResponse = await agent.RunAsync(
            "with-factory",
            history: null,
            runOptions);
        var originalResponse = await agent.RunAsync("without-factory");

        Assert.Equal("transformed", transformedResponse.Text);
        Assert.Equal("original", originalResponse.Text);
        Assert.Equal(1, factoryCallCount);
        Assert.Equal("with-factory", Assert.Single(transformedClient.RequestMessages).Text);
        Assert.Equal("without-factory", Assert.Single(originalClient.RequestMessages).Text);
    }

    [Fact]
    public async Task StreamingRunUsesTransformedClientOnce()
    {
        using var originalClient = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "original")]);
        using var transformedClient = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "transformed")]);
        var factoryCallCount = 0;
        var agent = new ChatClientAgent(originalClient);
        var runOptions = new ChatClientAgentRunOptions
        {
            ChatClientFactory = client =>
            {
                factoryCallCount++;
                Assert.Same(originalClient, client);
                return transformedClient;
            }
        };

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync(
                           "streaming",
                           new List<ChatMessage>(),
                           runOptions))
        {
            updates.Add(update);
        }

        Assert.Equal("transformed", Assert.Single(updates).Text);
        Assert.Equal(1, factoryCallCount);
        Assert.Equal("streaming", Assert.Single(transformedClient.RequestMessages).Text);
        Assert.Empty(originalClient.RequestMessages);
    }

    [Fact]
    public async Task ChatClientFactoryReturningNullFailsBeforeClientInvocation()
    {
        using var client = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "answer")]);
        var agent = new ChatClientAgent(client);
        var history = new List<ChatMessage> { new(ChatRole.User, "prior") };
        var runOptions = new ChatClientAgentRunOptions
        {
            ChatClientFactory = _ => null!
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync("input", history, runOptions));

        Assert.Contains(nameof(ChatClientAgentRunOptions.ChatClientFactory), error.Message);
        Assert.Equal(["prior"], history.Select(message => message.Text));
        Assert.Empty(client.RequestMessages);
    }

    [Fact]
    public async Task RunOptionsMergeWithoutMutatingDefaultsOrRequest()
    {
        using var client = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "answer")]);
        var defaultTool = AIFunctionFactory.Create(() => "default", name: "DefaultTool");
        var requestTool = AIFunctionFactory.Create(() => "request", name: "RequestTool");
        var defaults = new ChatOptions
        {
            Instructions = "agent",
            ModelId = "agent-model",
            Temperature = 0.2f,
            StopSequences = ["agent-stop"],
            Tools = [defaultTool],
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["agent"] = "default",
                ["shared"] = "agent"
            }
        };
        var request = new ChatOptions
        {
            Instructions = "request",
            ModelId = "request-model",
            StopSequences = ["request-stop"],
            Tools = [requestTool],
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["shared"] = "request"
            }
        };
        var runOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = request,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["run"] = "override"
            },
            ResponseFormat = ChatResponseFormat.Json
        };
        var agent = new ChatClientAgent(
            client,
            new ChatClientAgentOptions
            {
                Id = "agent-id",
                Name = "agent-name",
                Description = "agent-description",
                ChatOptions = defaults
            });

        await agent.RunAsync(
            [new ChatMessage(ChatRole.User, "first"), new ChatMessage(ChatRole.User, "second")],
            runOptions: runOptions);

        Assert.Equal("agent\nrequest", client.Options?.Instructions);
        Assert.Equal("request-model", client.Options?.ModelId);
        Assert.Equal(0.2f, client.Options?.Temperature);
        Assert.Equal(["request-stop", "agent-stop"], client.Options?.StopSequences);
        Assert.Equal(["RequestTool", "DefaultTool"], client.Options?.Tools?.Select(tool => tool.Name));
        Assert.Equal("default", client.Options?.AdditionalProperties?["agent"]);
        Assert.Equal("request", client.Options?.AdditionalProperties?["shared"]);
        Assert.Equal("override", client.Options?.AdditionalProperties?["run"]);
        Assert.Same(ChatResponseFormat.Json, client.Options?.ResponseFormat);
        Assert.Equal("agent", defaults.Instructions);
        Assert.Equal(["agent-stop"], defaults.StopSequences);
        Assert.Single(defaults.Tools!);
        Assert.Equal("request", request.Instructions);
        Assert.Equal(["request-stop"], request.StopSequences);
        Assert.Single(request.Tools!);
        Assert.Equal("agent-id", agent.Id);
        Assert.Equal("agent-name", agent.Name);
        Assert.Equal("agent-description", agent.Description);
    }

    [Fact]
    public async Task ContextProvidersComposeInOrderAndRemainRequestLocal()
    {
        using var client = new RecordingStreamingClient(
            [new ChatResponseUpdate(ChatRole.Assistant, "answer")]);
        var calls = new List<string>();
        var first = new RecordingContextProvider(
            "first",
            calls,
            new AIContext
            {
                Instructions = "first-instructions",
                Messages = [new ChatMessage(ChatRole.System, "first-message")],
                Tools = [AIFunctionFactory.Create(() => "first", name: "FirstTool")]
            });
        var second = new RecordingContextProvider(
            "second",
            calls,
            new AIContext
            {
                Instructions = "second-instructions",
                Messages = [new ChatMessage(ChatRole.System, "second-message")],
                Tools = [AIFunctionFactory.Create(() => "second", name: "SecondTool")]
            });
        var agent = new ChatClientAgent(
            client,
            new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions { Instructions = "base" },
                AIContextProviders = [first, second]
            });
        var history = new List<ChatMessage> { new(ChatRole.User, "prior") };

        await agent.RunAsync(new ChatMessage(ChatRole.User, "input"), history);

        Assert.Equal(
            ["prior", "input", "first-message", "second-message"],
            client.RequestMessages.Select(message => message.Text));
        Assert.Equal("base\nfirst-instructions\nsecond-instructions", client.Options?.Instructions);
        Assert.Equal(["FirstTool", "SecondTool"], client.Options?.Tools?.Select(tool => tool.Name));
        Assert.Equal(["invoke:first", "invoke:second", "invoked:first", "invoked:second"], calls);
        Assert.Equal(["prior", "input", "answer"], history.Select(message => message.Text));
        Assert.Equal("base\nfirst-instructions", second.ObservedInstructions);
        Assert.Equal(["FirstTool"], second.ObservedTools);
        Assert.Same(second, agent.GetService<RecordingContextProvider>("second"));
    }

    [Fact]
    public async Task ContextProvidersObserveFailureAndOriginalFailureWins()
    {
        using var client = new RecordingStreamingClient([], failAfterUpdates: true);
        var calls = new List<string>();
        var provider = new RecordingContextProvider("only", calls, new AIContext())
        {
            FailDuringNotification = true
        };
        var agent = new ChatClientAgent(
            client,
            new ChatClientAgentOptions { AIContextProviders = [provider] });
        var history = new List<ChatMessage>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync(new ChatMessage(ChatRole.User, "input"), history));

        Assert.Equal("stream failed", error.Message);
        Assert.Equal(["invoke:only", "invoked:only"], calls);
        Assert.IsType<InvalidOperationException>(provider.ObservedFailure);
        Assert.Empty(history);
    }

    [Fact]
    public void GetServiceResolvesAgentMetadataOptionsProviderAndInnerClient()
    {
        using var client = new RecordingStreamingClient([]);
        var provider = new RecordingContextProvider("provider", [], new AIContext());
        var agent = new ChatClientAgent(
            client,
            new ChatClientAgentOptions
            {
                Name = "named",
                AIContextProviders = [provider]
            });

        Assert.Same(agent, agent.GetService<ChatClientAgent>());
        Assert.Same(agent.Metadata, agent.GetService<AgentMetadata>());
        Assert.Same(client, agent.GetService<IChatClient>());
        Assert.Same(provider, agent.GetService<RecordingContextProvider>("provider"));
        Assert.Equal("test-provider", agent.Metadata.ProviderName);
        Assert.Equal("named", agent.GetService<ChatClientAgentOptions>()?.Name);
        Assert.Null(agent.GetService<ChatClientAgent>("keyed"));
    }

    private sealed class RecordingStreamingClient(
        IReadOnlyList<ChatResponseUpdate> updates,
        bool failAfterUpdates = false) : IChatClient
    {
        public IReadOnlyList<ChatMessage> RequestMessages { get; private set; } = [];

        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                .ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            RequestMessages = chatMessages.ToList();
            Options = options;
            foreach (var update in updates)
                yield return update;
            if (failAfterUpdates)
                throw new InvalidOperationException("stream failed");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata)
                ? new ChatClientMetadata("test-provider")
                : serviceType.IsInstanceOfType(this)
                    ? this
                    : null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingContextProvider(
        string name,
        List<string> calls,
        AIContext provided) : AIContextProvider
    {
        public bool FailDuringNotification { get; set; }

        public string? ObservedInstructions { get; private set; }

        public IReadOnlyList<string> ObservedTools { get; private set; } = [];

        public Exception? ObservedFailure { get; private set; }

        protected override ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext context,
            CancellationToken cancellationToken = default)
        {
            calls.Add($"invoke:{name}");
            ObservedInstructions = context.AIContext.Instructions;
            ObservedTools = context.AIContext.Tools?.Select(tool => tool.Name).ToList() ?? [];
            return ValueTask.FromResult(provided);
        }

        protected override ValueTask InvokedCoreAsync(
            InvokedContext context,
            CancellationToken cancellationToken)
        {
            calls.Add($"invoked:{name}");
            ObservedFailure = context.InvokeException;
            return FailDuringNotification
                ? ValueTask.FromException(new ApplicationException("provider notification failed"))
                : ValueTask.CompletedTask;
        }

        public override object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this)
            && serviceKey is string key
            && string.Equals(key, name, StringComparison.Ordinal)
                ? this
                : base.GetService(serviceType, serviceKey);
    }
}
