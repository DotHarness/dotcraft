using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceInterruptionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Interruption_" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ModelProviderProtocols.OpenAI, true, false)]
    [InlineData(ModelProviderProtocols.OpenAI, true, true)]
    [InlineData(ModelProviderProtocols.OpenAIResponses, true, false)]
    [InlineData(ModelProviderProtocols.OpenAI, false, false)]
    public async Task Cancel_PersistsBeforeNotificationAndReachesNextRequest(string protocol, bool enabled, bool ephemeral)
    {
        var client = new InterruptibleClient();
        await using var factory = Factory(protocol, enabled);
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity());
        thread.Ephemeral = ephemeral;
        var run = Task.Run(async () =>
        {
            await foreach (var evt in service.SubmitInputAsync(thread.Id, [new TextContent("first")]))
            {
                if (evt.EventType == SessionEventType.TurnCancelled && !ephemeral)
                {
                    var history = await store.LoadModelHistoryAsync(thread.Id);
                    Assert.Equal(enabled ? 1 : 0, history.Count(IsMarker));
                }
            }
        });
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        client.Block = false;
        if (!ephemeral)
        {
            service = Service(factory, client, new ThreadStore(root));
            await service.GetThreadAsync(thread.Id);
        }
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        var markers = client.Messages.Where(IsMarker).ToArray();
        Assert.Equal(enabled ? 1 : 0, markers.Length);
        if (enabled)
        {
            Assert.Equal(protocol == ModelProviderProtocols.OpenAIResponses ? "developer" : "user", markers[0].Role.Value);
            Assert.True(client.Messages.IndexOf(markers[0]) < client.Messages.FindLastIndex(m => m.Text.Contains("second")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonIntentionalCancellation_DoesNotCreateMarker(bool callerCancellation)
    {
        var client = new InterruptibleClient { ThrowCancellation = !callerCancellation };
        await using var factory = Factory();
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity());
        using var cts = new CancellationTokenSource();
        var run = Drain(service.SubmitInputAsync(thread.Id, [new TextContent("first")], ct: cts.Token));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (callerCancellation) cts.Cancel();
        try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) when (callerCancellation) { }
        Assert.DoesNotContain(await store.LoadModelHistoryAsync(thread.Id), IsMarker);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task CancelBeforeSessionInitialization_PreservesInputAndMarker(bool ephemeral, bool enabled, bool callerCancellation)
    {
        var client = new InterruptibleClient { Block = false };
        await using var factory = Factory(enabled: enabled);
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity());
        thread.Ephemeral = ephemeral;
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("previous")]));
        var runtime = Assert.IsType<ThreadRuntime>(service.DebugGetRuntime(thread.Id));
        runtime.AgentLock = new SemaphoreSlim(0, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadRuntimeSignalForBroadcast = (_, signal, _) =>
        {
            if (signal == SessionThreadRuntimeSignal.TurnStarted) started.TrySetResult();
        };
        using var cts = new CancellationTokenSource();
        var run = Drain(service.SubmitInputAsync(thread.Id, [new TextContent("early")], ct: cts.Token));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (callerCancellation) cts.Cancel();
        else await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) when (callerCancellation) { }
        runtime.AgentLock.Release();
        await Drain(service.SubmitInputAsync(thread.Id, [new TextContent("next")]));
        Assert.Equal(enabled && !callerCancellation ? 1 : 0, client.Messages.Count(IsMarker));
        Assert.Contains(client.Messages, message => message.Text.Contains("previous"));
        Assert.Contains(client.Messages, message => message.Text.Contains("early"));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public async Task ActiveFork_MarksOnlyRetainedChildHistory(bool enabled, bool ephemeral, bool excludeActive)
    {
        var client = new InterruptibleClient();
        await using var factory = Factory(enabled: enabled);
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var source = await service.CreateThreadAsync(Identity());
        var run = Drain(service.SubmitInputAsync(source.Id, [new TextContent("work")]));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var fork = await service.ForkThreadAsync(source.Id, new ThreadForkOptions
        {
            Ephemeral = ephemeral,
            ForkPoint = excludeActive ? new ThreadForkPoint { TurnId = source.Turns[^1].Id, Position = "before" } : null
        });
        Assert.Equal(TurnStatus.Running, source.Turns[^1].Status);
        Assert.DoesNotContain(await store.LoadModelHistoryAsync(source.Id), IsMarker);
        await service.CancelTurnAsync(source.Id, source.Turns[^1].Id);
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        client.Block = false;
        await Drain(service.SubmitInputAsync(fork.Id, [new TextContent("branch") ]));
        Assert.Equal(enabled && !excludeActive ? 1 : 0, client.Messages.Count(IsMarker));
    }

    [Fact]
    public async Task CancelledFork_InheritsMarkerEvenWhenNewMarkersDisabled()
    {
        var client = new InterruptibleClient();
        await using var factory = Factory();
        var store = new ThreadStore(root);
        var service = Service(factory, client, store);
        var thread = await service.CreateThreadAsync(Identity());
        var run = Drain(service.SubmitInputAsync(thread.Id, [new TextContent("work")]));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        await run;
        factory.RuntimeContext.Config.AgentInterruptMessageEnabled = false;
        var fork = await service.ForkThreadAsync(thread.Id);
        Assert.Single(await store.LoadModelHistoryAsync(fork.Id), IsMarker);
    }

    [Fact]
    public void Configuration_UsesDefaultAndWorkspaceOverride()
    {
        Directory.CreateDirectory(root);
        var global = Path.Combine(root, "global.json");
        var workspace = Path.Combine(root, "workspace.json");
        File.WriteAllText(global, "{}");
        File.WriteAllText(workspace, "{}");
        Assert.True(AppConfig.LoadWithGlobalFallback(workspace, global).AgentInterruptMessageEnabled);
        File.WriteAllText(global, """{"AgentInterruptMessageEnabled":false}""");
        Assert.False(AppConfig.LoadWithGlobalFallback(workspace, global).AgentInterruptMessageEnabled);
        File.WriteAllText(workspace, """{"AgentInterruptMessageEnabled":true}""");
        Assert.True(AppConfig.LoadWithGlobalFallback(workspace, global).AgentInterruptMessageEnabled);
    }

    private AgentFactory Factory(string protocol = ModelProviderProtocols.OpenAI, bool enabled = true, IChatClient? client = null)
    {
        Directory.CreateDirectory(root);
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Providers[config.ProviderId].Protocol = protocol;
        config.AgentInterruptMessageEnabled = enabled;
        return new AgentFactory(root, root, config, new MemoryStore(root), new SkillsLoader(root),
            new AutoApproveApprovalService(), null, chatClientRegistry: TestModelProviderRegistry.Create(), chatClient: client);
    }

    private static SessionService Service(AgentFactory factory, IChatClient client, ThreadStore store) =>
        new(factory, new StreamingFunctionInvokingChatClient(client).AsAIAgent(), new SessionPersistenceService(store), new SessionGate());

    private SessionIdentity Identity() => new() { ChannelName = "test", UserId = "user", WorkspacePath = root };
    private static bool IsMarker(ChatMessage message) => ThreadContextItems.IsKind(message, TurnInterruption.Kind);
    private static async Task Drain(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var evt in events)
            Assert.True(evt.EventType != SessionEventType.TurnFailed, evt.TurnFailedPayload?.Error);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class InterruptibleClient : IChatClient
    {
        public TaskCompletionSource Started { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Text.Json.Nodes.JsonArray? NativeInput { get; private set; }
        public bool Block { get; set; } = true;
        public bool ThrowCancellation { get; init; }
        public Func<Task>? BeforeResponse { get; set; }
        public List<ChatMessage> Messages { get; private set; } = [];
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Messages = messages.ToList();
            if (ProviderRequestContextScope.Current?.History is OpenAIResponsesProviderHistoryContext native)
                NativeInput = (await native.PrepareInputAsync(Messages, options, cancellationToken)).Input;
            Started.TrySetResult();
            if (BeforeResponse != null) await BeforeResponse();
            if (ThrowCancellation) throw new OperationCanceledException();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
