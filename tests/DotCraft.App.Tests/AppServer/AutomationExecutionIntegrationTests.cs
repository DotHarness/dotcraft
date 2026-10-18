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
        var root = Path.Combine(Path.GetTempPath(), "automation_execution_" + Guid.NewGuid().ToString("N"));
        var craft = Path.Combine(root, ".craft"); Directory.CreateDirectory(craft);
        var config = AppConfigTestFactory.CreateOpenAI();
        var paths = new DotCraftPaths(root, craft, null);
        using var chat = new LocalChatClient();
        await using var agents = new AgentFactory(craft, root, config, new MemoryStore(craft), new SkillsLoader(craft),
            new AutoApproveApprovalService(), null, chatClientRegistry: TestModelProviderRegistry.Create(), chatClient: chat, toolSources: []);
        var sessions = new SessionService(agents, chat.AsAIAgent(), new SessionPersistenceService(new ThreadStore(craft)), new SessionGate());
        var service = new AutomationService(new AutomationsConfig { PollingInterval = TimeSpan.FromHours(1) }, paths, NullLogger<AutomationService>.Instance);
        var channel = new LocalChannel();
        var router = new MessageRouter(new ChannelRuntimeRegistry()); router.RegisterChannel(channel);
        await using var provider = new ServiceCollection().AddSingleton(service).AddSingleton(router).BuildServiceProvider();
        await using var runtime = new AppServerAutomationRuntime(provider);
        var handler = new AutomationsRequestHandler(service);
        var finishedRuns = new ConcurrentDictionary<string, TaskCompletionSource<AutomationRun>>();
        Task<AutomationRun> Finished(string runId) =>
            finishedRuns.GetOrAdd(runId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(15));
        service.RunUpdated += run =>
        {
            if (run.CompletedAt != null && run.DeliveryStatus != "pending")
                finishedRuns.GetOrAdd(run.Id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(run);
            return Task.CompletedTask;
        };
        try
        {
            await runtime.StartAsync(new WorkspaceRuntimeAppServerFeatureContext(provider, config, paths, new ModuleRegistry(), sessions, null!, _ => { }));
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
            var finished = await Finished(accepted.Run.Id);
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
            var failed = await Finished(failing.Run.Id);
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
        finally
        {
            await runtime.StopAsync();
            try { Directory.Delete(root, true); }
            catch (IOException) { }
        }
    }
    private sealed class LocalChatClient : IChatClient
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
    private sealed class LocalChannel : IChannelService
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
