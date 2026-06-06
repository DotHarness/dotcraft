using System.Collections.Concurrent;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Protocol;
using DotCraft.State;

namespace DotCraft.Tracing;

/// <param name="synchronousPersist">When true, writes traces on the caller thread (blocks). When false (default), uses background persistence.</param>
public sealed class TraceStore
{
    private readonly string? _storagePath;
    private readonly int _maxEventsPerSession;
    private readonly bool _synchronousPersist;
    private readonly StateRuntime? _stateRuntime;
    private readonly TraceSessionBindingStore? _bindingStore;
    private readonly object _diskMutationLock = new();
    private readonly ConcurrentDictionary<string, TraceSession> _sessions = new();
    private readonly Channel<TraceEvent> _sseChannel = Channel.CreateBounded<TraceEvent>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    private int _persistInFlight;
    private const int DefaultEventPageLimit = 1000;
    private const int MaxEventPageLimit = 1000;

    private static readonly JsonSerializerOptions PersistJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public TraceStore(
        string? storagePath = null,
        int maxEventsPerSession = 5000,
        bool synchronousPersist = false)
        : this(storagePath, maxEventsPerSession, synchronousPersist, null)
    {
    }

    internal TraceStore(
        string? storagePath,
        int maxEventsPerSession,
        bool synchronousPersist,
        StateRuntime? stateRuntime)
    {
        _storagePath = storagePath;
        _maxEventsPerSession = maxEventsPerSession;
        _synchronousPersist = synchronousPersist;
        _stateRuntime = stateRuntime;
        _bindingStore = stateRuntime != null ? new TraceSessionBindingStore(stateRuntime) : null;
    }

    public void Record(TraceEvent evt)
    {
        _bindingStore?.GetOrCreateBinding(evt.SessionKey, evt.Timestamp);
        ApplyEvent(evt, writeToSse: true);

        if (_stateRuntime != null || _storagePath != null)
            PersistEvent(evt);
    }

    /// <summary>
    /// Blocks until asynchronous persistence work scheduled by this instance has completed.
    /// </summary>
    public void WaitForPendingPersistence()
    {
        if ((_storagePath == null && _stateRuntime == null) || _synchronousPersist)
            return;

        var spin = new SpinWait();
        while (Volatile.Read(ref _persistInFlight) != 0)
            spin.SpinOnce();
    }

    /// <summary>
    /// Replaces in-memory sessions with a full reload from the persistent store.
    /// </summary>
    public void RefreshFromDisk()
    {
        if (_storagePath == null && _stateRuntime == null)
            return;

        WaitForPendingPersistence();

        lock (_diskMutationLock)
        {
            _sessions.Clear();
            LoadFromDisk();
        }
    }

    public void UpsertSessionMetadata(
        string sessionKey,
        string? finalSystemPrompt,
        IEnumerable<string>? toolNames,
        string? systemPromptHash = null,
        string? toolSchemaHash = null,
        DateTimeOffset? capturedAt = null,
        string? promptCacheEventKind = null,
        IEnumerable<string>? promptCacheChangedFields = null)
    {
        var session = _sessions.GetOrAdd(sessionKey, key => new TraceSession
        {
            SessionKey = key
        });

        var effectivePromptCacheEventKind = promptCacheEventKind;
        var effectivePromptCacheChangedFields = promptCacheChangedFields?.ToArray();
        if (string.IsNullOrWhiteSpace(effectivePromptCacheEventKind))
        {
            var legacyChangedFields = new List<string>(capacity: 2);
            if (!string.IsNullOrWhiteSpace(systemPromptHash)
                && !string.IsNullOrWhiteSpace(session.SystemPromptHash)
                && !string.Equals(session.SystemPromptHash, systemPromptHash, StringComparison.Ordinal))
            {
                legacyChangedFields.Add(PromptCacheChangedFields.Prompt);
            }

            if (!string.IsNullOrWhiteSpace(toolSchemaHash)
                && !string.IsNullOrWhiteSpace(session.ToolSchemaHash)
                && !string.Equals(session.ToolSchemaHash, toolSchemaHash, StringComparison.Ordinal))
            {
                legacyChangedFields.Add(PromptCacheChangedFields.Tools);
            }

            if (legacyChangedFields.Count > 0)
            {
                effectivePromptCacheEventKind = PromptCacheEventKinds.Drift;
                effectivePromptCacheChangedFields = legacyChangedFields.ToArray();
            }
        }

        if (!string.IsNullOrWhiteSpace(finalSystemPrompt))
            session.FinalSystemPrompt = finalSystemPrompt;

        if (!string.IsNullOrWhiteSpace(systemPromptHash))
            session.SystemPromptHash = systemPromptHash;

        if (!string.IsNullOrWhiteSpace(toolSchemaHash))
            session.ToolSchemaHash = toolSchemaHash;

        session.SetToolNames(toolNames);

        var at = capturedAt ?? DateTimeOffset.UtcNow;
        if (!session.SessionMetadataCapturedAt.HasValue || at > session.SessionMetadataCapturedAt.Value)
            session.SessionMetadataCapturedAt = at;

        if (string.Equals(effectivePromptCacheEventKind, PromptCacheEventKinds.Drift, StringComparison.Ordinal))
            session.PromptDriftCount++;

        if (string.Equals(effectivePromptCacheEventKind, PromptCacheEventKinds.Drift, StringComparison.Ordinal)
            || string.Equals(effectivePromptCacheEventKind, PromptCacheEventKinds.ToolExtension, StringComparison.Ordinal))
        {
            session.LastPromptCacheChangeAt = at;
            session.LastPromptCacheChangeKind = effectivePromptCacheEventKind;
            session.LastPromptCacheChangedFields = effectivePromptCacheChangedFields?
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [];
        }
    }

    public IReadOnlyList<TraceSession> GetSessions()
    {
        return _sessions.Values
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();
    }

    public TraceSession? GetSession(string sessionKey)
    {
        return _sessions.GetValueOrDefault(sessionKey);
    }

    public IReadOnlyList<TraceEvent> GetEvents(string sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session))
            return [];
        return session.Events.OrderBy(e => e.Timestamp).ToList();
    }

    public TraceEventPage GetEventPage(
        string? sessionKey = null,
        int limit = DefaultEventPageLimit,
        string? beforeCursor = null,
        string? filter = null)
    {
        var normalizedLimit = Math.Clamp(limit <= 0 ? DefaultEventPageLimit : limit, 1, MaxEventPageLimit);
        var types = ResolveEventPageFilter(filter);

        if (_stateRuntime != null)
            return GetEventPageFromDb(sessionKey, normalizedLimit, beforeCursor, types);

        return GetEventPageFromMemory(sessionKey, normalizedLimit, beforeCursor, types);
    }

    public bool ClearSession(string sessionKey)
    {
        lock (_diskMutationLock)
        {
            var removed = _sessions.TryRemove(sessionKey, out _);
            var persistedRemoved = false;

            if (_stateRuntime != null)
            {
                using var connection = _stateRuntime.OpenConnection();
                using var deleteEvents = connection.CreateCommand();
                deleteEvents.CommandText = "DELETE FROM trace_events WHERE session_key = $session_key";
                deleteEvents.Parameters.AddWithValue("$session_key", sessionKey);
                persistedRemoved |= deleteEvents.ExecuteNonQuery() > 0;

                using var deleteSession = connection.CreateCommand();
                deleteSession.CommandText = "DELETE FROM trace_sessions WHERE session_key = $session_key";
                deleteSession.Parameters.AddWithValue("$session_key", sessionKey);
                persistedRemoved |= deleteSession.ExecuteNonQuery() > 0;
            }
            else if (_storagePath != null)
            {
                persistedRemoved = File.Exists(Path.Combine(_storagePath, $"{SanitizeFileName(sessionKey)}.jsonl"));
                DeleteSessionFile(sessionKey);
            }

            if (!removed && !persistedRemoved)
                return false;

            _bindingStore?.DeleteBinding(sessionKey);
            return true;
        }
    }

    public void ClearAll()
    {
        lock (_diskMutationLock)
        {
            _sessions.Clear();

            if (_stateRuntime != null)
            {
                using var connection = _stateRuntime.OpenConnection();
                using var deleteEvents = connection.CreateCommand();
                deleteEvents.CommandText = "DELETE FROM trace_events";
                deleteEvents.ExecuteNonQuery();

                using var deleteSessions = connection.CreateCommand();
                deleteSessions.CommandText = "DELETE FROM trace_sessions";
                deleteSessions.ExecuteNonQuery();
            }
            else if (_storagePath != null)
            {
                DeleteAllSessionFiles();
            }

            _bindingStore?.DeleteAllBindings();
        }
    }

    public ChannelReader<TraceEvent> SseReader => _sseChannel.Reader;

    public void BindThreadMainSession(string threadId, DateTimeOffset? createdAt = null)
        => _bindingStore?.BindThreadMain(threadId, createdAt);

    public void BindChildSession(
        string sessionKey,
        string rootThreadId,
        string parentSessionKey,
        DateTimeOffset? createdAt = null)
        => _bindingStore?.BindThreadChild(sessionKey, rootThreadId, parentSessionKey, createdAt);

    public TraceSessionDeletionDescriptor DescribeSessionDeletion(string sessionKey)
    {
        var binding = _bindingStore?.GetOrCreateBinding(sessionKey);
        var scope = binding == null
            || binding.BindingKind == TraceSessionBindingKind.Unbound
            || string.IsNullOrWhiteSpace(binding.RootThreadId)
            ? SessionPersistenceDeletionScopes.TraceOnly
            : SessionPersistenceDeletionScopes.ThreadCascade;

        return new TraceSessionDeletionDescriptor(
            sessionKey,
            binding?.RootThreadId,
            (binding?.BindingKind ?? TraceSessionBindingKind.Unbound).ToStorageValue(),
            scope);
    }

    public Dictionary<string, TraceSessionDeletionDescriptor> DescribeSessionDeletions(IEnumerable<string> sessionKeys)
    {
        var keys = sessionKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var bindings = _bindingStore?.GetBindings(keys)
            ?? new Dictionary<string, TraceSessionBinding>(StringComparer.Ordinal);
        var result = new Dictionary<string, TraceSessionDeletionDescriptor>(StringComparer.Ordinal);

        foreach (var sessionKey in keys)
        {
            if (!bindings.TryGetValue(sessionKey, out var binding))
                binding = _bindingStore?.GetOrCreateBinding(sessionKey)
                    ?? new TraceSessionBinding(
                        sessionKey,
                        null,
                        null,
                        TraceSessionBindingKind.Unbound,
                        DateTimeOffset.UtcNow);

            var scope = binding.BindingKind == TraceSessionBindingKind.Unbound || string.IsNullOrWhiteSpace(binding.RootThreadId)
                ? SessionPersistenceDeletionScopes.TraceOnly
                : SessionPersistenceDeletionScopes.ThreadCascade;
            result[sessionKey] = new TraceSessionDeletionDescriptor(
                sessionKey,
                binding.RootThreadId,
                binding.BindingKind.ToStorageValue(),
                scope);
        }

        return result;
    }

    public IReadOnlyList<string> GetBoundSessionKeys(string rootThreadId)
    {
        if (_bindingStore == null)
            return [];

        return _bindingStore.GetBindingsForRootThread(rootThreadId)
            .Select(b => b.SessionKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public bool DeleteStandaloneSession(string sessionKey)
    {
        var hadBinding = _bindingStore?.GetBinding(sessionKey) != null;
        var deleted = ClearSession(sessionKey);
        _bindingStore?.DeleteBinding(sessionKey);
        return deleted || hadBinding;
    }

    public TraceSummary GetSummary()
    {
        long totalInput = 0, totalOutput = 0, totalCachedInput = 0, totalCacheWriteInput = 0, totalReasoningOutput = 0;
        int totalRequests = 0, totalMaintenanceForkRequests = 0, totalResponses = 0, totalMaintenanceForkResponses = 0;
        int totalToolCalls = 0, totalErrors = 0, totalContextCompactions = 0;
        long totalToolDuration = 0, maxToolDuration = 0, maxTurnDuration = 0;

        foreach (var session in _sessions.Values)
        {
            maxTurnDuration = Math.Max(maxTurnDuration, session.MaxTurnDurationMs);
            totalInput += session.TotalInputTokens;
            totalOutput += session.TotalOutputTokens;
            totalCachedInput += session.TotalCachedInputTokens;
            totalCacheWriteInput += session.TotalCacheWriteInputTokens;
            totalReasoningOutput += session.TotalReasoningOutputTokens;
            totalRequests += session.RequestCount;
            totalMaintenanceForkRequests += session.MaintenanceForkRequestCount;
            totalResponses += session.ResponseCount;
            totalMaintenanceForkResponses += session.MaintenanceForkResponseCount;
            totalToolCalls += session.ToolCallCount;
            totalErrors += session.ErrorCount;
            totalContextCompactions += session.ContextCompactionCount;
            totalToolDuration += session.TotalToolDurationMs;
            maxToolDuration = Math.Max(maxToolDuration, session.MaxToolDurationMs);
        }

        return new TraceSummary
        {
            SessionCount = _sessions.Count,
            TotalRequests = totalRequests,
            TotalMaintenanceForkRequests = totalMaintenanceForkRequests,
            TotalResponses = totalResponses,
            TotalMaintenanceForkResponses = totalMaintenanceForkResponses,
            TotalToolCalls = totalToolCalls,
            TotalErrors = totalErrors,
            TotalContextCompactions = totalContextCompactions,
            TotalToolDurationMs = totalToolDuration,
            AvgToolDurationMs = totalToolCalls > 0 ? totalToolDuration / (double)totalToolCalls : 0,
            MaxToolDurationMs = maxToolDuration,
            MaxTurnDurationMs = maxTurnDuration,
            TotalInputTokens = totalInput,
            TotalOutputTokens = totalOutput,
            TotalCachedInputTokens = totalCachedInput,
            TotalCacheWriteInputTokens = totalCacheWriteInput,
            TotalReasoningOutputTokens = totalReasoningOutput,
            TotalTokens = totalInput + totalOutput
        };
    }

    /// <summary>
    /// Aggregates per-day token usage across all sessions for activity charts (spec Section 27A.3).
    /// Each session contributes its token totals to the local calendar day of its
    /// <see cref="TraceSession.StartedAt"/>, where local day is derived by shifting the UTC
    /// timestamp by <paramref name="tzOffsetMinutes"/>. The result is sparse (only days with
    /// at least one session) and ascending by date.
    /// </summary>
    /// <param name="from">Inclusive lower bound on the local day, or null for no lower bound.</param>
    /// <param name="to">Inclusive upper bound on the local day, or null for no upper bound.</param>
    /// <param name="tzOffsetMinutes">Minutes to add to UTC to obtain the client's local time.</param>
    public IReadOnlyList<DailyUsageBucket> GetDailyUsage(DateOnly? from, DateOnly? to, int tzOffsetMinutes)
    {
        var offset = TimeSpan.FromMinutes(tzOffsetMinutes);
        var buckets = new Dictionary<DateOnly, (long Input, long Output, int Sessions)>();

        foreach (var session in _sessions.Values)
        {
            var localWallClock = session.StartedAt.ToUniversalTime().Add(offset).DateTime;
            var date = DateOnly.FromDateTime(localWallClock);
            if (from.HasValue && date < from.Value)
                continue;
            if (to.HasValue && date > to.Value)
                continue;

            var current = buckets.GetValueOrDefault(date);
            buckets[date] = (
                current.Input + session.TotalInputTokens,
                current.Output + session.TotalOutputTokens,
                current.Sessions + 1);
        }

        return buckets
            .OrderBy(kv => kv.Key)
            .Select(kv => new DailyUsageBucket
            {
                Date = kv.Key,
                InputTokens = kv.Value.Input,
                OutputTokens = kv.Value.Output,
                SessionCount = kv.Value.Sessions
            })
            .ToList();
    }

    /// <summary>
    /// Longest single Turn duration (ms) across all sessions — the workspace "longest task"
    /// (spec §27A.3). Returns 0 when no turn durations have been recorded.
    /// </summary>
    public long GetLongestTurnDurationMs()
    {
        long max = 0;
        foreach (var session in _sessions.Values)
            max = Math.Max(max, session.MaxTurnDurationMs);
        return max;
    }

    /// <summary>
    /// Aggregates Profile "activity insights" metrics (spec §27A.5): the most-used model and
    /// reasoning effort (ranked by Response-event count), the distinct/total count of skill
    /// references, and the top-N referenced skills. Model usage reflects all persisted history
    /// (model id is recorded on every Response event); reasoning and skill metrics are
    /// forward-only (recorded from when tracking shipped, so they may be empty initially).
    /// Uses SQL aggregation when DB-backed; otherwise aggregates the in-memory event log.
    /// </summary>
    public ProfileInsights GetProfileInsights(int topSkills = 5)
    {
        var limit = Math.Clamp(topSkills, 1, 50);
        return _stateRuntime != null
            ? GetProfileInsightsFromDb(limit)
            : GetProfileInsightsFromMemory(limit);
    }

    private ProfileInsights GetProfileInsightsFromDb(int topSkills)
    {
        using var connection = _stateRuntime!.OpenConnection();

        var topModel = QueryTopRanked(
            connection,
            "SELECT model_id, COUNT(*) FROM trace_events WHERE type = 'Response' AND model_id IS NOT NULL AND model_id <> '' GROUP BY model_id ORDER BY COUNT(*) DESC, model_id ASC LIMIT 1",
            "SELECT COUNT(*) FROM trace_events WHERE type = 'Response' AND model_id IS NOT NULL AND model_id <> ''");

        var topReasoning = QueryTopRanked(
            connection,
            "SELECT reasoning_effort, COUNT(*) FROM trace_events WHERE type = 'Response' AND reasoning_effort IS NOT NULL AND reasoning_effort <> '' GROUP BY reasoning_effort ORDER BY COUNT(*) DESC, reasoning_effort ASC LIMIT 1",
            "SELECT COUNT(*) FROM trace_events WHERE type = 'Response' AND reasoning_effort IS NOT NULL AND reasoning_effort <> ''");

        long distinctSkills = 0;
        long totalSkills = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(DISTINCT tool_name), COUNT(*) FROM trace_events WHERE type = 'SkillReferenced' AND tool_name IS NOT NULL AND tool_name <> ''";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                distinctSkills = reader.GetInt64(0);
                totalSkills = reader.GetInt64(1);
            }
        }

        var skills = new List<SkillUsageBucket>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT tool_name, COUNT(*) FROM trace_events WHERE type = 'SkillReferenced' AND tool_name IS NOT NULL AND tool_name <> '' GROUP BY tool_name ORDER BY COUNT(*) DESC, tool_name ASC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", topSkills);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                skills.Add(new SkillUsageBucket(reader.GetString(0), reader.GetInt64(1)));
        }

        return new ProfileInsights(topModel, topReasoning, (int)distinctSkills, totalSkills, skills);
    }

    private static RankedUsage? QueryTopRanked(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string topSql,
        string totalSql)
    {
        string? key = null;
        long count = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = topSql;
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                key = reader.GetString(0);
                count = reader.GetInt64(1);
            }
        }

        if (string.IsNullOrEmpty(key))
            return null;

        using var totalCommand = connection.CreateCommand();
        totalCommand.CommandText = totalSql;
        var total = Convert.ToInt64(totalCommand.ExecuteScalar() ?? 0L);
        return new RankedUsage(key, count, total);
    }

    private ProfileInsights GetProfileInsightsFromMemory(int topSkills)
    {
        var events = _sessions.Values.SelectMany(static session => session.Events).ToList();

        var topModel = TopRankedFromMemory(events
            .Where(e => e.Type == TraceEventType.Response && !string.IsNullOrWhiteSpace(e.ModelId))
            .Select(e => e.ModelId!));

        var topReasoning = TopRankedFromMemory(events
            .Where(e => e.Type == TraceEventType.Response && !string.IsNullOrWhiteSpace(e.ReasoningEffort))
            .Select(e => e.ReasoningEffort!));

        var skillNames = events
            .Where(e => e.Type == TraceEventType.SkillReferenced && !string.IsNullOrWhiteSpace(e.ToolName))
            .Select(e => e.ToolName!)
            .ToList();

        var grouped = skillNames
            .GroupBy(name => name, StringComparer.Ordinal)
            .Select(g => new SkillUsageBucket(g.Key, g.LongCount()))
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .ToList();

        return new ProfileInsights(
            topModel,
            topReasoning,
            grouped.Count,
            skillNames.Count,
            grouped.Take(topSkills).ToList());
    }

    private static RankedUsage? TopRankedFromMemory(IEnumerable<string> keys)
    {
        var list = keys.ToList();
        if (list.Count == 0)
            return null;

        var top = list
            .GroupBy(key => key, StringComparer.Ordinal)
            .Select(g => new { g.Key, Count = g.LongCount() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .First();

        return new RankedUsage(top.Key, top.Count, list.Count);
    }

    public void LoadFromDisk()
    {
        if (_stateRuntime != null)
        {
            LoadFromDb();
            return;
        }

        if (_storagePath == null || !Directory.Exists(_storagePath))
            return;

        foreach (var file in Directory.GetFiles(_storagePath, "*.jsonl"))
        {
            if (string.Equals(Path.GetFileName(file), "token_usage.jsonl", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var evt = JsonSerializer.Deserialize<TraceEvent>(line, PersistJsonOptions);
                    if (evt != null && !string.IsNullOrEmpty(evt.SessionKey))
                        ApplyEvent(evt, writeToSse: false);
                }
            }
            catch
            {
                // Skip corrupted files
            }
        }
    }

    private void ApplyEvent(TraceEvent evt, bool writeToSse)
    {
        var session = _sessions.GetOrAdd(evt.SessionKey, key => new TraceSession
        {
            SessionKey = key,
            StartedAt = evt.Timestamp
        });

        if (session.StartedAt > evt.Timestamp)
        {
            // TraceSession.StartedAt is init-only; keep earliest in a new replacement session.
            _sessions[evt.SessionKey] = session = CloneSessionWithStartedAt(session, evt.Timestamp);
        }

        session.LastActivityAt = evt.Timestamp;

        switch (evt.Type)
        {
            case TraceEventType.SessionMetadata:
                UpsertSessionMetadata(
                    evt.SessionKey,
                    evt.FinalSystemPrompt,
                    evt.ToolNames,
                    evt.SystemPromptHash,
                    evt.ToolSchemaHash,
                    evt.Timestamp,
                    evt.PromptCacheEventKind,
                    evt.PromptCacheChangedFields);
                break;
            case TraceEventType.Request:
                session.RequestCount++;
                if (evt.Type == TraceEventType.Request
                    && string.IsNullOrWhiteSpace(session.FirstUserRequest)
                    && !string.IsNullOrWhiteSpace(evt.Content))
                {
                    session.FirstUserRequest = evt.Content.Trim();
                }
                break;
            case TraceEventType.MaintenanceForkRequest:
                session.MaintenanceForkRequestCount++;
                break;
            case TraceEventType.Response:
                session.ResponseCount++;
                if (!string.IsNullOrEmpty(evt.FinishReason))
                    session.LastFinishReason = evt.FinishReason;
                break;
            case TraceEventType.MaintenanceForkResponse:
                session.MaintenanceForkResponseCount++;
                break;
            case TraceEventType.ToolCallCompleted:
                session.ToolCallCount++;
                if (evt.DurationMs.HasValue)
                    session.AddToolDuration((long)Math.Round(evt.DurationMs.Value));
                break;
            case TraceEventType.TurnCompleted:
                if (evt.DurationMs.HasValue)
                    session.RecordTurnDuration((long)Math.Round(evt.DurationMs.Value));
                break;
            case TraceEventType.ToolInjection:
                ApplyPromptCacheChangeSummary(session, evt);
                break;
            case TraceEventType.TokenUsage:
                session.TokenUsageCount++;
                if (evt.InputTokens.HasValue)
                    session.AddInputTokens(evt.InputTokens.Value);
                if (evt.OutputTokens.HasValue)
                    session.AddOutputTokens(evt.OutputTokens.Value);
                if (evt.CachedInputTokens.HasValue)
                    session.AddCachedInputTokens(evt.CachedInputTokens.Value);
                if (evt.CacheWriteInputTokens.HasValue)
                    session.AddCacheWriteInputTokens(evt.CacheWriteInputTokens.Value);
                if (evt.ReasoningOutputTokens.HasValue)
                    session.AddReasoningOutputTokens(evt.ReasoningOutputTokens.Value);
                break;
            case TraceEventType.Error:
                session.ErrorCount++;
                break;
            case TraceEventType.ContextCompaction:
                session.ContextCompactionCount++;
                break;
            case TraceEventType.Thinking:
                session.ThinkingCount++;
                break;
        }

        AddInMemoryEvent(session, evt);

        if (writeToSse)
            _sseChannel.Writer.TryWrite(evt);
    }

    private static void ApplyPromptCacheChangeSummary(TraceSession session, TraceEvent evt)
    {
        if (string.Equals(evt.PromptCacheEventKind, PromptCacheEventKinds.Drift, StringComparison.Ordinal))
            session.PromptDriftCount++;

        if (!string.Equals(evt.PromptCacheEventKind, PromptCacheEventKinds.Drift, StringComparison.Ordinal)
            && !string.Equals(evt.PromptCacheEventKind, PromptCacheEventKinds.ToolExtension, StringComparison.Ordinal))
        {
            return;
        }

        session.LastPromptCacheChangeAt = evt.Timestamp;
        session.LastPromptCacheChangeKind = evt.PromptCacheEventKind;
        session.LastPromptCacheChangedFields = evt.PromptCacheChangedFields?
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
    }

    private void PersistEvent(TraceEvent evt)
    {
        if (_synchronousPersist)
        {
            PersistEventCore(evt);
            return;
        }

        Interlocked.Increment(ref _persistInFlight);
        _ = Task.Run(() =>
        {
            try
            {
                PersistEventCore(evt);
            }
            finally
            {
                Interlocked.Decrement(ref _persistInFlight);
            }
        });
    }

    private void PersistEventCore(TraceEvent evt)
    {
        if (_stateRuntime != null)
        {
            PersistEventToDb(evt);
            return;
        }

        try
        {
            Directory.CreateDirectory(_storagePath!);
            var safeKey = SanitizeFileName(evt.SessionKey);
            var filePath = Path.Combine(_storagePath!, $"{safeKey}.jsonl");
            var json = JsonSerializer.Serialize(evt, PersistJsonOptions);
            lock (_diskMutationLock)
            {
                File.AppendAllText(filePath, json + "\n");
            }
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    private void PersistEventToDb(TraceEvent evt)
    {
        try
        {
            using var connection = _stateRuntime!.OpenConnection();
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO trace_events (
                    event_id,
                    session_key,
                    timestamp,
                    type,
                    tool_name,
                    call_id,
                    response_id,
                    message_id,
                    model_id,
                    reasoning_effort,
                    finish_reason,
                    duration_ms,
                    event_json
                ) VALUES (
                    $event_id,
                    $session_key,
                    $timestamp,
                    $type,
                    $tool_name,
                    $call_id,
                    $response_id,
                    $message_id,
                    $model_id,
                    $reasoning_effort,
                    $finish_reason,
                    $duration_ms,
                    $event_json
                )
                """;
            insert.Parameters.AddWithValue("$event_id", evt.Id);
            insert.Parameters.AddWithValue("$session_key", evt.SessionKey);
            insert.Parameters.AddWithValue("$timestamp", evt.Timestamp.UtcDateTime.ToString("O"));
            insert.Parameters.AddWithValue("$type", evt.Type.ToString());
            insert.Parameters.AddWithValue("$tool_name", (object?)evt.ToolName ?? DBNull.Value);
            insert.Parameters.AddWithValue("$call_id", (object?)evt.CallId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$response_id", (object?)evt.ResponseId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$message_id", (object?)evt.MessageId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$model_id", (object?)evt.ModelId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$reasoning_effort", (object?)evt.ReasoningEffort ?? DBNull.Value);
            insert.Parameters.AddWithValue("$finish_reason", (object?)evt.FinishReason ?? DBNull.Value);
            insert.Parameters.AddWithValue("$duration_ms", evt.DurationMs ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$event_json", JsonSerializer.Serialize(evt, PersistJsonOptions));
            insert.ExecuteNonQuery();

            if (_sessions.TryGetValue(evt.SessionKey, out var session))
                PersistSessionSummary(connection, session);
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    private static void PersistSessionSummary(Microsoft.Data.Sqlite.SqliteConnection connection, TraceSession session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trace_sessions (
                session_key,
                started_at,
                last_activity_at,
                request_count,
                maintenance_fork_request_count,
                response_count,
                maintenance_fork_response_count,
                tool_call_count,
                error_count,
                context_compaction_count,
                thinking_count,
                token_usage_count,
                total_input_tokens,
                total_output_tokens,
                total_cached_input_tokens,
                total_cache_write_input_tokens,
                total_reasoning_output_tokens,
                total_tool_duration_ms,
                max_tool_duration_ms,
                max_turn_duration_ms,
                last_finish_reason,
                final_system_prompt,
                tool_names_json
            ) VALUES (
                $session_key,
                $started_at,
                $last_activity_at,
                $request_count,
                $maintenance_fork_request_count,
                $response_count,
                $maintenance_fork_response_count,
                $tool_call_count,
                $error_count,
                $context_compaction_count,
                $thinking_count,
                $token_usage_count,
                $total_input_tokens,
                $total_output_tokens,
                $total_cached_input_tokens,
                $total_cache_write_input_tokens,
                $total_reasoning_output_tokens,
                $total_tool_duration_ms,
                $max_tool_duration_ms,
                $max_turn_duration_ms,
                $last_finish_reason,
                $final_system_prompt,
                $tool_names_json
            )
            ON CONFLICT(session_key) DO UPDATE SET
                started_at = excluded.started_at,
                last_activity_at = excluded.last_activity_at,
                request_count = excluded.request_count,
                maintenance_fork_request_count = excluded.maintenance_fork_request_count,
                response_count = excluded.response_count,
                maintenance_fork_response_count = excluded.maintenance_fork_response_count,
                tool_call_count = excluded.tool_call_count,
                error_count = excluded.error_count,
                context_compaction_count = excluded.context_compaction_count,
                thinking_count = excluded.thinking_count,
                token_usage_count = excluded.token_usage_count,
                total_input_tokens = excluded.total_input_tokens,
                total_output_tokens = excluded.total_output_tokens,
                total_cached_input_tokens = excluded.total_cached_input_tokens,
                total_cache_write_input_tokens = excluded.total_cache_write_input_tokens,
                total_reasoning_output_tokens = excluded.total_reasoning_output_tokens,
                total_tool_duration_ms = excluded.total_tool_duration_ms,
                max_tool_duration_ms = excluded.max_tool_duration_ms,
                max_turn_duration_ms = excluded.max_turn_duration_ms,
                last_finish_reason = excluded.last_finish_reason,
                final_system_prompt = excluded.final_system_prompt,
                tool_names_json = excluded.tool_names_json
            """;
        command.Parameters.AddWithValue("$session_key", session.SessionKey);
        command.Parameters.AddWithValue("$started_at", session.StartedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$last_activity_at", session.LastActivityAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$request_count", session.RequestCount);
        command.Parameters.AddWithValue("$maintenance_fork_request_count", session.MaintenanceForkRequestCount);
        command.Parameters.AddWithValue("$response_count", session.ResponseCount);
        command.Parameters.AddWithValue("$maintenance_fork_response_count", session.MaintenanceForkResponseCount);
        command.Parameters.AddWithValue("$tool_call_count", session.ToolCallCount);
        command.Parameters.AddWithValue("$error_count", session.ErrorCount);
        command.Parameters.AddWithValue("$context_compaction_count", session.ContextCompactionCount);
        command.Parameters.AddWithValue("$thinking_count", session.ThinkingCount);
        command.Parameters.AddWithValue("$token_usage_count", session.TokenUsageCount);
        command.Parameters.AddWithValue("$total_input_tokens", session.TotalInputTokens);
        command.Parameters.AddWithValue("$total_output_tokens", session.TotalOutputTokens);
        command.Parameters.AddWithValue("$total_cached_input_tokens", session.TotalCachedInputTokens);
        command.Parameters.AddWithValue("$total_cache_write_input_tokens", session.TotalCacheWriteInputTokens);
        command.Parameters.AddWithValue("$total_reasoning_output_tokens", session.TotalReasoningOutputTokens);
        command.Parameters.AddWithValue("$total_tool_duration_ms", session.TotalToolDurationMs);
        command.Parameters.AddWithValue("$max_tool_duration_ms", session.MaxToolDurationMs);
        command.Parameters.AddWithValue("$max_turn_duration_ms", session.MaxTurnDurationMs);
        command.Parameters.AddWithValue("$last_finish_reason", (object?)session.LastFinishReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$final_system_prompt", (object?)session.FinalSystemPrompt ?? DBNull.Value);
        command.Parameters.AddWithValue("$tool_names_json", JsonSerializer.Serialize(session.ToolNames, PersistJsonOptions));
        command.ExecuteNonQuery();
    }

    private void LoadFromDb()
    {
        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_json
            FROM trace_events
            ORDER BY timestamp, id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var evt = JsonSerializer.Deserialize<TraceEvent>(reader.GetString(0), PersistJsonOptions);
                if (evt != null)
                    ApplyEvent(evt, writeToSse: false);
            }
            catch
            {
                // Skip corrupted rows.
            }
        }
    }

    private void DeleteSessionFile(string sessionKey)
    {
        var filePath = Path.Combine(_storagePath!, $"{SanitizeFileName(sessionKey)}.jsonl");
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private void DeleteAllSessionFiles()
    {
        if (_storagePath == null || !Directory.Exists(_storagePath))
            return;

        foreach (var file in Directory.GetFiles(_storagePath, "*.jsonl"))
        {
            if (string.Equals(Path.GetFileName(file), "token_usage.jsonl", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Delete(file);
        }
    }

    private static TraceSession CloneSessionWithStartedAt(TraceSession session, DateTimeOffset startedAt)
    {
        var clone = new TraceSession
        {
            SessionKey = session.SessionKey,
            StartedAt = startedAt,
            LastActivityAt = session.LastActivityAt,
            RequestCount = session.RequestCount,
            ResponseCount = session.ResponseCount,
            ToolCallCount = session.ToolCallCount,
            ErrorCount = session.ErrorCount,
            ContextCompactionCount = session.ContextCompactionCount,
            ThinkingCount = session.ThinkingCount,
            TokenUsageCount = session.TokenUsageCount,
            MaintenanceForkRequestCount = session.MaintenanceForkRequestCount,
            MaintenanceForkResponseCount = session.MaintenanceForkResponseCount,
            FinalSystemPrompt = session.FinalSystemPrompt,
            SystemPromptHash = session.SystemPromptHash,
            ToolSchemaHash = session.ToolSchemaHash,
            PromptDriftCount = session.PromptDriftCount,
            FirstUserRequest = session.FirstUserRequest,
            LastFinishReason = session.LastFinishReason,
            SessionMetadataCapturedAt = session.SessionMetadataCapturedAt,
            LastPromptCacheChangeAt = session.LastPromptCacheChangeAt,
            LastPromptCacheChangeKind = session.LastPromptCacheChangeKind,
            LastPromptCacheChangedFields = session.LastPromptCacheChangedFields
        };
        clone.SetToolNames(session.ToolNames);
        clone.LoadAggregateSnapshot(
            session.TotalInputTokens,
            session.TotalOutputTokens,
            session.TotalCachedInputTokens,
            session.TotalCacheWriteInputTokens,
            session.TotalReasoningOutputTokens,
            session.TotalToolDurationMs,
            session.MaxToolDurationMs,
            session.MaxTurnDurationMs);
        foreach (var evt in session.Events)
            clone.Events.Enqueue(evt);
        return clone;
    }

    private void AddInMemoryEvent(TraceSession session, TraceEvent evt)
    {
        if (_maxEventsPerSession <= 0)
            return;

        session.Events.Enqueue(evt);
        while (session.Events.Count > _maxEventsPerSession)
            session.Events.TryDequeue(out _);
    }

    private TraceEventPage GetEventPageFromDb(
        string? sessionKey,
        int limit,
        string? beforeCursor,
        IReadOnlyList<TraceEventType>? types)
    {
        TryDecodeDbCursor(beforeCursor, out var beforeTimestamp, out var beforeRowId);
        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(sessionKey))
        {
            where.Add("session_key = $session_key");
            command.Parameters.AddWithValue("$session_key", sessionKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(beforeTimestamp) && beforeRowId.HasValue)
        {
            where.Add("(timestamp < $before_timestamp OR (timestamp = $before_timestamp AND id < $before_id))");
            command.Parameters.AddWithValue("$before_timestamp", beforeTimestamp);
            command.Parameters.AddWithValue("$before_id", beforeRowId.Value);
        }

        if (types is { Count: > 0 })
        {
            var names = new List<string>(types.Count);
            for (var i = 0; i < types.Count; i++)
            {
                var name = "$type" + i;
                names.Add(name);
                command.Parameters.AddWithValue(name, types[i].ToString());
            }

            where.Add("type IN (" + string.Join(", ", names) + ")");
        }

        command.Parameters.AddWithValue("$limit", limit + 1);
        command.CommandText = $"""
            SELECT id, timestamp, event_json
            FROM trace_events
            {(where.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", where))}
            ORDER BY timestamp DESC, id DESC
            LIMIT $limit
            """;

        var rows = new List<TraceEventDbRow>(limit + 1);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new TraceEventDbRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        var hasMore = rows.Count > limit;
        var pageRows = rows.Take(limit).ToList();
        var events = new List<TraceEvent>(pageRows.Count);
        foreach (var row in pageRows)
        {
            try
            {
                var evt = JsonSerializer.Deserialize<TraceEvent>(row.EventJson, PersistJsonOptions);
                if (evt != null)
                    events.Add(evt);
            }
            catch
            {
                // Skip corrupted rows.
            }
        }

        events.Reverse();
        var oldestCursor = pageRows.Count == 0
            ? null
            : EncodeDbCursor(pageRows[^1].Timestamp, pageRows[^1].RowId);
        return new TraceEventPage(events, oldestCursor, hasMore);
    }

    private TraceEventPage GetEventPageFromMemory(
        string? sessionKey,
        int limit,
        string? beforeCursor,
        IReadOnlyList<TraceEventType>? types)
    {
        IEnumerable<TraceEvent> source = string.IsNullOrWhiteSpace(sessionKey)
            ? _sessions.Values.SelectMany(static session => session.Events)
            : _sessions.TryGetValue(sessionKey.Trim(), out var session)
                ? session.Events
                : [];

        if (types is { Count: > 0 })
        {
            var typeSet = types.ToHashSet();
            source = source.Where(evt => typeSet.Contains(evt.Type));
        }

        if (TryDecodeMemoryCursor(beforeCursor, out var beforeTicks, out var beforeEventId))
        {
            source = source.Where(evt =>
            {
                var ticks = evt.Timestamp.UtcDateTime.Ticks;
                if (ticks < beforeTicks)
                    return true;
                if (ticks > beforeTicks)
                    return false;
                return string.CompareOrdinal(evt.Id, beforeEventId) < 0;
            });
        }

        var rows = source
            .OrderByDescending(static evt => evt.Timestamp)
            .ThenByDescending(static evt => evt.Id, StringComparer.Ordinal)
            .Take(limit + 1)
            .ToList();
        var hasMore = rows.Count > limit;
        var page = rows.Take(limit).ToList();
        page.Reverse();
        var oldest = page.FirstOrDefault();
        return new TraceEventPage(
            page,
            oldest == null ? null : EncodeMemoryCursor(oldest),
            hasMore);
    }

    private static IReadOnlyList<TraceEventType>? ResolveEventPageFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
            return null;

        return filter.Trim().ToLowerInvariant() switch
        {
            "request" => [TraceEventType.Request],
            "response" => [TraceEventType.Response],
            "thinking" => [TraceEventType.Thinking],
            "tool" => [
                TraceEventType.ToolCallStarted,
                TraceEventType.ToolCallCompleted,
                TraceEventType.ToolInjection,
                TraceEventType.DeferredToolLoading
            ],
            "maintenance" => [
                TraceEventType.MaintenanceForkRequest,
                TraceEventType.MaintenanceForkResponse,
                TraceEventType.ContextCompaction,
                TraceEventType.ThreadRollback
            ],
            "tokenusage" or "tokens" => [TraceEventType.TokenUsage],
            "error" => [TraceEventType.Error],
            _ => Enum.TryParse<TraceEventType>(filter, ignoreCase: true, out var type)
                ? [type]
                : null
        };
    }

    private static string EncodeDbCursor(string timestamp, long rowId)
        => "db:" + ToBase64Url($"{timestamp}|{rowId}");

    private static bool TryDecodeDbCursor(string? cursor, out string? timestamp, out long? rowId)
    {
        timestamp = null;
        rowId = null;
        if (string.IsNullOrWhiteSpace(cursor) || !cursor.StartsWith("db:", StringComparison.Ordinal))
            return false;

        if (!TryFromBase64Url(cursor[3..], out var decoded))
            return false;
        var separator = decoded.LastIndexOf('|');
        if (separator <= 0 || separator >= decoded.Length - 1)
            return false;

        if (!long.TryParse(decoded[(separator + 1)..], out var parsedRowId))
            return false;

        timestamp = decoded[..separator];
        rowId = parsedRowId;
        return true;
    }

    private static string EncodeMemoryCursor(TraceEvent evt)
        => "mem:" + ToBase64Url($"{evt.Timestamp.UtcDateTime.Ticks}|{evt.Id}");

    private static bool TryDecodeMemoryCursor(string? cursor, out long ticks, out string eventId)
    {
        ticks = 0;
        eventId = string.Empty;
        if (string.IsNullOrWhiteSpace(cursor) || !cursor.StartsWith("mem:", StringComparison.Ordinal))
            return false;

        if (!TryFromBase64Url(cursor[4..], out var decoded))
            return false;
        var separator = decoded.IndexOf('|');
        if (separator <= 0 || separator >= decoded.Length - 1)
            return false;

        if (!long.TryParse(decoded[..separator], out ticks))
            return false;

        eventId = decoded[(separator + 1)..];
        return true;
    }

    private static string ToBase64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryFromBase64Url(string value, out string decoded)
    {
        decoded = string.Empty;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string SanitizeFileName(string value)
        => string.Concat(value.Split(Path.GetInvalidFileNameChars()));

    private sealed record TraceEventDbRow(long RowId, string Timestamp, string EventJson);
}

public sealed record TraceEventPage(
    IReadOnlyList<TraceEvent> Events,
    string? OldestCursor,
    bool HasMore);

public sealed class TraceSummary
{
    public int SessionCount { get; init; }

    public int TotalRequests { get; init; }

    public int TotalMaintenanceForkRequests { get; init; }

    public int TotalResponses { get; init; }

    public int TotalMaintenanceForkResponses { get; init; }

    public int TotalToolCalls { get; init; }

    public int TotalErrors { get; init; }

    public int TotalContextCompactions { get; init; }

    public long TotalToolDurationMs { get; init; }

    public double AvgToolDurationMs { get; init; }

    public long MaxToolDurationMs { get; init; }

    /// <summary>Longest single Turn (one unit of agent work) across all sessions, in milliseconds.</summary>
    public long MaxTurnDurationMs { get; init; }

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalCachedInputTokens { get; init; }

    public long TotalCacheWriteInputTokens { get; init; }

    public long TotalFreshInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens - TotalCacheWriteInputTokens);

    public long TotalNonCachedInputTokens => Math.Max(0, TotalInputTokens - TotalCachedInputTokens);

    public long TotalReasoningOutputTokens { get; init; }

    public double CacheHitRate => TotalInputTokens > 0
        ? TotalCachedInputTokens / (double)TotalInputTokens
        : 0;

    public long TotalTokens { get; init; }
}

/// <summary>
/// One day of aggregated token usage produced by <see cref="TraceStore.GetDailyUsage"/>.
/// <see cref="Date"/> is a local calendar day (see the caller's timezone offset).
/// </summary>
public sealed class DailyUsageBucket
{
    public DateOnly Date { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public int SessionCount { get; init; }

    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Profile "activity insights" aggregate produced by <see cref="TraceStore.GetProfileInsights"/>
/// (spec §27A.5). <see cref="TopModel"/>/<see cref="TopReasoning"/> are null when no data exists.
/// </summary>
public sealed record ProfileInsights(
    RankedUsage? TopModel,
    RankedUsage? TopReasoning,
    int DistinctSkillCount,
    long TotalSkillCount,
    IReadOnlyList<SkillUsageBucket> TopSkills);

/// <summary>A leading value and its count out of <see cref="Total"/> observations (for share%).</summary>
public sealed record RankedUsage(string Key, long Count, long Total);

/// <summary>One referenced skill and how many times it was invoked.</summary>
public sealed record SkillUsageBucket(string Name, long Count);
