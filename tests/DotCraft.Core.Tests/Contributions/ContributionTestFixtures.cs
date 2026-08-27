using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tests.Contributions;

/// <summary>A throwaway workspace plus the agent factories a contribution test asserts its reader through, so
/// each contribution test states only what it varies. Disposing the host disposes every factory it handed out.</summary>
internal sealed class ContributionAgentHost : IDisposable
{
    private readonly List<AgentFactory> _factories = [];

    internal ContributionAgentHost(string name)
    {
        WorkspacePath = Path.Combine(Path.GetTempPath(), $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkspacePath);
    }

    /// <summary>Gets the workspace every factory of this host is rooted at; it doubles as the DotCraft data directory.</summary>
    internal string WorkspacePath { get; }

    internal AgentFactory CreateFactory(
        IContributionView? contributions,
        IChatClient? chatClient = null,
        IReadOnlyList<IToolSource>? toolSources = null,
        string? threadId = null,
        TraceCollector? traceCollector = null)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        var chatClientRegistry = TestModelProviderRegistry.Create();
        var memoryStore = new MemoryStore(WorkspacePath);
        var skillsLoader = new SkillsLoader(WorkspacePath);
        var factory = new AgentFactory(
            dotcraftPath: WorkspacePath,
            workspacePath: WorkspacePath,
            config: config,
            memoryStore: memoryStore,
            skillsLoader: skillsLoader,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            traceCollector: traceCollector,
            runtimeContext: new AgentRuntimeContext
            {
                Config = config,
                ChatClient = chatClient ?? chatClientRegistry.GetMainChatClient(config),
                ChatClientRegistry = chatClientRegistry,
                EffectiveProviderId = config.ProviderId,
                EffectiveProviderProtocol = ModelProviderProtocols.OpenAIChatCompletions,
                EffectiveMainModel = "gpt-4o-mini",
                WorkspacePath = WorkspacePath,
                BotPath = WorkspacePath,
                MemoryStore = memoryStore,
                SkillsLoader = skillsLoader,
                ContextPageManager = new ContextPageManager(),
                ApprovalService = new AutoApproveApprovalService(),
                CurrentThreadId = threadId,
                Contributions = contributions
            },
            chatClientRegistry: chatClientRegistry,
            chatClient: chatClient,
            toolSources: toolSources);
        _factories.Add(factory);
        return factory;
    }

    public void Dispose()
    {
        foreach (var factory in _factories)
            factory.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { Directory.Delete(WorkspacePath, recursive: true); }
        catch { }
    }
}

/// <summary>Captures the formatted warnings an operator would see from a contribution dispatcher.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_warnings)
                return _warnings.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_warnings);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<string> warnings) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
                return;
            // The dispatcher pumps run off the calling thread; a test reads the list from its own.
            lock (warnings)
                warnings.Add(formatter(state, exception));
        }
    }
}

/// <summary>A chat client that answers with a fixed text and remembers the instructions it was sent.</summary>
internal sealed class ContributionChatClient(string responseText = "ok") : IChatClient
{
    public string? LastInstructions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastInstructions = options?.Instructions;
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LastInstructions = options?.Instructions;
        yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>A chat client whose every call fails, so a turn reaches its failing terminal state.</summary>
internal sealed class FailingContributionChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("model is unavailable");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("model is unavailable");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

/// <summary>Builds the tool registrations a contribution test plans and dispatches through.</summary>
internal static class ContributionTools
{
    private static readonly System.Text.Json.JsonElement EmptyObjectSchema =
        System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    internal static ToolRegistration Registration(
        ToolName name,
        string sourceId = "stub",
        ToolExposure exposure = ToolExposure.Direct,
        ToolPolicyScope policyScope = ToolPolicyScope.ProfileManaged,
        ToolInvocationAudience audiences =
            ToolInvocationAudience.Model | ToolInvocationAudience.Host | ToolInvocationAudience.App)
    {
        var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, sourceId, new SourceToolId(name.Name));
        var definition = new ToolDefinition(
            definitionId,
            name,
            $"Run {name.Name}",
            EmptyObjectSchema,
            policyScope: policyScope);
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"binding-{name.Name}"),
            definitionId,
            new ContributionToolRuntime(),
            ToolBindingLeases.AlwaysAvailable,
            $"authority:{name.Name}",
            revision: 1);
        return new ToolRegistration(definition, binding, ToolProjectionShape.StandardPair, exposure, audiences);
    }

    private sealed class ContributionToolRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            System.Text.Json.Nodes.JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }
}
