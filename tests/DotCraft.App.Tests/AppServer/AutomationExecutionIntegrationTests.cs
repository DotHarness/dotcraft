using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Automations;
using DotCraft.Automations.Protocol;
using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Workspaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;
namespace DotCraft.Tests.AppServer;

public sealed class AutomationExecutionIntegrationTests
{
    [Fact]
    public async Task CreateRunAndPause_UsesRealSession_AndOnlyAFailedRunNotifiesTheCapturedChat()
    {
        await using var h = await Harness.StartAsync();
        var (chat, channel, handler, sessions) = (h.Chat, h.Channel, h.Handler, h.Sessions);
        Contract.AutomationReadResult created;
        using (ChannelSessionScope.Set(new()
        {
            Channel = "qq", UserId = "creator", GroupId = "42", DefaultDeliveryTarget = "group:42",
            Chat = new ChannelChatDescriptor(ChannelChatKinds.Group, "Release team", ChannelChatMemberships.Shared)
        }))
            created = await handler.HandleCreateAsync(new() { Automation = new Contract.AutomationInput {
                Name = "Daily report", Prompt = "Summarize project changes", Status = "active", ExecutionMode = "independent",
                WorkspaceMode = "project", ApprovalPolicy = "workspaceScope", NotificationPolicy = "all",
                Schedule = new() { Kind = "every", EveryMs = 86400000 } } }, default);
        var id = created.Automation.Id;
        var accepted = await handler.HandleRunAsync(new() { AutomationId = id }, default);
        Assert.Equal("queued", accepted.Run.Status);
        Assert.Equal(ChannelChatKinds.Group, created.Automation.Origin?.ChatKind);
        var finished = await h.Finished(accepted.Run.Id);
        Assert.Equal("succeeded", finished.Status);
        Assert.Equal("skipped", finished.DeliveryStatus);
        Assert.Equal("Project is up to date.", finished.Summary);
        Assert.Equal(0, channel.Deliveries);
        Assert.Contains("Trigger: automation \"Daily report\"", chat.LastInput, StringComparison.Ordinal);
        var history = await handler.HandleRunsListAsync(new() { AutomationId = id }, default);
        var recorded = Assert.Single(history.Runs);
        Assert.Equal(finished.Id, recorded.Id);
        Assert.Equal(finished.ThreadId, recorded.ThreadId);
        Assert.Equal(finished.TurnId, recorded.TurnId);
        var thread = await sessions.GetThreadAsync(recorded.ThreadId!);
        var turn = Assert.Single(thread.Turns);
        Assert.Equal(recorded.TurnId, turn.Id);
        Assert.False(thread.Configuration?.RequireApprovalOutsideWorkspace);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Equal("automation", turn.Input?.AsUserMessage?.TriggerKind);
        Assert.Equal(id, turn.Input?.AsUserMessage?.TriggerRefId);
        Assert.Equal("qq", chat.Origin?.Channel);

        chat.Fail = true;
        var failing = await handler.HandleRunAsync(new() { AutomationId = id }, default);
        var failed = await h.Finished(failing.Run.Id);
        Assert.Equal(("failed", "sent"), (failed.Status, failed.DeliveryStatus));
        Assert.Equal(1, channel.Deliveries);
        Assert.Equal("group:42", channel.Target);
        Assert.DoesNotContain(failed.Error!, channel.Message?.Text, StringComparison.Ordinal);
        var updated = await handler.HandleUpdateAsync(new() { AutomationId = id, ExpectedVersion = created.Automation.Version,
            Automation = new Contract.AutomationInput { Name = "Edited report", Prompt = "Summarize only important changes", Status = "paused",
                ExecutionMode = "independent", WorkspaceMode = "project", ApprovalPolicy = "workspaceScope", NotificationPolicy = "all",
                Schedule = new() { Kind = "every", EveryMs = 86400000 } } }, default);
        Assert.Equal("paused", updated.Automation.Status);
        Assert.Equal("Edited report", updated.Automation.Name);
        Assert.Null(updated.Automation.NextRunAt);
    }

    [Fact]
    public async Task OriginWithoutChatKind_IsAChannelOrigin_AndItsFailureNotifiesTheChat()
    {
        await using var h = await Harness.StartAsync();
        var created = await h.Service.CreateAsync(
            new AutomationInput
            {
                Name = "Nightly check", Prompt = "Check the build", Status = "paused", ExecutionMode = "independent",
                WorkspaceMode = "project", ApprovalPolicy = "workspaceScope", NotificationPolicy = "all",
                Schedule = new AutomationSchedule { Kind = "every", EveryMs = 86400000 }
            },
            new AutomationOrigin { Channel = "qq", UserId = "creator", GroupId = "42", DeliveryTarget = "group:42" });
        h.Chat.Fail = true;

        var accepted = await h.Handler.HandleRunAsync(new() { AutomationId = created.Id }, default);
        var failed = await h.Finished(accepted.Run.Id);

        Assert.Equal(("failed", "sent"), (failed.Status, failed.DeliveryStatus));
        Assert.Equal("group:42", h.Channel.Target);
        Assert.Equal("group:42", h.Chat.Origin?.DefaultDeliveryTarget);
        Assert.NotNull(h.Chat.Origin?.Chat);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<AutomationRun>> _finishedRuns = new();
        private string _root = "";
        private AgentFactory _agents = null!;
        private ServiceProvider _provider = null!;
        private AppServerAutomationRuntime _runtime = null!;
        public LocalChatClient Chat { get; } = new();
        public LocalChannel Channel { get; } = new();
        public SessionService Sessions { get; private set; } = null!;
        public AutomationService Service { get; private set; } = null!;
        public AutomationsRequestHandler Handler { get; private set; } = null!;

        public static async Task<Harness> StartAsync()
        {
            var h = new Harness { _root = Path.Combine(Path.GetTempPath(), "automation_execution_" + Guid.NewGuid().ToString("N")) };
            var craft = Path.Combine(h._root, ".craft"); Directory.CreateDirectory(craft);
            var config = AppConfigTestFactory.CreateOpenAI();
            var paths = new DotCraftPaths(h._root, craft, null);
            h._agents = new AgentFactory(craft, h._root, config, new MemoryStore(craft), new SkillsLoader(craft),
                new AutoApproveApprovalService(), null, chatClientRegistry: TestModelProviderRegistry.Create(), chatClient: h.Chat, toolSources: []);
            h.Sessions = new SessionService(h._agents, h.Chat.AsAIAgent(), new SessionPersistenceService(new ThreadStore(craft)), new SessionGate());
            h.Service = new AutomationService(new AutomationsConfig { PollingInterval = TimeSpan.FromHours(1) }, paths, NullLogger<AutomationService>.Instance);
            var router = new MessageRouter(new ChannelRuntimeRegistry()); router.RegisterChannel(h.Channel);
            h._provider = new ServiceCollection().AddSingleton(h.Service).AddSingleton(router).BuildServiceProvider();
            h._runtime = new AppServerAutomationRuntime(h._provider);
            h.Handler = new AutomationsRequestHandler(h.Service);
            h.Service.RunUpdated += run =>
            {
                if (run.CompletedAt != null && run.DeliveryStatus != "pending")
                    h._finishedRuns.GetOrAdd(run.Id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(run);
                return Task.CompletedTask;
            };
            await h._runtime.StartAsync(new WorkspaceRuntimeAppServerFeatureContext(h._provider, config, paths, new ModuleRegistry(), h.Sessions, null!, _ => { }));
            return h;
        }

        public Task<AutomationRun> Finished(string runId) =>
            _finishedRuns.GetOrAdd(runId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(15));

        public async ValueTask DisposeAsync()
        {
            await _runtime.StopAsync();
            await _runtime.DisposeAsync();
            await _provider.DisposeAsync();
            await _agents.DisposeAsync();
            Chat.Dispose();
            try { Directory.Delete(_root, true); }
            catch (IOException) { }
        }
    }

    internal sealed class LocalChatClient : IChatClient
    {
        public ChannelSessionInfo? Origin;
        public bool Fail;
        public string LastInput = "";
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Project is up to date.")));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Origin = ChannelSessionScope.Current;
            LastInput = messages.Last(message => message.Role == ChatRole.User).Text;
            if (Fail) throw new InvalidOperationException("model unavailable");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Project is up to date.");
            await Task.CompletedTask;
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    internal sealed class LocalChannel : IChannelService
    {
        public string Name => "qq";
        public IApprovalService? ApprovalService => null;
        public string? Target;
        public ChannelDeliveryMessage? Message;
        public int Deliveries;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public IReadOnlyList<string> GetAdminTargets() => [];
        public Task<ChannelDeliveryResult> DeliverAsync(string target, ChannelDeliveryMessage message, object? metadata = null, string? deliveryId = null, CancellationToken cancellationToken = default)
        { Target = target; Message = message; Deliveries++; return Task.FromResult(new ChannelDeliveryResult { Delivered = true }); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
