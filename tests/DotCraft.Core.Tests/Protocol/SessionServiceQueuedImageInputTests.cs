using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceQueuedImageInputTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceQueuedImageInputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QueuedImageInput_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task TryStartNextQueuedTurnAsync_LegacyRemoteImageBecomesModelPlaceholder()
    {
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.EnqueueTurnInputAsync(
            thread.Id,
            [new TextContent("snapshot")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = "legacy image" }],
                MaterializedInputParts =
                [
                    new SessionInputPart { Type = "text", Text = "before" },
                    new SessionInputPart { Type = "image", Url = "http://127.0.0.1:1/image.png" },
                    new SessionInputPart { Type = "text", Text = "after" }
                ],
                DisplayText = "legacy image"
            });

        await service.TryStartNextQueuedTurnAsync(thread.Id);
        await WaitUntilAsync(() =>
            thread.Turns.Count == 1
            && thread.Turns[0].Status == TurnStatus.Completed
            && thread.QueuedInputs.Count == 0);

        var request = Assert.Single(chatClient.CapturedRequests);
        var userMessage = request.Last(message => message.Role == ChatRole.User);
        Assert.Equal(
            ["before", SessionInputPartResolver.RemoteImageOmittedText, "after"],
            userMessage.Contents
                .OfType<TextContent>()
                .Take(3)
                .Select(content => content.Text)
                .ToArray());
    }

    [Fact]
    public async Task TryStartNextQueuedTurnAsync_InlineImageRestoresDataContent()
    {
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.EnqueueTurnInputAsync(
            thread.Id,
            [new DataContent(new byte[] { 1, 2, 3 }, "image/png")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "image", Url = "data:image/png;base64,AQID" }],
                MaterializedInputParts = [new SessionInputPart { Type = "image", Url = "data:image/png;base64,AQID" }],
                DisplayText = "inline image"
            });

        await service.TryStartNextQueuedTurnAsync(thread.Id);
        await WaitUntilAsync(() =>
            thread.Turns.Count == 1
            && thread.Turns[0].Status == TurnStatus.Completed
            && thread.QueuedInputs.Count == 0);

        var request = Assert.Single(chatClient.CapturedRequests);
        var userMessage = request.Last(message => message.Role == ChatRole.User);
        var image = Assert.Single(userMessage.Contents.OfType<DataContent>());
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal([1, 2, 3], image.Data.ToArray());
    }

    [Fact]
    public async Task SubmitInputAsync_GuidancePendingLegacyRemoteImageBecomesModelPlaceholder()
    {
        var chatClient = new GuidanceDrainingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        using var loop = new StreamingFunctionInvokingChatClient(chatClient);
        var service = new SessionService(agentFactory, loop.AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)), new SessionGate());
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var turnTask = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));
        await chatClient.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var queued = await service.EnqueueTurnInputAsync(
            thread.Id,
            [new TextContent("snapshot")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = "legacy guidance" }],
                MaterializedInputParts =
                [
                    new SessionInputPart { Type = "text", Text = "before" },
                    new SessionInputPart { Type = "image", Url = "https://127.0.0.1:1/image.png" },
                    new SessionInputPart { Type = "text", Text = "after" }
                ],
                DisplayText = "legacy guidance"
            });
        var activeTurn = Assert.Single(thread.Turns);
        await service.UpdateQueuedTurnInputAsync(
            thread.Id,
            queued.Id,
            activeTurn.Id,
            "guidancePending");

        chatClient.Release();
        await turnTask;

        var guidance = chatClient.GuidanceMessage;
        Assert.NotNull(guidance);
        Assert.Equal(ChatRole.User, guidance.Role);
        Assert.Equal(
            ["before", SessionInputPartResolver.RemoteImageOmittedText, "after"],
            guidance.Contents
                .OfType<TextContent>()
                .Select(content => content.Text)
                .ToArray());
        Assert.Empty(thread.QueuedInputs);
        Assert.Contains(
            activeTurn.Items,
            item => item.AsUserMessage is { DeliveryMode: "guidance", QueuedInputId: var queuedInputId }
                && string.Equals(queuedInputId, queued.Id, StringComparison.Ordinal));
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient)
    {
        var defaultAgent = chatClient.AsAIAgent();
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
    }

    private AgentFactory CreateAgentFactory(IChatClient chatClient) =>
        new(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: AppConfigTestFactory.CreateOpenAI(),
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolSources: Array.Empty<IToolSource>());

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "u",
        WorkspacePath = _tempDir
    };

    private static async Task<List<SessionEvent>> DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        var collected = new List<SessionEvent>();
        await foreach (var evt in events)
            collected.Add(evt);
        return collected;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, cts.Token);
        }
    }

    private sealed class StaticChatClient(string responseText) : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> CapturedRequests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(chatMessages.ToList());
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(chatMessages.ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class GuidanceDrainingChatClient : IChatClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public ChatMessage? GuidanceMessage { get; private set; }

        public void Release() => _release.TrySetResult();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            GuidanceMessage = chatMessages.LastOrDefault(message => message.Role == ChatRole.User
                && message.Text.Contains(SessionInputPartResolver.RemoteImageOmittedText, StringComparison.Ordinal));
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Release();
    }
}
