namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private ThreadImportCoordinator? _threadImportCoordinator;

    private ThreadImportCoordinator ThreadImport => _threadImportCoordinator ??= new ThreadImportCoordinator(this);

    /// <inheritdoc />
    public Task<ThreadImportResult> ImportThreadAsync(
        ThreadImportRequest request,
        CancellationToken ct = default) =>
        ThreadImport.ImportAsync(request, ct);

    /// <inheritdoc />
    public Task<ThreadImportResult> AppendImportedTurnsAsync(
        ThreadImportAppendRequest request,
        CancellationToken ct = default) =>
        InvokeThreadCommandAsync(
            request.ThreadId,
            commandCt => ThreadImport.AppendAsync(request, commandCt),
            ct);

    private sealed class ThreadImportCoordinator(SessionService owner)
    {
        private const string UsageSource = "history_estimate";

        public async Task<ThreadImportResult> ImportAsync(ThreadImportRequest request, CancellationToken ct)
        {
            if (!IsImportChannel(request.Identity.ChannelName))
            {
                throw new ArgumentException(
                    $"Imported threads must use the '{ThreadImportConstants.ChannelName}' channel.",
                    nameof(request));
            }

            var (thread, created) = await GetOrCreateAsync(request, ct);
            if (created)
            {
                owner.GetOrCreateBroker(thread.Id).PublishThreadEvent(SessionEventType.ThreadCreated, thread);
                owner.ThreadCreatedForBroadcast?.Invoke(thread);
            }

            return new ThreadImportResult { Thread = thread, AlreadyExisted = !created };
        }

        public async Task<ThreadImportResult> AppendAsync(ThreadImportAppendRequest request, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(request.ThreadId, ct);
            if (FindRefusal(thread, request.ExistingTurns) is { } refusal)
                throw new ThreadImportRefusedException(refusal);

            var extendedTurn = thread.Turns[^1];
            var extension = ExtendTurn(extendedTurn, request.ExistingTurns[^1]);
            if (extension.Count > 0)
            {
                RaiseLastActiveAt(thread, extendedTurn.CompletedAt!.Value);
                await owner.PersistTurnStateWithMaterializationAsync(thread, extendedTurn, ct);
            }

            var appended = AppendTurns(thread, request.NewTurns);
            if (appended.Count > 0)
            {
                RaiseLastActiveAt(thread, appended[^1].CompletedAt!.Value);
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
            }

            if (extension.Count == 0 && appended.Count == 0)
                return new ThreadImportResult { Thread = thread };

            if (request.EstimatedTokens > 0)
            {
                await owner.SaveContextUsageSnapshotAsync(
                    thread.Id, request.EstimatedTokens, UsageSource, isEstimate: true, ct);
            }

            var broker = owner.GetOrCreateBroker(thread.Id);
            foreach (var item in extension)
                PublishCompletedItem(broker, extendedTurn.Id, item);
            foreach (var turn in appended)
            {
                broker.PublishTurnStarted(turn);
                foreach (var item in turn.Items)
                    PublishCompletedItem(broker, turn.Id, item);
                broker.PublishTurnCompleted(turn);
            }

            owner.ThreadUpdatedForBroadcast?.Invoke(thread);
            return new ThreadImportResult { Thread = thread };
        }

        private async Task<(SessionThread Thread, bool Created)> GetOrCreateAsync(
            ThreadImportRequest request,
            CancellationToken ct)
        {
            // The load gate makes the existence check and creation atomic for this id; registering only after
            // the write keeps a failed write from leaving an unpersisted thread in the registry.
            var threadId = request.ThreadId;
            var loadGate = owner.AddThreadLoadGateReference(threadId);
            var gateAcquired = false;
            try
            {
                await loadGate.Semaphore.WaitAsync(ct);
                gateAcquired = true;

                var existing = owner._runtimeRegistry.TryGetThread(threadId, out var loaded)
                    ? loaded
                    : await owner.Persistence.LoadThreadAsync(threadId, ct);
                if (existing != null)
                    return (existing, false);

                var thread = CreateThread(request);
                owner._runtimeRegistry.ClearPendingPermanentDeletion(threadId);
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
                await owner.SaveContextUsageSnapshotAsync(
                    threadId, request.EstimatedTokens, UsageSource, isEstimate: true, ct);
                owner._runtimeRegistry.SetThread(thread).Materialized = true;
                return (thread, true);
            }
            finally
            {
                if (gateAcquired)
                    loadGate.Semaphore.Release();
                owner.ReleaseThreadLoadGateReference(threadId, loadGate);
            }
        }

        private SessionThread CreateThread(ThreadImportRequest request)
        {
            var identity = request.Identity;
            var config = owner.CaptureThreadConfigurationForNewThread(null);
            config = ThreadWorkspaceResolver.Apply(
                identity.WorkspacePath,
                config,
                request.Cwd ?? config.Cwd,
                config.RuntimeWorkspaceRoots);
            var thread = new SessionThread
            {
                Id = request.ThreadId,
                WorkspacePath = identity.WorkspacePath,
                UserId = identity.UserId,
                OriginChannel = identity.ChannelName,
                ChannelContext = identity.ChannelContext,
                Status = ThreadStatus.Active,
                HistoryMode = HistoryMode.Server,
                Configuration = config,
                DisplayName = request.DisplayName,
                Source = ThreadSource.User(),
                ProviderHistorySchemaVersion = ProviderHistorySchema.CurrentSchemaVersion,
                Metadata = new Dictionary<string, string>(request.Metadata)
            };
            if (identity.ChannelContext != null)
                thread.Metadata["channelContext"] = identity.ChannelContext;

            var turns = AppendTurns(thread, request.Turns);
            var lastTurn = turns[^1];
            lastTurn.Items.Add(CreateAgentMessage(lastTurn, ThreadImportConstants.Marker));
            thread.CreatedAt = turns[0].StartedAt;
            thread.LastActiveAt = lastTurn.CompletedAt!.Value;
            return thread;
        }

        private static List<SessionTurn> AppendTurns(SessionThread thread, IReadOnlyList<ImportedTurnInput> inputs)
        {
            var appended = new List<SessionTurn>(inputs.Count);
            DateTimeOffset? previousStart = thread.Turns.Count > 0 ? thread.Turns[^1].StartedAt : null;
            foreach (var input in inputs)
            {
                var startedAt = previousStart is { } previous && input.StartedAt <= previous
                    ? previous.AddMilliseconds(1)
                    : input.StartedAt;
                var turn = new SessionTurn
                {
                    Id = SessionIdGenerator.NewTurnId(SessionIdGenerator.ReserveNextTurnSequence(thread)),
                    ThreadId = thread.Id,
                    Status = TurnStatus.Completed,
                    StartedAt = startedAt,
                    CompletedAt = input.CompletedAt < startedAt ? startedAt : input.CompletedAt,
                    OriginChannel = thread.OriginChannel,
                    Initiator = new TurnInitiatorContext
                    {
                        ChannelName = thread.OriginChannel,
                        UserId = thread.UserId,
                        ChannelContext = thread.ChannelContext
                    }
                };
                var userMessage = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(1),
                    TurnId = turn.Id,
                    Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = startedAt,
                    CompletedAt = startedAt,
                    Payload = new UserMessagePayload
                    {
                        Text = input.UserText,
                        ChannelName = thread.OriginChannel,
                        ChannelContext = thread.ChannelContext
                    }
                };
                turn.Input = userMessage;
                turn.Items.Add(userMessage);
                foreach (var text in input.AgentTexts)
                    turn.Items.Add(CreateAgentMessage(turn, text));

                thread.Turns.Add(turn);
                appended.Add(turn);
                previousStart = startedAt;
            }

            return appended;
        }

        private static List<SessionItem> ExtendTurn(SessionTurn turn, ImportedTurnInput source)
        {
            var added = source.AgentTexts.Skip(ImportedAgentTexts(turn).Count()).ToList();
            if (added.Count == 0)
                return [];

            if (source.CompletedAt > turn.CompletedAt)
                turn.CompletedAt = source.CompletedAt;
            var extension = new List<SessionItem>(added.Count);
            foreach (var text in added)
            {
                var item = CreateAgentMessage(turn, text);
                turn.Items.Add(item);
                extension.Add(item);
            }

            return extension;
        }

        private static SessionItem CreateAgentMessage(SessionTurn turn, string text) => new()
        {
            Id = SessionIdGenerator.NewItemId(SessionIdGenerator.LastItemSequence(turn.Items) + 1),
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = turn.CompletedAt!.Value,
            CompletedAt = turn.CompletedAt,
            Payload = new AgentMessagePayload { Text = text }
        };

        private static void RaiseLastActiveAt(SessionThread thread, DateTimeOffset completedAt)
        {
            if (completedAt > thread.LastActiveAt)
                thread.LastActiveAt = completedAt;
        }

        private static void PublishCompletedItem(ThreadEventBroker broker, string turnId, SessionItem item)
        {
            broker.PublishItemEvent(SessionEventType.ItemStarted, turnId, item);
            broker.PublishItemEvent(SessionEventType.ItemCompleted, turnId, item);
        }

        private string? FindRefusal(SessionThread thread, IReadOnlyList<ImportedTurnInput> existingTurns)
        {
            if (thread.Status != ThreadStatus.Active)
                return $"Thread '{thread.Id}' is {thread.Status}.";

            if (thread.Turns.Any(static turn =>
                    turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                || (owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime) && runtime.Maintenance != null))
            {
                return $"Thread '{thread.Id}' has a running Turn or maintenance task.";
            }

            if (!IsImportChannel(thread.OriginChannel)
                || !thread.Turns.All(static turn => IsImportChannel(turn.OriginChannel)))
            {
                return $"Thread '{thread.Id}' is not an imported thread or contains turns that were not imported.";
            }

            if (thread.Turns.Count == 0)
                return $"Thread '{thread.Id}' has no turns.";

            if (thread.Turns.Count != existingTurns.Count)
                return $"Thread '{thread.Id}' has {thread.Turns.Count} turns, but {existingTurns.Count} were expected.";

            for (var i = 0; i < existingTurns.Count; i++)
            {
                if (!MatchesSource(thread.Turns[i], existingTurns[i], allowGrowth: i == existingTurns.Count - 1))
                    return $"Turn '{thread.Turns[i].Id}' of thread '{thread.Id}' no longer matches its source.";
            }

            return null;
        }

        private static bool MatchesSource(SessionTurn turn, ImportedTurnInput source, bool allowGrowth)
        {
            var userTexts = turn.Items
                .Where(static item => item.Type == ItemType.UserMessage)
                .Select(static item => item.AsUserMessage?.Text);
            var agentTexts = ImportedAgentTexts(turn).ToList();
            var sourceCount = source.AgentTexts.Count;
            return userTexts.SequenceEqual([source.UserText], StringComparer.Ordinal)
                && (agentTexts.Count == sourceCount || (allowGrowth && agentTexts.Count < sourceCount))
                && agentTexts.SequenceEqual(source.AgentTexts.Take(agentTexts.Count), StringComparer.Ordinal);
        }

        private static IEnumerable<string?> ImportedAgentTexts(SessionTurn turn) =>
            turn.Items
                .Where(static item => item.Type == ItemType.AgentMessage)
                .Select(static item => item.AsAgentMessage?.Text)
                .Where(static text => !string.Equals(text, ThreadImportConstants.Marker, StringComparison.Ordinal));

        private static bool IsImportChannel(string? channelName) =>
            string.Equals(channelName, ThreadImportConstants.ChannelName, StringComparison.Ordinal);
    }
}
