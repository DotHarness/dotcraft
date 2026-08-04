using DotCraft.AppServer;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;
using QueuedTurnInput = DotCraft.Sessions.QueuedTurnInput;
using SenderContext = DotCraft.Sessions.SenderContext;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using ThreadSpawnEdge = DotCraft.Sessions.ThreadSpawnEdge;
using ThreadSummary = DotCraft.Sessions.ThreadSummary;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class HubTurnNotificationPolicyTests
{
    [Fact]
    public async Task ResolveDecision_ForNormalThread_ReturnsDisplayName()
    {
        var service = new FakeSessionService(new SessionThread
        {
            Id = "thread_user",
            OriginChannel = "dotcraft-desktop",
            DisplayName = "  User task  "
        });

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_user");

        Assert.True(decision.ShouldNotify);
        Assert.Equal("User task", decision.DisplayName);
        Assert.True(decision.OpenDesktopOnClick);
        Assert.Equal("thread_user", decision.ThreadId);
    }

    [Fact]
    public async Task ResolveDecision_ForNonDesktopOrigin_DoesNotOpenDesktopOnClick()
    {
        var service = new FakeSessionService(new SessionThread
        {
            Id = "thread_cli",
            OriginChannel = "cli",
            DisplayName = "CLI task"
        });

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_cli");

        Assert.True(decision.ShouldNotify);
        Assert.Equal("CLI task", decision.DisplayName);
        Assert.False(decision.OpenDesktopOnClick);
        Assert.Null(decision.ThreadId);
    }

    [Fact]
    public async Task ResolveDecision_ForInternalMetadataThread_SuppressesNotification()
    {
        var thread = new SessionThread
        {
            Id = "thread_internal",
            OriginChannel = "dotcraft-desktop",
            DisplayName = "[internal] Future helper"
        };
        thread.Metadata[ThreadVisibility.InternalMetadataKey] = "future-helper";
        var service = new FakeSessionService(thread);

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_internal");

        Assert.False(decision.ShouldNotify);
        Assert.False(decision.OpenDesktopOnClick);
        Assert.Null(decision.ThreadId);
    }

    [Fact]
    public async Task ResolveDecision_ForKnownInternalOriginThread_SuppressesNotification()
    {
        var service = new FakeSessionService(new SessionThread
        {
            Id = "thread_welcome",
            OriginChannel = WelcomeSuggestionConstants.ChannelName,
            DisplayName = "[internal] Welcome suggestions"
        });

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_welcome");

        Assert.False(decision.ShouldNotify);
        Assert.False(decision.OpenDesktopOnClick);
        Assert.Null(decision.ThreadId);
    }

    [Fact]
    public async Task ResolveDecision_ForSubAgentSourceThread_SuppressesNotification()
    {
        var service = new FakeSessionService(new SessionThread
        {
            Id = "thread_child",
            OriginChannel = "dotcraft-desktop",
            ChannelContext = "thread_parent",
            DisplayName = "Child agent",
            Source = ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = "thread_parent"
            })
        });

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_child");

        Assert.False(decision.ShouldNotify);
        Assert.False(decision.OpenDesktopOnClick);
        Assert.Null(decision.ThreadId);
    }

    [Fact]
    public async Task ResolveDecision_WhenThreadLoadFails_FailsOpenWithDefaultDisplayName()
    {
        var service = new FakeSessionService(null, throwOnGet: true);

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "missing_thread");

        Assert.True(decision.ShouldNotify);
        Assert.False(string.IsNullOrWhiteSpace(decision.DisplayName));
        Assert.False(decision.OpenDesktopOnClick);
        Assert.Null(decision.ThreadId);
    }

    [Fact]
    public void BuildDesktopOpenActionUrl_EncodesWorkspaceAndThread()
    {
        var url = HubTurnNotificationPolicy.BuildDesktopOpenActionUrl(
            @"E:\examples\workspace",
            "thread 1");

        Assert.Equal("dotcraft://workspace/open?path=E%3A%5Cexamples%5Cworkspace&threadId=thread%201", url);
    }

    private sealed class FakeSessionService(SessionThread? thread, bool throwOnGet = false) : ISessionService
    {
        public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }
        public Action<string>? ThreadDeletedForBroadcast { get; set; }
        public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }
        public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }
        public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

        public Task<SessionThread> CreateThreadAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? threadId = null, string? displayName = null, CancellationToken ct = default, ThreadSource? source = null) => throw new NotImplementedException();
        public Task<ThreadResetResult> ResetConversationAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? displayName = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task PauseThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ArchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, bool includeArchived = false, IReadOnlyList<string>? crossChannelOrigins = null, CancellationToken ct = default, bool includeSubAgents = false, ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity) => throw new NotImplementedException();
        public Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetThreadSpawnEdgeStatusAsync(string parentThreadId, string childThreadId, string status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(string parentThreadId, bool includeClosed = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(string threadId, bool replayRecent = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubmitInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, ChatMessage[]? messages = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task ResolveApprovalAsync(string threadId, string turnId, string requestId, SessionApprovalDecision decision, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ResolveUserInputRequestAsync(string threadId, string turnId, string requestId, RequestUserInputResponse response, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QueuedTurnInput> EnqueueTurnInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(string threadId, string queuedInputId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(string threadId, IReadOnlyList<string> orderedQueuedInputIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(string threadId, string queuedInputId, string expectedTurnId, string status, CancellationToken ct = default) => throw new NotImplementedException();
        public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default) => throw new NotImplementedException();
        public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;

        public Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default)
        {
            _ = threadId;
            _ = ct;
            if (throwOnGet || thread == null)
                throw new KeyNotFoundException("Thread not found.");
            return Task.FromResult(thread);
        }

        public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) =>
            GetThreadAsync(threadId, ct);
    }
}
