using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadTitleGenerationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "ThreadTitles_" + Guid.NewGuid().ToString("N")[..8]);

    public ThreadTitleGenerationTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task FirstUserMessage_PersistsProvisionalWithoutWaitingForGeneratedTitle()
    {
        var generator = new ControlledTitleGenerator();
        await using var factory = CreateAgentFactory(new StaticChatClient("main response"));
        var service = CreateService(factory, generator);
        var renamed = new List<string>();
        service.ThreadRenamedForBroadcast = thread => renamed.Add(thread.DisplayName ?? string.Empty);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var input = "  Fix   login\nflow " + new string('x', 80);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent(input)]));
        await generator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var provisional = ThreadTitleText.CreateProvisionalTitle(input);
        Assert.NotNull(provisional);
        Assert.Equal(36, provisional.EnumerateRunes().Count());
        Assert.Equal(provisional, (await service.GetThreadAsync(thread.Id)).DisplayName);
        Assert.Equal(provisional, Assert.Single(renamed));

        generator.Complete("Fix login flow");
        await WaitUntilAsync(
            async () => (await service.GetThreadAsync(thread.Id)).DisplayName == "Fix login flow"
                && renamed.Count == 2);

        Assert.Equal(new[] { provisional, "Fix login flow" }, renamed);
        var persisted = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        Assert.Equal("Fix login flow", persisted?.DisplayName);
    }

    [Fact]
    public async Task GeneratedTitle_DoesNotReplaceInterveningManualRename()
    {
        await using var factory = CreateAgentFactory(new StaticChatClient("main response"));
        var service = CreateService(factory, new ControlledTitleGenerator());
        var thread = await service.CreateThreadAsync(MakeIdentity(), displayName: "Provisional");
        await service.RenameThreadAsync(thread.Id, "User title");

        var applied = await service.TryApplyGeneratedThreadTitleAsync(
            thread.Id,
            "Provisional",
            "Generated title",
            CancellationToken.None);

        Assert.False(applied);
        Assert.Equal("User title", (await service.GetThreadAsync(thread.Id)).DisplayName);
        Assert.Equal("User title", (await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id))?.DisplayName);
    }

    [Fact]
    public async Task IneligibleThreads_DoNotGenerateTitles()
    {
        var generator = new CountingTitleGenerator();
        await using var factory = CreateAgentFactory(new StaticChatClient("main response"));
        var service = CreateService(factory, generator);

        var explicitThread = await service.CreateThreadAsync(MakeIdentity(), displayName: "Explicit");
        await DrainAsync(service.SubmitInputAsync(explicitThread.Id, [new TextContent("first request")]));

        var parent = await service.CreateThreadAsync(MakeIdentity(), displayName: "Parent");
        var subAgent = await service.CreateThreadAsync(
            MakeIdentity(),
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                RootThreadId = parent.Id,
                Depth = 1
            }));
        await DrainAsync(service.SubmitInputAsync(subAgent.Id, [new TextContent("child request")]));

        var internalThread = await service.CreateThreadAsync(MakeIdentity());
        internalThread.Metadata[ThreadVisibility.InternalMetadataKey] = "test";
        await DrainAsync(service.SubmitInputAsync(internalThread.Id, [new TextContent("internal request")]));

        var fork = await service.ForkThreadAsync(parent.Id);
        await DrainAsync(service.SubmitInputAsync(fork.Id, [new TextContent("fork request")]));

        var attachmentOnly = await service.CreateThreadAsync(MakeIdentity());
        await DrainAsync(service.SubmitInputAsync(
            attachmentOnly.Id,
            [new DataContent(new byte[] { 1, 2, 3 }, "application/octet-stream")]));

        Assert.Equal(0, generator.CallCount);
        Assert.Null((await service.GetThreadAsync(attachmentOnly.Id)).DisplayName);
    }

    [Fact]
    public async Task GenerationFailure_LeavesProvisionalTitleAndDoesNotRetry()
    {
        var generator = new ThrowingTitleGenerator();
        await using var factory = CreateAgentFactory(new StaticChatClient("main response"));
        var service = CreateService(factory, generator);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Investigate startup failure")]));
        await generator.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("More detail")]));

        Assert.Equal(1, generator.CallCount);
        Assert.Equal("Investigate startup failure", (await service.GetThreadAsync(thread.Id)).DisplayName);
    }

    private SessionService CreateService(AgentFactory factory, IThreadTitleGenerator generator)
    {
        var service = new SessionService(
            factory,
            new StaticChatClient("main response").AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
        service.ThreadTitleGenerator = generator;
        return service;
    }

    private AgentFactory CreateAgentFactory(IChatClient chatClient)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolSources: []);
    }

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "user",
        WorkspacePath = _tempDir
    };

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await predicate())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledTitleGenerator : IThreadTitleGenerator
    {
        private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> GenerateAsync(
            ThreadTitleGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return _result.Task;
        }

        public void Complete(string title) => _result.TrySetResult(title);
    }

    private sealed class CountingTitleGenerator : IThreadTitleGenerator
    {
        public int CallCount;

        public Task<string?> GenerateAsync(
            ThreadTitleGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult<string?>("Generated");
        }
    }

    private sealed class ThrowingTitleGenerator : IThreadTitleGenerator
    {
        public int CallCount;

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> GenerateAsync(
            ThreadTitleGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            Finished.TrySetResult();
            throw new InvalidOperationException("title failure");
        }
    }
}

public sealed class ModelThreadTitleGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_UsesSubAgentModelLowReasoningAndStrictBoundedSchema()
    {
        var client = new RecordingTitleChatClient("{\"title\":\"Fix login flow\"}");
        var provider = new RecordingModelProvider(client);
        var registry = new ChatClientRegistry(provider);
        var config = AppConfigTestFactory.CreateOpenAI(model: "main-model");
        config.SubAgent.ProviderPreferences[config.ProviderId] = new ModelPreference { Model = "title-model" };
        var generator = new ModelThreadTitleGenerator(registry, () => config);
        var longPrompt = string.Concat(Enumerable.Repeat("修复登录流程 ", 200));

        var title = await generator.GenerateAsync(new ThreadTitleGenerationRequest(
            "thread-1",
            "provisional",
            longPrompt,
            config.ProviderId,
            "main-model"));

        Assert.Equal("Fix login flow", title);
        Assert.Equal("title-model", Assert.Single(provider.Runtimes).Model);
        Assert.NotNull(client.Options);
        var options = client.Options;
        Assert.Empty(options.Tools ?? []);
        Assert.Equal(ReasoningEffort.Low, options.Reasoning?.Effort);
        var responseFormat = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.NotNull(responseFormat.Schema);
        var schema = responseFormat.Schema.Value;
        var titleSchema = schema.GetProperty("properties").GetProperty("title");
        Assert.Equal("string", titleSchema.GetProperty("type").GetString());
        Assert.Equal(1, titleSchema.GetProperty("minLength").GetInt32());
        Assert.Equal(36, titleSchema.GetProperty("maxLength").GetInt32());
        Assert.Equal("title", Assert.Single(schema.GetProperty("required").EnumerateArray()).GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var prompt = Assert.Single(client.Messages).Contents.OfType<TextContent>().Single().Text;
        Assert.InRange(Encoding.UTF8.GetByteCount(prompt), 1, 960);
    }

    [Fact]
    public async Task GenerateAsync_NormalizesTitleAndRejectsInvalidStructuredOutput()
    {
        var normalized = await GenerateAsync("{\"title\":\"“修复   登录流程！”\"}");
        var truncated = await GenerateAsync("{\"title\":\"" + string.Concat(Enumerable.Repeat("🧵", 40)) + "\"}");
        var unknownProperty = await GenerateAsync("{\"title\":\"Fix login\",\"extra\":true}");
        var invalidJson = await GenerateAsync("```json\n{\"title\":\"Fix login\"}\n```");
        var emptyTitle = await GenerateAsync("{\"title\":\"  \"}");

        Assert.Equal("修复 登录流程", normalized);
        Assert.Equal(36, truncated?.EnumerateRunes().Count());
        Assert.Null(unknownProperty);
        Assert.Null(invalidJson);
        Assert.Null(emptyTitle);
    }

    [Fact]
    public async Task GenerateAsync_TimesOutWithoutRetrying()
    {
        var client = new RecordingTitleChatClient(response: null);
        var provider = new RecordingModelProvider(client);
        var registry = new ChatClientRegistry(provider);
        var config = AppConfigTestFactory.CreateOpenAI();
        var generator = new ModelThreadTitleGenerator(registry, () => config, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generator.GenerateAsync(
            new ThreadTitleGenerationRequest("thread-1", "provisional", "Fix login", config.ProviderId, "main")));

        Assert.Equal(1, client.CallCount);
    }

    private static async Task<string?> GenerateAsync(string response)
    {
        var client = new RecordingTitleChatClient(response);
        var provider = new RecordingModelProvider(client);
        var registry = new ChatClientRegistry(provider);
        var config = AppConfigTestFactory.CreateOpenAI();
        var generator = new ModelThreadTitleGenerator(registry, () => config);
        return await generator.GenerateAsync(
            new ThreadTitleGenerationRequest("thread-1", "provisional", "Fix login", config.ProviderId, config.ProviderPreferences[config.ProviderId].Model));
    }
}

internal sealed class RecordingModelProvider(IChatClient client) : IModelProvider
{
    public IReadOnlyCollection<string> Protocols { get; } = [ModelProviderProtocols.OpenAI];

    public List<EffectiveModelRuntime> Runtimes { get; } = [];

    public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
    {
        Runtimes.Add(runtime);
        return client;
    }
}

internal sealed class RecordingTitleChatClient(string? response) : IChatClient
{
    public int CallCount { get; private set; }

    public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

    public ChatOptions? Options { get; private set; }

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        Messages = messages.ToArray();
        Options = options;
        if (response == null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, response));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}

internal sealed class StaticChatClient(string response) : IChatClient
{
    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, response);
        await Task.CompletedTask;
    }
}
