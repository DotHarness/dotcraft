using System.Text.Json;

namespace DotCraft.Protocol;

public sealed partial class SessionService
{
    private sealed class ThreadCreationCoordinator(SessionService owner)
    {
        public async Task<SessionThread> CreateAsync(
            SessionIdentity identity,
            ThreadConfiguration? config,
            HistoryMode historyMode,
            string? threadId,
            string? displayName,
            CancellationToken ct,
            ThreadSource? source)
        {
            var buildThreadAgentOnCreate = config != null || owner.ChannelRuntimeToolProvider != null;
            var capturedConfig = owner.CaptureThreadConfigurationForNewThread(config);
            var thread = new SessionThread
            {
                Id = threadId ?? SessionIdGenerator.NewThreadId(),
                WorkspacePath = identity.WorkspacePath,
                UserId = identity.UserId,
                OriginChannel = identity.ChannelName,
                Status = ThreadStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow,
                HistoryMode = historyMode,
                Configuration = capturedConfig,
                DisplayName = displayName,
                Source = source ?? ThreadSource.User()
            };

            if (identity.ChannelContext != null)
            {
                thread.ChannelContext = identity.ChannelContext;
                thread.Metadata["channelContext"] = identity.ChannelContext;
            }

            // Mirror a non-subagent spawn origin into metadata so the client can durably
            // render a "from another thread" affordance after restart (Source is not persisted).
            if (!string.IsNullOrWhiteSpace(thread.Source.SpawnedFromThreadId))
                thread.Metadata["spawnedFromThreadId"] = thread.Source.SpawnedFromThreadId!;

            owner._threadsPendingPermanentDeletion.TryRemove(thread.Id, out _);
            owner._runtimeRegistry.SetThread(thread);
            var broker = owner.GetOrCreateBroker(thread.Id);

            // Create a per-thread agent when custom configuration is provided or when
            // runtime external channel tools may need thread-scoped injection.
            if (buildThreadAgentOnCreate)
            {
                using (await owner.AcquireThreadAgentLockAsync(thread.Id, ct))
                    owner.SetThreadAgent(thread.Id, await owner.BuildAgentForThreadAsync(thread, ct));
            }

            await owner.PersistThreadWithMaterializationAsync(thread, ct);
            await owner.SaveContextUsageSnapshotAsync(thread.Id, 0, ct);

            broker.PublishThreadEvent(SessionEventType.ThreadCreated, thread);
            owner.ThreadCreatedForBroadcast?.Invoke(thread);

            return thread;
        }

        public async Task<ThreadResetResult> ResetConversationAsync(
            SessionIdentity identity,
            ThreadConfiguration? config,
            HistoryMode historyMode,
            string? displayName,
            CancellationToken ct)
        {
            var summaries = await owner.FindThreadsAsync(identity, includeArchived: false, crossChannelOrigins: null, ct);
            var archivedIds = new List<string>();
            foreach (var summary in summaries.Where(s => s.Status is ThreadStatus.Active or ThreadStatus.Paused))
            {
                await owner.ArchiveThreadAsync(summary.Id, ct);
                archivedIds.Add(summary.Id);
            }

            var thread = await owner.CreateThreadAsync(identity, config, historyMode, displayName: displayName, ct: ct);
            return new ThreadResetResult
            {
                Thread = thread,
                ArchivedThreadIds = archivedIds,
                CreatedLazily = true
            };
        }

        public async Task<SessionThread> ForkAsync(
            string threadId,
            ThreadForkOptions? options,
            CancellationToken ct)
        {
            options ??= new ThreadForkOptions();
            var normalizedThreadId = NormalizeRequiredThreadId(threadId);
            var source = await LoadForkSourceThreadAsync(normalizedThreadId, options.Path, ct);
            var identity = ResolveForkIdentity(source, options.Identity);
            var now = DateTimeOffset.UtcNow;
            var config = options.Config != null
                ? owner.CaptureThreadConfigurationForNewThread(options.Config)
                : source.Configuration != null
                    ? CloneThreadConfiguration(source.Configuration)
                    : owner.CaptureThreadConfigurationForNewThread(null);
            var forkedThreadId = SessionIdGenerator.NewThreadId();
            var forkedTurns = CloneForkTurns(source, options.ForkPoint, forkedThreadId, source.Id, now);
            var forked = new SessionThread
            {
                Id = forkedThreadId,
                WorkspacePath = identity.WorkspacePath,
                UserId = identity.UserId,
                OriginChannel = identity.ChannelName,
                ChannelContext = identity.ChannelContext,
                Status = ThreadStatus.Active,
                CreatedAt = now,
                LastActiveAt = now,
                HistoryMode = source.HistoryMode,
                Configuration = config,
                DisplayName = ResolveForkDisplayName(options.DisplayName, source, forkedTurns),
                Source = CloneThreadSourceForFork(source.Source),
                ForkedFromId = source.Id,
                Ephemeral = options.Ephemeral,
                Worktree = options.Worktree,
                Metadata = CopyForkMetadata(source, identity),
                Turns = forkedTurns,
                QueuedInputs = []
            };

            owner._threadsPendingPermanentDeletion.TryRemove(forked.Id, out _);
            owner._runtimeRegistry.SetThread(forked);
            var broker = owner.GetOrCreateBroker(forked.Id);

            var buildThreadAgentOnCreate = options.Config != null
                || HasAgentShapingConfiguration(config)
                || owner.ChannelRuntimeToolProvider != null;
            if (buildThreadAgentOnCreate)
            {
                using (await owner.AcquireThreadAgentLockAsync(forked.Id, ct))
                    owner.SetThreadAgent(forked.Id, await owner.BuildAgentForThreadAsync(forked, ct));
            }

            if (!forked.Ephemeral)
            {
                await owner.PersistThreadWithMaterializationAsync(forked, ct);
                await owner.SaveContextUsageSnapshotAsync(forked.Id, 0, ct);
            }

            broker.PublishThreadEvent(SessionEventType.ThreadCreated, forked);
            owner.ThreadCreatedForBroadcast?.Invoke(forked);
            return forked;
        }

        private async Task<SessionThread> LoadForkSourceThreadAsync(
            string threadId,
            string? rolloutPath,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(rolloutPath))
                return await owner.GetOrLoadThreadAsync(threadId, ct);

            var source = await owner.Persistence.LoadThreadFromPathAsync(rolloutPath, ct)
                ?? throw new KeyNotFoundException($"Thread rollout path '{rolloutPath}' was not found.");
            if (!string.Equals(source.Id, threadId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Thread rollout path belongs to '{source.Id}', not requested thread '{threadId}'.",
                    nameof(rolloutPath));
            }

            return source;
        }

        private static SessionIdentity ResolveForkIdentity(SessionThread source, SessionIdentity? requested)
        {
            if (requested == null)
            {
                return new SessionIdentity
                {
                    ChannelName = source.OriginChannel,
                    UserId = source.UserId,
                    ChannelContext = source.ChannelContext,
                    WorkspacePath = source.WorkspacePath
                };
            }

            return requested with
            {
                ChannelName = string.IsNullOrWhiteSpace(requested.ChannelName) ? source.OriginChannel : requested.ChannelName,
                UserId = requested.UserId ?? source.UserId,
                ChannelContext = requested.ChannelContext ?? source.ChannelContext,
                WorkspacePath = string.IsNullOrWhiteSpace(requested.WorkspacePath) ? source.WorkspacePath : requested.WorkspacePath
            };
        }

        private static Dictionary<string, string> CopyForkMetadata(SessionThread source, SessionIdentity identity)
        {
            var metadata = new Dictionary<string, string>(source.Metadata, StringComparer.Ordinal);
            foreach (var key in metadata.Keys
                .Where(key =>
                    key.StartsWith("subagent.", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("dotcraft.worktree", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "dotcraft.internal", StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                metadata.Remove(key);
            }

            if (identity.ChannelContext == null)
                metadata.Remove("channelContext");
            else
                metadata["channelContext"] = identity.ChannelContext;

            return metadata;
        }

        private static ThreadSource CloneThreadSourceForFork(ThreadSource source)
        {
            if (string.Equals(source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
                || source.SubAgent != null)
            {
                return ThreadSource.User();
            }

            var json = JsonSerializer.Serialize(source, SessionJsonOptions.Default);
            return JsonSerializer.Deserialize<ThreadSource>(json, SessionJsonOptions.Default) ?? ThreadSource.User();
        }

        private static List<SessionTurn> CloneForkTurns(
            SessionThread source,
            ThreadForkPoint? forkPoint,
            string forkedThreadId,
            string sourceThreadId,
            DateTimeOffset now)
        {
            var cloned = DeepCloneTurns(source.Turns);
            List<SessionTurn> selected;

            if (forkPoint == null)
            {
                selected = cloned;
            }
            else
            {
                selected = SelectForkTurnPrefix(cloned, forkPoint, now);
            }

            foreach (var turn in selected.Where(IsActiveTurn))
                MarkForkInterruptedTurn(turn, now, "Forked from an interrupted source turn.");

            RetargetForkTurns(selected, forkedThreadId);
            AppendForkBoundaryNotice(selected, forkedThreadId, sourceThreadId, now);
            return selected;
        }

        private static string? ResolveForkDisplayName(
            string? explicitDisplayName,
            SessionThread source,
            IReadOnlyList<SessionTurn> forkedTurns)
        {
            if (!string.IsNullOrWhiteSpace(explicitDisplayName))
                return explicitDisplayName;

            if (!string.IsNullOrWhiteSpace(source.DisplayName))
                return source.DisplayName;

            return FirstUserMessageDisplayName(forkedTurns);
        }

        private static string? FirstUserMessageDisplayName(IReadOnlyList<SessionTurn> turns)
        {
            foreach (var turn in turns)
            {
                var text = (turn.Input?.Payload as UserMessagePayload)?.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = turn.Items
                        .Where(item => item.Type == ItemType.UserMessage)
                        .Select(item => (item.Payload as UserMessagePayload)?.Text)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var trimmed = text.Trim();
                return trimmed.Length > 50 ? trimmed[..50] + "..." : trimmed;
            }

            return null;
        }

        private static void AppendForkBoundaryNotice(
            List<SessionTurn> turns,
            string forkedThreadId,
            string sourceThreadId,
            DateTimeOffset now)
        {
            if (turns.Count == 0)
            {
                var turn = new SessionTurn
                {
                    Id = SessionIdGenerator.NewTurnId(1),
                    ThreadId = forkedThreadId,
                    Status = TurnStatus.Completed,
                    StartedAt = now,
                    CompletedAt = now,
                    Items = []
                };
                turn.Items.Add(CreateForkBoundaryNoticeItem(turn, 1, sourceThreadId, now));
                turns.Add(turn);
                return;
            }

            var targetTurn = turns[^1];
            targetTurn.Items.Add(CreateForkBoundaryNoticeItem(
                targetTurn,
                targetTurn.Items.Count + 1,
                sourceThreadId,
                now));
        }

        private static SessionItem CreateForkBoundaryNoticeItem(
            SessionTurn turn,
            int seq,
            string sourceThreadId,
            DateTimeOffset now)
        {
            return new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(seq),
                TurnId = turn.Id,
                Type = ItemType.SystemNotice,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = new SystemNoticePayload
                {
                    Kind = "forked",
                    SourceThreadId = sourceThreadId
                }
            };
        }

        private static List<SessionTurn> DeepCloneTurns(IReadOnlyList<SessionTurn> turns)
        {
            var json = JsonSerializer.Serialize(turns, SessionJsonOptions.Default);
            return JsonSerializer.Deserialize<List<SessionTurn>>(json, SessionJsonOptions.Default) ?? [];
        }

        private static List<SessionTurn> SelectForkTurnPrefix(
            List<SessionTurn> turns,
            ThreadForkPoint forkPoint,
            DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(forkPoint.TurnId))
                throw new ArgumentException("forkPoint.turnId is required when forkPoint is provided.", nameof(forkPoint));

            var turnIndex = turns.FindIndex(turn => string.Equals(turn.Id, forkPoint.TurnId, StringComparison.Ordinal));
            if (turnIndex < 0)
                throw new ArgumentException($"forkPoint.turnId '{forkPoint.TurnId}' was not found.", nameof(forkPoint));

            var includePoint = ResolveForkPosition(forkPoint.Position);
            if (string.IsNullOrWhiteSpace(forkPoint.ItemId))
                return includePoint ? turns.Take(turnIndex + 1).ToList() : turns.Take(turnIndex).ToList();

            var targetTurn = turns[turnIndex];
            var itemIndex = targetTurn.Items.FindIndex(item => string.Equals(item.Id, forkPoint.ItemId, StringComparison.Ordinal));
            if (itemIndex < 0)
                throw new ArgumentException($"forkPoint.itemId '{forkPoint.ItemId}' was not found in turn '{forkPoint.TurnId}'.", nameof(forkPoint));

            var itemCount = includePoint ? itemIndex + 1 : itemIndex;
            var selected = turns.Take(turnIndex).ToList();
            if (itemCount <= 0)
                return selected;

            var originalItemCount = targetTurn.Items.Count;
            targetTurn.Items = targetTurn.Items.Take(itemCount).ToList();
            targetTurn.Input = ResolveTurnInput(targetTurn);
            if (itemCount < originalItemCount || IsActiveTurn(targetTurn))
                MarkForkInterruptedTurn(targetTurn, now, "Forked from a partial source turn.");
            selected.Add(targetTurn);
            return selected;
        }

        private static bool ResolveForkPosition(string? position)
        {
            if (string.IsNullOrWhiteSpace(position)
                || string.Equals(position, ThreadForkPositions.After, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(position, ThreadForkPositions.Before, StringComparison.OrdinalIgnoreCase))
                return false;

            throw new ArgumentException("forkPoint.position must be 'before' or 'after'.", nameof(position));
        }

        private static void RetargetForkTurns(List<SessionTurn> turns, string forkedThreadId)
        {
            foreach (var turn in turns)
            {
                turn.ThreadId = forkedThreadId;
                foreach (var item in turn.Items)
                    item.TurnId = turn.Id;
                turn.Input = ResolveTurnInput(turn);
            }
        }

        private static SessionItem? ResolveTurnInput(SessionTurn turn)
        {
            var inputId = turn.Input?.Id;
            if (!string.IsNullOrWhiteSpace(inputId))
            {
                var input = turn.Items.FirstOrDefault(item => string.Equals(item.Id, inputId, StringComparison.Ordinal));
                if (input != null)
                    return input;
            }

            return turn.Items.FirstOrDefault(item => item.Type == ItemType.UserMessage);
        }

        private static bool IsActiveTurn(SessionTurn turn) =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput;

        private static void MarkForkInterruptedTurn(SessionTurn turn, DateTimeOffset now, string message)
        {
            turn.Status = TurnStatus.Cancelled;
            turn.CompletedAt ??= now;
            turn.Error ??= message;
            foreach (var item in turn.Items.Where(item => item.Status != ItemStatus.Completed))
            {
                item.Status = ItemStatus.Completed;
                item.CompletedAt ??= now;
            }
        }
    }
}
