using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.AI;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.SessionImport.Tests;

internal sealed class FakeSessionService : ISessionService
{
    public Dictionary<string, SessionThread> Threads { get; } = new(StringComparer.Ordinal);

    public List<ThreadImportRequest> ImportRequests { get; } = [];

    public List<ThreadImportAppendRequest> AppendRequests { get; } = [];

    public Task? ImportGate { get; set; }

    public bool RefuseAppends { get; set; }

    public async Task<ThreadImportResult> ImportThreadAsync(ThreadImportRequest request, CancellationToken ct = default)
    {
        if (ImportGate is { } gate)
            await gate.WaitAsync(ct);
        lock (Threads)
        {
            ImportRequests.Add(request);
            if (Threads.TryGetValue(request.ThreadId, out var existing))
                return new ThreadImportResult { Thread = existing, AlreadyExisted = true };
            var thread = new SessionThread
            {
                Id = request.ThreadId,
                WorkspacePath = request.Identity.WorkspacePath,
                UserId = request.Identity.UserId,
                OriginChannel = request.Identity.ChannelName,
                ChannelContext = request.Identity.ChannelContext,
                DisplayName = request.DisplayName,
                Status = ThreadStatus.Active,
                Metadata = new Dictionary<string, string>(request.Metadata)
            };
            AddTurns(thread, request.Turns, ThreadImportConstants.ChannelName);
            Threads[thread.Id] = thread;
            return new ThreadImportResult { Thread = thread };
        }
    }

    public Task<ThreadImportResult> AppendImportedTurnsAsync(ThreadImportAppendRequest request, CancellationToken ct = default)
    {
        lock (Threads)
        {
            AppendRequests.Add(request);
            if (!Threads.TryGetValue(request.ThreadId, out var thread))
                throw new KeyNotFoundException(request.ThreadId);
            if (RefuseAppends || thread.Turns.Count != request.ExistingTurns.Count)
                throw new ThreadImportRefusedException("refused by test");
            AddTurns(thread, request.NewTurns, ThreadImportConstants.ChannelName);
            return Task.FromResult(new ThreadImportResult { Thread = thread });
        }
    }

    public void AddTurns(SessionThread thread, IEnumerable<ImportedTurnInput> turns, string originChannel)
    {
        foreach (var turn in turns)
        {
            thread.Turns.Add(new SessionTurn
            {
                Id = $"turn_{thread.Turns.Count + 1:000}",
                ThreadId = thread.Id,
                Status = TurnStatus.Completed,
                StartedAt = turn.StartedAt,
                CompletedAt = turn.CompletedAt,
                OriginChannel = originChannel
            });
        }
    }

    public Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity)
    {
        lock (Threads)
        {
            IReadOnlyList<ThreadSummary> summaries = Threads.Values
                .Where(thread => includeArchived || thread.Status != ThreadStatus.Archived)
                .Select(ThreadSummary.FromThread)
                .ToArray();
            return Task.FromResult(summaries);
        }
    }

    public Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default)
    {
        lock (Threads)
        {
            IReadOnlyList<SessionTurn> turns = Threads[threadId].Turns.Take(limit).ToArray();
            return Task.FromResult(new ThreadHistoryPage<SessionTurn>(turns, null));
        }
    }

    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    public Action<string>? ThreadDeletedForBroadcast { get; set; }

    public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    public Action<string, SessionThreadRuntimeSignal, SessionTurn?>? ThreadRuntimeSignalForBroadcast { get; set; }

    public Task<SessionThread> CreateThreadAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? threadId = null, string? displayName = null, CancellationToken ct = default, ThreadSource? source = null) => throw new NotSupportedException();

    public Task<ThreadResetResult> ResetConversationAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? displayName = null, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task PauseThreadAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task ArchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct = default) => throw new NotSupportedException();

    public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default) => throw new NotSupportedException();

    public Task SetThreadSpawnEdgeStatusAsync(string parentThreadId, string childThreadId, string status, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(string parentThreadId, bool includeClosed = false, CancellationToken ct = default) => throw new NotSupportedException();

    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(string threadId, bool replayRecent = false, CancellationToken ct = default) => throw new NotSupportedException();

    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, ChatMessage[]? messages = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotSupportedException();

    public Task<QueuedTurnInput> EnqueueTurnInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotSupportedException();

    public Task<string> SteerTurnAsync(string threadId, string expectedTurnId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotSupportedException();

    public Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(string threadId, string queuedInputId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(string threadId, IReadOnlyList<string> orderedQueuedInputIds, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(string threadId, string queuedInputId, string expectedTurnId, string status, CancellationToken ct = default) => throw new NotSupportedException();

    public Task ResolveApprovalAsync(string threadId, string turnId, string requestId, SessionApprovalDecision decision, CancellationToken ct = default) => throw new NotSupportedException();

    public Task ResolveUserInputRequestAsync(string threadId, string turnId, string requestId, RequestUserInputResponse response, CancellationToken ct = default) => throw new NotSupportedException();

    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default) => throw new NotSupportedException();

    public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) => throw new NotSupportedException();

    public Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<string>> GetInstructionSourcesAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default) => throw new NotSupportedException();

    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;
}
