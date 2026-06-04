using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.Compaction;
using DotCraft.State;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Manages thread persistence under the .craft directory.
/// Canonical thread history is stored as thread JSONL under threads/active|archived while metadata and agent sessions live in SQLite.
/// </summary>
public sealed class ThreadStore
{
    private readonly ThreadMetadataStore _metadataStore;
    private readonly ThreadRolloutStore _rolloutStore;
    private readonly ThreadAttachmentStore _attachmentStore;
    private readonly ConcurrentDictionary<string, SessionThread> _threadSnapshotCache = new(StringComparer.Ordinal);

    public ThreadStore(string botPath)
        : this(botPath, null)
    {
    }

    internal ThreadStore(string botPath, StateRuntime? stateRuntime)
    {
        var runtime = stateRuntime ?? new StateRuntime(botPath);
        _metadataStore = new ThreadMetadataStore(runtime);
        _rolloutStore = new ThreadRolloutStore(botPath);
        _attachmentStore = new ThreadAttachmentStore(runtime, botPath);
        RebuildAttachmentReferences();
    }

    /// <summary>
    /// Persists a thread to canonical thread JSONL storage and upserts queryable metadata in SQLite.
    /// </summary>
    public async Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
    {
        if (!_threadSnapshotCache.TryGetValue(thread.Id, out var previous))
        {
            previous = await _rolloutStore.LoadThreadAsync(thread.Id, ct);
            if (previous != null)
                _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(previous);
        }

        var rolloutPath = await _rolloutStore.SaveThreadAsync(thread, previous, ct);
        _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(thread);
        _metadataStore.UpsertThread(thread, rolloutPath);
        _attachmentStore.ReplaceThreadAttachments(thread);
    }

    /// <summary>
    /// Appends a rollback record for an already-pruned thread and updates metadata/cache.
    /// </summary>
    public async Task RollbackThreadAsync(
        SessionThread thread,
        int numTurns,
        CancellationToken ct = default)
    {
        var rolloutPath = await _rolloutStore.AppendRollbackAsync(thread, numTurns, ct);
        _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(thread);
        _metadataStore.UpsertThread(thread, rolloutPath);
        _attachmentStore.ReplaceThreadAttachments(thread);
    }

    internal async Task AppendCompactionCheckpointAsync(
        string threadId,
        string coveredThroughTurnId,
        IReadOnlyList<ChatMessage> replacementHistory,
        string trigger,
        string mode,
        long tokensBefore,
        long tokensAfter,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var replacementElement = JsonSerializer.SerializeToElement(
            replacementHistory,
            SessionPersistenceJsonOptions.Default);

        await _rolloutStore.AppendCompactionCheckpointAsync(
            threadId,
            coveredThroughTurnId,
            trigger,
            mode,
            tokensBefore,
            tokensAfter,
            replacementElement,
            DateTimeOffset.UtcNow,
            ct);
    }

    /// <summary>
    /// Loads a thread by replaying canonical thread history.
    /// </summary>
    public async Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await _rolloutStore.LoadThreadAsync(threadId, ct);
        if (thread == null)
        {
            _threadSnapshotCache.TryRemove(threadId, out _);
            return null;
        }

        _threadSnapshotCache[threadId] = CloneThreadSnapshot(thread);
        return thread;
    }

    /// <summary>
    /// Loads a thread from a restricted rollout path under threads/active or threads/archived.
    /// </summary>
    public async Task<SessionThread?> LoadThreadFromPathAsync(string path, CancellationToken ct = default)
    {
        var thread = await _rolloutStore.LoadThreadFromPathAsync(path, ct);
        if (thread == null)
            return null;

        _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(thread);
        return thread;
    }

    /// <summary>
    /// Deletes a thread JSONL history and metadata row.
    /// </summary>
    public void DeleteThread(string threadId)
    {
        var candidatePaths = _threadSnapshotCache.TryGetValue(threadId, out var cached)
            ? _attachmentStore.ExtractManagedImagePaths(cached)
            : _rolloutStore.LoadThreadAsync(threadId).GetAwaiter().GetResult() is { } loaded
                ? _attachmentStore.ExtractManagedImagePaths(loaded)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _threadSnapshotCache.TryRemove(threadId, out _);
        _attachmentStore.DeleteThreadReferencesAndCleanup(threadId, candidatePaths);
        _rolloutStore.DeleteThread(threadId);
        _metadataStore.DeleteThread(threadId);
    }

    /// <summary>
    /// Deletes the persisted agent session for a thread from SQLite.
    /// </summary>
    public void DeleteSessionFile(string threadId) => _metadataStore.DeleteSession(threadId);

    /// <summary>
    /// Saves the agent session JSON into SQLite.
    /// </summary>
    public async Task SaveSessionAsync(
        AIAgent agent,
        AgentSession session,
        string threadId,
        CancellationToken ct = default)
    {
        var serialized = await agent.SerializeSessionAsync(session, SessionPersistenceJsonOptions.Default, ct);
        _metadataStore.SaveSessionJson(threadId, serialized.GetRawText());
    }

    /// <summary>
    /// Rebuilds and saves the persisted agent session from canonical thread history.
    /// </summary>
    public async Task RebuildAndSaveSessionFromThreadAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct = default)
    {
        var rebuilt = await RebuildSessionFromRolloutAsync(agent, threadId, ct);
        await SaveSessionAsync(agent, rebuilt, threadId, ct);
    }

    /// <summary>
    /// Loads an existing agent session from SQLite, or creates a new session when none exists.
    /// </summary>
    public async Task<AgentSession> LoadOrCreateSessionAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct = default)
    {
        var sessionJson = _metadataStore.LoadSessionJson(threadId);
        if (!string.IsNullOrWhiteSpace(sessionJson))
        {
            try
            {
                var element = JsonSerializer.Deserialize<JsonElement>(sessionJson, SessionPersistenceJsonOptions.Default);
                var session = await agent.DeserializeSessionAsync(element, SessionPersistenceJsonOptions.Default, ct);
                NormalizeSessionToolCallArguments(session);
                return session;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fall back to canonical rollout history when the SQLite session is
                // missing, malformed, or cannot be deserialized by the current agent.
            }
        }

        return await RebuildSessionFromRolloutAsync(agent, threadId, ct);
    }

    /// <summary>
    /// Returns true when a thread has a persisted server-side session in SQLite.
    /// </summary>
    public bool SessionFileExists(string threadId)
        => _metadataStore.SessionExists(threadId);

    /// <summary>
    /// Loads the persisted context-window usage token count for a thread.
    /// Returns null when no context usage snapshot has been recorded yet.
    /// </summary>
    public long? LoadContextUsageTokens(string threadId)
        => _metadataStore.LoadContextUsageTokens(threadId);

    /// <summary>
    /// Loads the persisted provider-usage anchor metadata for a thread.
    /// Returns null when the metadata is absent or predates anchor support.
    /// </summary>
    public ContextUsageAnchor? LoadContextUsageAnchor(string threadId)
        => _metadataStore.LoadContextUsageAnchor(threadId);

    /// <summary>
    /// Persists the current context-window usage token count for a thread.
    /// </summary>
    public Task SaveContextUsageTokensAsync(string threadId, long tokens, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SaveContextUsageTokens(threadId, tokens);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists a context-window usage token count with anchor validation metadata.
    /// </summary>
    public Task SaveContextUsageAnchorAsync(string threadId, ContextUsageAnchor anchor, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SaveContextUsageAnchor(threadId, anchor);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the current persistent goal for a thread, if one exists.
    /// </summary>
    public Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.LoadThreadGoal(threadId));
    }

    /// <summary>
    /// Upserts the current persistent goal for a thread.
    /// </summary>
    public Task UpsertThreadGoalAsync(ThreadGoal goal, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.UpsertThreadGoal(goal);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Atomically adds usage to the current thread goal when its id still matches the expected goal id.
    /// </summary>
    public Task<ThreadGoal?> AccountThreadGoalUsageAsync(
        string threadId,
        string expectedGoalId,
        TokenUsageInfo usageDelta,
        long timeDeltaSeconds,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.AccountThreadGoalUsage(
            threadId,
            expectedGoalId,
            usageDelta,
            timeDeltaSeconds));
    }

    /// <summary>
    /// Deletes the current persistent goal for a thread.
    /// </summary>
    public Task<bool> DeleteThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.DeleteThreadGoal(threadId));
    }

    /// <summary>
    /// Returns all persisted thread summaries from SQLite metadata, ordered by activity.
    /// </summary>
    public Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.LoadIndex());
    }

    public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.UpsertThreadSpawnEdge(edge);
        return Task.CompletedTask;
    }

    public Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SetThreadSpawnEdgeStatus(parentThreadId, childThreadId, status);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ThreadSpawnEdge>>(_metadataStore.ListSubAgentChildren(parentThreadId, includeClosed));
    }

    private static SessionThread CloneThreadSnapshot(SessionThread thread)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(thread, SessionJsonOptions.Default);
        return JsonSerializer.Deserialize<SessionThread>(json, SessionJsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to clone thread snapshot for {thread.Id}.");
    }

    private void RebuildAttachmentReferences()
    {
        try
        {
            _attachmentStore.RebuildFromThreads(_rolloutStore.LoadAllThreads());
        }
        catch
        {
            // Attachment indexing is best-effort; missing thumbnails should not block startup.
        }
    }

    private async Task<AgentSession> RebuildSessionFromRolloutAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct)
    {
        var thread = await _rolloutStore.LoadThreadAsync(threadId, ct);
        if (thread == null)
            return await agent.CreateSessionAsync(ct);

        var history =
            await TryBuildModelVisibleHistoryFromLatestCheckpointAsync(thread, ct) ??
            BuildModelVisibleHistoryFromTurns(thread.Turns);

        if (history.Count == 0)
            return await agent.CreateSessionAsync(ct);

        return await CreateSessionWithHistoryAsync(agent, history, ct);
    }

    private async Task<List<ChatMessage>?> TryBuildModelVisibleHistoryFromLatestCheckpointAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var checkpoints = await _rolloutStore.LoadCompactionCheckpointsAsync(thread.Id, ct);
        if (checkpoints.Count == 0)
            return null;

        var orderedTurns = thread.Turns
            .OrderBy(t => t.StartedAt)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        for (var i = checkpoints.Count - 1; i >= 0; i--)
        {
            var checkpoint = checkpoints[i];
            var coveredTurnIndex = orderedTurns.FindIndex(turn =>
                string.Equals(turn.Id, checkpoint.CoveredThroughTurnId, StringComparison.Ordinal));
            if (coveredTurnIndex < 0)
                continue;

            if (!TryDeserializeCheckpointHistory(checkpoint, out var history))
                continue;

            for (var turnIndex = coveredTurnIndex + 1; turnIndex < orderedTurns.Count; turnIndex++)
                history.AddRange(BuildModelVisibleHistoryFromTurn(orderedTurns[turnIndex]));

            return history;
        }

        return null;
    }

    private static bool TryDeserializeCheckpointHistory(
        ThreadCompactionCheckpoint checkpoint,
        out List<ChatMessage> history)
    {
        history = [];
        try
        {
            var restored = checkpoint.ReplacementHistory.Deserialize<List<ChatMessage>>(
                SessionPersistenceJsonOptions.Default);
            if (restored is null)
                return false;

            history = MessageGrouper.NormalizeFunctionCallArguments(restored).ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<ChatMessage> BuildModelVisibleHistoryFromTurns(IEnumerable<SessionTurn> turns)
    {
        var history = new List<ChatMessage>();
        foreach (var turn in turns.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal))
            history.AddRange(BuildModelVisibleHistoryFromTurn(turn));

        return history;
    }

    internal static IReadOnlyList<ChatMessage> BuildModelVisibleHistoryFromTurn(SessionTurn turn)
    {
        var history = new List<ChatMessage>();
        var completedItems = turn.Items
            .Where(static item => item.Status == ItemStatus.Completed)
            .ToList();
        var pairedToolCallIds = CollectPairedToolCallIds(completedItems);
        var assistantBuilder = new AssistantSamplingSegmentBuilder();

        foreach (var item in completedItems)
        {
            if (item.Type == ItemType.UserMessage && TryBuildUserMessage(item, out var userMessage))
            {
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(userMessage);
            }
            else if (item.Type == ItemType.ReasoningContent &&
                     item.AsReasoningContent is { Text: { } reasoningText } &&
                     !string.IsNullOrWhiteSpace(reasoningText))
            {
                assistantBuilder.AddReasoning(reasoningText);
            }
            else if (item.Type == ItemType.AgentMessage && item.AsAgentMessage is { Text: { } agentText } &&
                     !string.IsNullOrWhiteSpace(agentText))
            {
                assistantBuilder.AddText(agentText.Trim());
            }
            else if (item.Type == ItemType.ToolCall &&
                     TryBuildToolCallContent(item, pairedToolCallIds, out var toolCallContent))
            {
                assistantBuilder.AddToolCall(toolCallContent);
            }
            else if (item.Type == ItemType.ToolResult &&
                     TryBuildToolResultMessage(item, pairedToolCallIds, out var toolResultMessage))
            {
                FlushAssistantSegment(history, assistantBuilder);
                history.Add(toolResultMessage);
            }
        }

        FlushAssistantSegment(history, assistantBuilder);
        return history;
    }

    private static async Task<AgentSession> CreateSessionWithHistoryAsync(
        AIAgent agent,
        List<ChatMessage> history,
        CancellationToken ct)
    {
        var session = await agent.CreateSessionAsync(ct);
        session.SetInMemoryChatHistory(history, jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
        return session;
    }

    private static void NormalizeSessionToolCallArguments(AgentSession session)
    {
        if (!session.TryGetInMemoryChatHistory(
                out var history,
                jsonSerializerOptions: SessionPersistenceJsonOptions.Default))
        {
            return;
        }

        var normalized = MessageGrouper.NormalizeFunctionCallArguments(history);
        if (normalized.Any(static message =>
                message.Contents.OfType<FunctionCallContent>().Any(static call => call.Arguments is not null))
            || history.Any(static message =>
                message.Contents.OfType<FunctionCallContent>().Any(static call => call.Arguments is null)))
        {
            session.SetInMemoryChatHistory(normalized, jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
        }
    }

    private static bool TryBuildUserMessage(SessionItem item, out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.User, string.Empty);
        if (item.AsUserMessage is not { } user)
            return false;

        var parts =
            user.MaterializedInputParts is { Count: > 0 } materialized ? materialized :
            user.NativeInputParts is { Count: > 0 } native ? native :
            null;

        if (parts is { Count: > 0 })
        {
            var contents = parts
                .Select(p => p.ToAIContent())
                .Where(c => c is not TextContent tc || !string.IsNullOrWhiteSpace(tc.Text))
                .ToList();
            if (contents.Count > 0)
            {
                message = new ChatMessage(ChatRole.User, (IList<AIContent>)contents);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(user.Text))
            return false;

        message = new ChatMessage(ChatRole.User, user.Text.Trim());
        return true;
    }

    private static HashSet<string> CollectPairedToolCallIds(IReadOnlyList<SessionItem> items)
    {
        var resultIds = items
            .Select(static item => item.Payload as ToolResultPayload)
            .Where(static payload => !string.IsNullOrWhiteSpace(payload?.CallId))
            .Select(static payload => payload!.CallId)
            .ToHashSet(StringComparer.Ordinal);

        if (resultIds.Count == 0)
            return [];

        return items
            .Select(static item => item.Payload as ToolCallPayload)
            .Where(payload =>
                !string.IsNullOrWhiteSpace(payload?.CallId) &&
                !string.IsNullOrWhiteSpace(payload.ToolName) &&
                resultIds.Contains(payload.CallId))
            .Select(static payload => payload!.CallId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void FlushAssistantSegment(
        List<ChatMessage> history,
        AssistantSamplingSegmentBuilder builder)
    {
        if (builder.TryBuild(out var message))
            history.Add(message);
        builder.Clear();
    }

    private static bool TryBuildToolCallContent(
        SessionItem item,
        IReadOnlySet<string> pairedToolCallIds,
        out FunctionCallContent content)
    {
        content = new FunctionCallContent(string.Empty, string.Empty);
        if (item.Payload is not ToolCallPayload payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            string.IsNullOrWhiteSpace(payload.ToolName) ||
            !pairedToolCallIds.Contains(payload.CallId))
        {
            return false;
        }

        content = new FunctionCallContent(
            payload.CallId,
            payload.ToolName,
            BuildToolArguments(payload.Arguments));
        return true;
    }

    private static bool TryBuildToolResultMessage(
        SessionItem item,
        IReadOnlySet<string> pairedToolCallIds,
        out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.Tool, string.Empty);
        if (item.Payload is not ToolResultPayload payload ||
            string.IsNullOrWhiteSpace(payload.CallId) ||
            !pairedToolCallIds.Contains(payload.CallId))
        {
            return false;
        }

        message = new ChatMessage(
            ChatRole.Tool,
            (IList<AIContent>)[new FunctionResultContent(payload.CallId, payload.Result)]);
        return true;
    }

    private static IDictionary<string, object?>? BuildToolArguments(JsonObject? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return new Dictionary<string, object?>();

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
            arguments.ToJsonString(),
            SessionPersistenceJsonOptions.Default);
    }

    private sealed class AssistantSamplingSegmentBuilder
    {
        private readonly List<AIContent> _reasoning = [];
        private readonly List<AIContent> _visible = [];
        private readonly List<AIContent> _toolCalls = [];

        public void AddReasoning(string text) => _reasoning.Add(new TextReasoningContent(text));

        public void AddText(string text) => _visible.Add(new TextContent(text));

        public void AddToolCall(FunctionCallContent content) => _toolCalls.Add(content);

        public bool TryBuild(out ChatMessage message)
        {
            message = new ChatMessage(ChatRole.Assistant, string.Empty);
            if (_visible.Count == 0 && _toolCalls.Count == 0)
                return false;

            var contents = new List<AIContent>();
            if (_toolCalls.Count > 0)
                contents.AddRange(_reasoning);
            contents.AddRange(_visible);
            contents.AddRange(_toolCalls);

            message = new ChatMessage(ChatRole.Assistant, contents);
            return true;
        }

        public void Clear()
        {
            _reasoning.Clear();
            _visible.Clear();
            _toolCalls.Clear();
        }
    }
}
