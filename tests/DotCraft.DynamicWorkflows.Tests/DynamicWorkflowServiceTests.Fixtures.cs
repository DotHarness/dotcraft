using System.Reflection;
using DotCraft.AppServer;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed partial class DynamicWorkflowServiceTests
{
    public class SessionServiceProxy : DispatchProxy
    {
        public required SessionThread Thread { get; set; }
        public Dictionary<string, SessionThread> Children { get; } = new(StringComparer.Ordinal);
        public List<ThreadSpawnEdge> SpawnEdges { get; } = [];
        public int EnqueueCalls { get; private set; }
        public BackgroundTaskEnding? LastEnding { get; private set; }
        public Func<BackgroundTaskEnding, CancellationToken, Task<string?>>? NotificationHandler { get; set; }
        public Func<string, CancellationToken, IAsyncEnumerable<SessionEvent>>? InputHandler { get; set; }
        public Func<SessionThread, ThreadWorktreeStatus>? WorktreeStatusHandler { get; set; }
        public List<string> RemovedWorktrees { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(ISessionService.GetThreadAsync) => GetThread(args),
            nameof(ISessionService.CreateThreadAsync) => CreateThread(args),
            nameof(ISessionService.ListSubAgentChildrenAsync) => ListChildren(args),
            nameof(ISessionService.UpsertThreadSpawnEdgeAsync) => UpsertSpawnEdge(args),
            nameof(ISessionService.SetThreadSpawnEdgeStatusAsync) => SetSpawnEdgeStatus(args),
            nameof(ISessionService.SubmitInputAsync) => InputHandler?.Invoke((string)args![0]!, (CancellationToken)args[4]!)
                ?? CompleteInput((string)args![0]!),
            nameof(ISessionService.AddSubAgentMailboxEntryAsync) => Task.CompletedTask,
            nameof(ISessionService.HandoffThreadWorktreeAsync) => HandoffWorktree(args),
            nameof(ISessionService.GetWorktreeStatusAsync) => Task.FromResult(WorktreeStatusHandler!(Children[(string)args![0]!])),
            nameof(ISessionService.RemoveManagedWorktreeAsync) => RemoveWorktree(args),
            nameof(IBackgroundTaskNotifications.NotifyBackgroundTaskEndedAsync) => Notify(args),
            nameof(ISessionService.ReadThreadAggregatesAsync) => ReadAggregates(args),
            _ => throw new NotSupportedException($"Unexpected Session method '{targetMethod?.Name}'.")
        };

        private Task<WorktreeHandoffResult> HandoffWorktree(object?[]? args)
        {
            var options = (WorktreeHandoffOptions)args![0]!;
            var child = Children[options.ThreadId];
            child.Worktree = new ThreadWorktreeInfo { Path = Path.Combine(Thread.WorkspacePath, child.Id), BranchName = options.BranchName! };
            return Task.FromResult(new WorktreeHandoffResult { Thread = child, Worktree = child.Worktree });
        }

        private Task RemoveWorktree(object?[]? args)
        {
            var options = (WorktreeRemoveOptions)args![0]!;
            RemovedWorktrees.Add(options.ThreadId!);
            Children[options.ThreadId!].Worktree = null;
            return Task.CompletedTask;
        }

        private Task<ThreadHistoryAggregates> ReadAggregates(object?[]? args)
        {
            var turns = GetThread(args).Result.Turns;
            return Task.FromResult(new ThreadHistoryAggregates(
                turns.Count,
                turns.Select(static turn => turn.TokenUsage ?? new TokenUsageInfo()).Aggregate(new TokenUsageInfo(), static (a, b) => a + b),
                turns.SelectMany(static turn => turn.Items).Count(static item => item.IsToolInvocation)));
        }

        private Task<SessionThread> GetThread(object?[]? args)
        {
            var threadId = (string)args![0]!;
            if (string.Equals(threadId, Thread.Id, StringComparison.Ordinal))
                return Task.FromResult(Thread);
            return Task.FromResult(Children[threadId]);
        }

        private Task<SessionThread> CreateThread(object?[]? args)
        {
            var identity = (SessionIdentity)args![0]!;
            var now = DateTimeOffset.UtcNow;
            var child = new SessionThread
            {
                Id = (string)args[3]!,
                WorkspacePath = identity.WorkspacePath,
                UserId = identity.UserId,
                OriginChannel = identity.ChannelName,
                ChannelContext = identity.ChannelContext,
                Configuration = (ThreadConfiguration?)args[1],
                HistoryMode = (HistoryMode)args[2]!,
                DisplayName = (string?)args[4],
                Source = (ThreadSource?)args[6] ?? ThreadSource.User(),
                Status = ThreadStatus.Active,
                CreatedAt = now,
                LastActiveAt = now
            };
            Children.Add(child.Id, child);
            return Task.FromResult(child);
        }

        private Task<IReadOnlyList<ThreadSpawnEdge>> ListChildren(object?[]? args)
        {
            var parentThreadId = (string)args![0]!;
            var includeClosed = (bool)args[1]!;
            IReadOnlyList<ThreadSpawnEdge> result = SpawnEdges
                .Where(edge => edge.ParentThreadId == parentThreadId
                    && (includeClosed || edge.Status == ThreadSpawnEdgeStatus.Open))
                .ToArray();
            return Task.FromResult(result);
        }

        private Task UpsertSpawnEdge(object?[]? args)
        {
            var edge = (ThreadSpawnEdge)args![0]!;
            SpawnEdges.RemoveAll(existing => existing.ChildThreadId == edge.ChildThreadId);
            SpawnEdges.Add(edge);
            return Task.CompletedTask;
        }

        private Task SetSpawnEdgeStatus(object?[]? args)
        {
            var childThreadId = (string)args![1]!;
            var status = (string)args[2]!;
            var edge = SpawnEdges.Single(item => item.ChildThreadId == childThreadId);
            edge.Status = status;
            edge.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        private static async IAsyncEnumerable<SessionEvent> CompleteInput(string threadId)
        {
            await Task.CompletedTask;
            yield return new SessionEvent { EventType = SessionEventType.TurnStarted };
            yield return new SessionEvent
            {
                EventType = SessionEventType.TurnCompleted,
                Payload = new SessionTurn { Id = "turn_child", ThreadId = threadId, Status = TurnStatus.Completed }
            };
        }

        private Task<string?> Notify(object?[]? args)
        {
            EnqueueCalls++;
            LastEnding = (BackgroundTaskEnding)args![0]!;
            return NotificationHandler?.Invoke(LastEnding, (CancellationToken)args[1]!) ?? Task.FromResult<string?>("queued_test");
        }
    }

    public interface INotifyingSessionService : ISessionService, IBackgroundTaskNotifications
    {
    }

    private sealed class NullTransport : IAppServerTransport
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null) => throw new NotSupportedException();
    }
}
