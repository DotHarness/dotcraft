using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using DotCraft.Sessions;

namespace DotCraft.ContextExport;

/// <summary>
/// Searches DotCraft thread metadata, trace state, and rollout snippets for troubleshooting context.
/// </summary>
public sealed class ContextSearchService
{
    private readonly ContextWorkspaceReader _reader = new();

    /// <summary>
    /// Searches the selected workspace for sessions related to the supplied query.
    /// </summary>
    /// <param name="options">Search options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked context search hits.</returns>
    public async Task<ContextSearchResult> SearchAsync(
        ContextSearchOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Query))
            throw new ArgumentException("Search query is required.", nameof(options));

        var query = options.Query.Trim();
        var terms = SplitTerms(query);
        var warnings = new List<string>();
        var paths = _reader.ResolvePaths(options.WorkspacePath);
        var rows = _reader.LoadThreadIndex(options.WorkspacePath, options.Status, warnings);
        var rowByThreadId = rows.ToDictionary(r => r.ThreadId, StringComparer.Ordinal);
        var hits = new Dictionary<string, HitBuilder>(StringComparer.Ordinal);

        SearchThreadMetadata(rows, terms, options, hits);

        using (var connection = _reader.TryOpenStateDb(options.WorkspacePath, warnings))
        {
            if (connection != null)
            {
                var bindings = LoadTraceBindings(connection, warnings);
                SearchTraceSessions(connection, bindings, rowByThreadId, terms, options, hits, warnings);
                SearchTraceEvents(connection, bindings, rowByThreadId, terms, options, hits, warnings);
            }
        }

        await SearchRolloutSnippetsAsync(paths, rows, terms, options, hits, ct).ConfigureAwait(false);

        var limit = Math.Clamp(options.Limit <= 0 ? 10 : options.Limit, 1, 100);
        var resultHits = hits.Values
            .Where(h => h.Score > 0)
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.LastActiveAt)
            .Take(limit)
            .Select(h => h.ToResult(paths.WorkspacePath))
            .ToList();

        return new ContextSearchResult
        {
            Query = query,
            Hits = resultHits,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList()
        };
    }

    private static void SearchThreadMetadata(
        IReadOnlyList<ContextThreadIndexRow> rows,
        IReadOnlyList<string> terms,
        ContextSearchOptions options,
        Dictionary<string, HitBuilder> hits)
    {
        foreach (var row in rows)
        {
            var haystack = string.Join(
                '\n',
                row.ThreadId,
                row.DisplayName,
                row.FirstUserMessage,
                 row.OriginChannel);
            var score = ScoreText(haystack, terms);
            if (row.ThreadId.Contains(string.Join(' ', terms), StringComparison.OrdinalIgnoreCase))
                score += 80;
            if (score <= 0)
                continue;

            var hit = GetHit(hits, row);
            hit.Score += score + 20;
            hit.AddEvidence(new ContextSearchEvidence
            {
                Source = "threads",
                SourceId = row.ThreadId,
                Timestamp = row.LastActiveAt,
                Preview = ContextWorkspaceReader.Bound(
                    ContextWorkspaceReader.NormalizeWhitespace(row.FirstUserMessage ?? row.DisplayName ?? row.ThreadId),
                    Math.Max(1, options.PreviewChars))
            });
        }
    }

    private static void SearchTraceSessions(
        SqliteConnection connection,
        IReadOnlyDictionary<string, string?> bindings,
        IReadOnlyDictionary<string, ContextThreadIndexRow> rowByThreadId,
        IReadOnlyList<string> terms,
        ContextSearchOptions options,
        Dictionary<string, HitBuilder> hits,
        List<string> warnings)
    {
        try
        {
            using var command = connection.CreateCommand();
            var expression = """
                lower(coalesce(session_key, '') || ' ' ||
                      coalesce(last_finish_reason, '') || ' ' ||
                      coalesce(tool_names_json, ''))
                """;
            AddTermParameters(command, terms);
            command.CommandText = $"""
                SELECT session_key,
                       last_activity_at,
                       last_finish_reason,
                       final_system_prompt,
                       tool_names_json,
                       error_count,
                       tool_call_count,
                       context_compaction_count
                FROM trace_sessions
                WHERE {BuildTermWhere(expression, terms)}
                ORDER BY last_activity_at DESC
                LIMIT 200
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var sessionKey = reader.GetString(0);
                var threadId = ResolveThreadIdForSession(sessionKey, bindings, rowByThreadId);
                if (threadId == null || !AllowsResolvedThread(threadId, rowByThreadId, options.Status))
                    continue;

                var lastActiveAt = ParseDateTimeOffset(reader.GetString(1));
                var preview = BuildTraceSessionPreview(
                    sessionKey,
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    null,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7));

                var hit = GetHit(hits, threadId, rowByThreadId, sessionKey, lastActiveAt);
                hit.Score += 15 + ScoreText(preview, terms);
                hit.AddEvidence(new ContextSearchEvidence
                {
                    Source = "trace_sessions",
                    SourceId = sessionKey,
                    Timestamp = lastActiveAt,
                    Preview = ContextWorkspaceReader.Bound(
                        ContextWorkspaceReader.NormalizeWhitespace(preview),
                        Math.Max(1, options.PreviewChars))
                });
            }
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            warnings.Add($"Unable to search trace_sessions: {ex.Message}");
        }
    }

    private static void SearchTraceEvents(
        SqliteConnection connection,
        IReadOnlyDictionary<string, string?> bindings,
        IReadOnlyDictionary<string, ContextThreadIndexRow> rowByThreadId,
        IReadOnlyList<string> terms,
        ContextSearchOptions options,
        Dictionary<string, HitBuilder> hits,
        List<string> warnings)
    {
        try
        {
            using var command = connection.CreateCommand();
            var expression = """
                lower(coalesce(event_json, '') || ' ' ||
                      coalesce(tool_name, '') || ' ' ||
                      coalesce(model_id, '') || ' ' ||
                      coalesce(finish_reason, '') || ' ' ||
                      coalesce(type, ''))
                """;
            AddTermParameters(command, terms);
            command.CommandText = $"""
                SELECT event_id,
                       session_key,
                       timestamp,
                       type,
                       tool_name,
                       model_id,
                       finish_reason,
                       event_json
                FROM trace_events
                WHERE {BuildTermWhere(expression, terms)}
                ORDER BY timestamp DESC, id DESC
                LIMIT 500
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var eventId = reader.GetString(0);
                var sessionKey = reader.GetString(1);
                var threadId = ResolveThreadIdForSession(sessionKey, bindings, rowByThreadId);
                if (threadId == null || !AllowsResolvedThread(threadId, rowByThreadId, options.Status))
                    continue;

                var timestamp = ParseDateTimeOffset(reader.GetString(2));
                var preview = BuildTraceEventPreview(
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetString(7));

                var hit = GetHit(hits, threadId, rowByThreadId, sessionKey, timestamp);
                var typeScore = string.Equals(reader.GetString(3), "Error", StringComparison.OrdinalIgnoreCase) ? 20 : 0;
                hit.Score += 10 + typeScore + ScoreText(preview, terms);
                hit.AddEvidence(new ContextSearchEvidence
                {
                    Source = "trace_events",
                    SourceId = eventId,
                    Timestamp = timestamp,
                    Preview = ContextWorkspaceReader.Bound(
                        ContextWorkspaceReader.NormalizeWhitespace(preview),
                        Math.Max(1, options.PreviewChars))
                });
            }
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            warnings.Add($"Unable to search trace_events: {ex.Message}");
        }
    }

    private async Task SearchRolloutSnippetsAsync(
        ContextWorkspacePaths paths,
        IReadOnlyList<ContextThreadIndexRow> rows,
        IReadOnlyList<string> terms,
        ContextSearchOptions options,
        Dictionary<string, HitBuilder> hits,
        CancellationToken ct)
    {
        var candidates = rows.Count > 0
            ? rows.Select(row => (row.ThreadId, row.RolloutPath)).DistinctBy(c => c.RolloutPath, StringComparer.OrdinalIgnoreCase)
            : EnumerateRolloutCandidates(paths);

        var rowByThreadId = rows.ToDictionary(r => r.ThreadId, StringComparer.Ordinal);
        foreach (var (threadId, path) in candidates.Take(250))
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;

            var snippetsByItem = new Dictionary<string, RolloutItemSnippet>(StringComparer.Ordinal);
            var lineNumber = 0;
            await foreach (var line in File.ReadLinesAsync(path, ct))
            {
                lineNumber++;
                ContextRolloutRecord? record;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    if (!document.RootElement.TryGetProperty("kind", out var kindElement))
                        continue;

                    var kind = kindElement.GetString();
                    if (string.Equals(kind, "model_history_messages_appended", StringComparison.Ordinal) ||
                        string.Equals(kind, "provider_history_items_appended", StringComparison.Ordinal) ||
                        string.Equals(kind, "provider_history_replaced", StringComparison.Ordinal) ||
                        string.Equals(kind, "provider_history_attempt_aborted", StringComparison.Ordinal) ||
                        string.Equals(kind, "context_compacted", StringComparison.Ordinal) ||
                        kind is not ("item_appended" or "turn_state_replaced"))
                    {
                        continue;
                    }

                    record = document.RootElement.Deserialize<ContextRolloutRecord>(SessionJsonOptions.Default);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    continue;
                }

                if (record is null)
                    continue;

                if (record is { Kind: "item_appended", ItemAppended: { } appended })
                {
                    AddOrReplaceRolloutItemSnippet(
                        snippetsByItem,
                        appended.TurnId,
                        appended.Item,
                        lineNumber,
                        record.Timestamp);
                }
                else if (record is { Kind: "turn_state_replaced", TurnStateReplaced: { } replacement })
                {
                    foreach (var item in replacement.Turn.Items)
                    {
                        AddOrReplaceRolloutItemSnippet(
                            snippetsByItem,
                            replacement.Turn.Id,
                            item,
                            lineNumber,
                            record.Timestamp);
                    }
                }
            }

            foreach (var snippet in snippetsByItem.Values.OrderBy(static snippet => snippet.LineNumber))
            {
                if (!ContainsAllTerms(snippet.Text, terms))
                    continue;

                var hit = GetHit(hits, threadId, rowByThreadId, null, null);
                hit.RolloutPath ??= path;
                hit.Score += 8 + ScoreText(snippet.Text, terms);
                hit.AddEvidence(new ContextSearchEvidence
                {
                    Source = "rollout",
                    SourceId = $"{Path.GetFileName(path)}:{snippet.LineNumber}",
                    Timestamp = snippet.Timestamp,
                    Preview = ContextWorkspaceReader.Bound(
                        ContextWorkspaceReader.NormalizeWhitespace(snippet.Text),
                        Math.Max(1, options.PreviewChars))
                });
            }
        }
    }

    private static void AddOrReplaceRolloutItemSnippet(
        Dictionary<string, RolloutItemSnippet> snippetsByItem,
        string turnId,
        SessionItem item,
        int lineNumber,
        DateTimeOffset timestamp)
    {
        var text = BuildDisplayableItemText(item);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var key = $"{turnId}\0{item.Id}";
        snippetsByItem[key] = new RolloutItemSnippet(lineNumber, timestamp, text);
    }

    private static string BuildDisplayableItemText(SessionItem item)
    {
        var builder = new StringBuilder();
        AppendSearchField(builder, "type", item.Type.ToString());
        AppendSearchField(builder, "status", item.Status.ToString());

        switch (item.Type)
        {
            case ItemType.UserMessage when item.AsUserMessage is { } user:
                AppendSearchField(builder, "text", user.Text);
                AppendSearchField(builder, "sender", user.SenderName);
                AppendSearchField(builder, "channel", user.ChannelName);
                break;
            case ItemType.AgentMessage when item.AsAgentMessage is { } agent:
                AppendSearchField(builder, "text", agent.Text);
                break;
            case ItemType.ReasoningContent:
                // Reasoning is intentionally omitted from searchable/exportable handoff content.
                return string.Empty;
            case ItemType.CommandExecution when item.AsCommandExecution is { } command:
                AppendSearchField(builder, "command", command.Command);
                AppendSearchField(builder, "working_directory", command.WorkingDirectory);
                AppendSearchField(builder, "execution_status", command.Status);
                AppendSearchField(builder, "output", command.AggregatedOutput);
                break;
            case ItemType.ToolExecution when item.AsToolExecution is { } execution:
                AppendSearchField(builder, "tool", execution.ToolName);
                AppendSearchField(builder, "execution_status", execution.Status);
                AppendSearchField(builder, "result", execution.ResultPreview);
                AppendSearchField(builder, "error", execution.ErrorMessage);
                break;
            case ItemType.ImageGeneration when item.AsImageGeneration is { } image:
                AppendSearchField(builder, "generation_status", image.Status);
                AppendSearchField(builder, "prompt", image.RevisedPrompt);
                break;
            case ItemType.ToolCall when item.AsToolCall is { } toolCall:
                AppendSearchField(builder, "namespace", toolCall.Namespace);
                AppendSearchField(builder, "tool", toolCall.ToolName);
                break;
            case ItemType.McpToolCall when item.AsMcpToolCall is { } mcpCall:
                AppendSearchField(builder, "server", mcpCall.Server);
                AppendSearchField(builder, "namespace", mcpCall.Namespace);
                AppendSearchField(builder, "tool", mcpCall.ToolName);
                AppendSearchField(builder, "execution_status", mcpCall.Status);
                AppendSearchField(builder, "error_code", mcpCall.ErrorCode);
                AppendSearchField(builder, "error", mcpCall.ErrorMessage);
                break;
            case ItemType.DynamicToolCall when item.AsDynamicToolCall is { } dynamicCall:
                AppendSearchField(builder, "namespace", dynamicCall.Namespace);
                AppendSearchField(builder, "tool", dynamicCall.ToolName);
                AppendSearchField(builder, "execution_status", dynamicCall.Status);
                AppendSearchField(builder, "error_code", dynamicCall.ErrorCode);
                AppendSearchField(builder, "error", dynamicCall.ErrorMessage);
                break;
            case ItemType.ToolResult when item.AsToolResult is { } result:
                AppendSearchField(builder, "namespace", result.Namespace);
                AppendSearchField(builder, "tool", result.ToolName);
                AppendSearchField(builder, "result", result.Result);
                AppendSearchField(builder, "error_code", result.ErrorCode);
                AppendSearchField(builder, "error", result.ErrorMessage);
                break;
            case ItemType.ApprovalRequest when item.AsApprovalRequest is { } approval:
                AppendSearchField(builder, "approval_type", approval.ApprovalType);
                AppendSearchField(builder, "operation", approval.Operation);
                AppendSearchField(builder, "target", approval.Target);
                AppendSearchField(builder, "reason", approval.Reason);
                break;
            case ItemType.ApprovalResponse when item.AsApprovalResponse is { } response:
                AppendSearchField(builder, "decision", response.Decision.ToString());
                break;
            case ItemType.UserInputRequest when item.AsUserInputRequest is { } request:
                foreach (var question in request.Questions)
                {
                    AppendSearchField(builder, "question_header", question.Header);
                    AppendSearchField(builder, "question", question.Question);
                    foreach (var option in question.Options)
                    {
                        AppendSearchField(builder, "option", option.Label);
                        AppendSearchField(builder, "option_description", option.Description);
                    }
                }
                break;
            case ItemType.UserInputResponse when item.AsUserInputResponse is { } inputResponse:
                AppendSearchField(
                    builder,
                    "response",
                    JsonSerializer.Serialize(inputResponse.Response, SessionJsonOptions.Default));
                break;
            case ItemType.Error when item.AsError is { } error:
                AppendSearchField(builder, "error_code", error.Code);
                AppendSearchField(builder, "error", error.Message);
                break;
            case ItemType.SystemNotice when item.AsSystemNotice is { } notice:
                AppendSearchField(builder, "notice", notice.Kind);
                AppendSearchField(builder, "trigger", notice.Trigger);
                AppendSearchField(builder, "mode", notice.Mode);
                break;
        }

        return builder.ToString();
    }

    private static void AppendSearchField(StringBuilder builder, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (builder.Length > 0)
            builder.Append(' ');
        builder.Append(name).Append('=').Append(value);
    }

    private static IReadOnlyDictionary<string, string?> LoadTraceBindings(
        SqliteConnection connection,
        List<string> warnings)
    {
        var bindings = new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT session_key, root_thread_id FROM trace_session_bindings";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                bindings[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            warnings.Add($"Unable to read trace_session_bindings: {ex.Message}");
        }

        return bindings;
    }

    private static string BuildTraceSessionPreview(
        string sessionKey,
        string? finishReason,
        string? finalSystemPrompt,
        string? toolNamesJson,
        int errorCount,
        int toolCallCount,
        int compactionCount)
    {
        var builder = new StringBuilder();
        builder.Append($"session={sessionKey}");
        if (!string.IsNullOrWhiteSpace(finishReason))
            builder.Append($" finish={finishReason}");
        builder.Append($" errors={errorCount} tools={toolCallCount} compactions={compactionCount}");
        if (!string.IsNullOrWhiteSpace(toolNamesJson))
            builder.Append($" tools={toolNamesJson}");
        return builder.ToString();
    }

    private static string BuildTraceEventPreview(
        string type,
        string? toolName,
        string? modelId,
        string? finishReason,
        string eventJson)
    {
        var builder = new StringBuilder();
        builder.Append(type);
        if (!string.IsNullOrWhiteSpace(toolName))
            builder.Append($" tool={toolName}");
        if (!string.IsNullOrWhiteSpace(modelId))
            builder.Append($" model={modelId}");
        if (!string.IsNullOrWhiteSpace(finishReason))
            builder.Append($" finish={finishReason}");
        return builder.ToString();
    }

    private static string? ResolveThreadIdForSession(
        string sessionKey,
        IReadOnlyDictionary<string, string?> bindings,
        IReadOnlyDictionary<string, ContextThreadIndexRow> rowByThreadId)
    {
        if (bindings.TryGetValue(sessionKey, out var boundThreadId) && !string.IsNullOrWhiteSpace(boundThreadId))
            return boundThreadId;

        if (rowByThreadId.ContainsKey(sessionKey))
            return sessionKey;

        return sessionKey;
    }

    private static bool AllowsResolvedThread(
        string threadId,
        IReadOnlyDictionary<string, ContextThreadIndexRow> rowByThreadId,
        ContextSearchStatusFilter filter)
    {
        if (!rowByThreadId.TryGetValue(threadId, out var row))
            return filter == ContextSearchStatusFilter.All;

        return filter switch
        {
            ContextSearchStatusFilter.All => true,
            ContextSearchStatusFilter.Archived => string.Equals(row.Status, "Archived", StringComparison.OrdinalIgnoreCase),
            ContextSearchStatusFilter.Active => !string.Equals(row.Status, "Archived", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static HitBuilder GetHit(
        Dictionary<string, HitBuilder> hits,
        ContextThreadIndexRow row)
    {
        return GetHit(
            hits,
            row.ThreadId,
            new Dictionary<string, ContextThreadIndexRow>(StringComparer.Ordinal) { [row.ThreadId] = row },
            null,
            row.LastActiveAt);
    }

    private static HitBuilder GetHit(
        Dictionary<string, HitBuilder> hits,
        string threadId,
        IReadOnlyDictionary<string, ContextThreadIndexRow> rowByThreadId,
        string? fallbackSessionKey,
        DateTimeOffset? fallbackLastActiveAt)
    {
        if (hits.TryGetValue(threadId, out var existing))
            return existing;

        rowByThreadId.TryGetValue(threadId, out var row);
        var hit = new HitBuilder
        {
            ThreadId = threadId,
            DisplayName = row?.DisplayName,
            Status = row?.Status ?? "unbound",
            LastActiveAt = row?.LastActiveAt ?? fallbackLastActiveAt,
            RolloutPath = row?.RolloutPath,
            FallbackSessionKey = fallbackSessionKey
        };
        hits[threadId] = hit;
        return hit;
    }

    private static string[] SplitTerms(string query)
    {
        return query
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static double ScoreText(string? value, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var lower = value.ToLowerInvariant();
        var score = 0d;
        foreach (var term in terms)
        {
            var count = CountOccurrences(lower, term);
            if (count == 0)
                return 0;

            score += Math.Min(count, 5) * (term.Length >= 6 ? 4 : 2);
        }

        return score;
    }

    private static int CountOccurrences(string value, string term)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(term, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += Math.Max(1, term.Length);
        }

        return count;
    }

    private static bool ContainsAllTerms(string value, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var lower = value.ToLowerInvariant();
        return terms.All(term => lower.Contains(term, StringComparison.Ordinal));
    }

    private static void AddTermParameters(
        SqliteCommand command,
        IReadOnlyList<string> terms)
    {
        for (var i = 0; i < terms.Count; i++)
        {
            command.Parameters.AddWithValue($"$term{i}", "%" + EscapeLike(terms[i]) + "%");
        }
    }

    private static string BuildTermWhere(string expression, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return "1 = 1";

        var clauses = new List<string>(terms.Count);
        for (var i = 0; i < terms.Count; i++)
            clauses.Add($"{expression} LIKE $term{i} ESCAPE '\\'");
        return string.Join(" AND ", clauses);
    }

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static DateTimeOffset ParseDateTimeOffset(string value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.MinValue;

    private static IEnumerable<(string ThreadId, string RolloutPath)> EnumerateRolloutCandidates(ContextWorkspacePaths paths)
    {
        foreach (var dir in new[]
                 {
                     Path.Combine(paths.CraftPath, "threads", "active"),
                     Path.Combine(paths.CraftPath, "threads", "archived")
                 })
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.TopDirectoryOnly))
                yield return (Path.GetFileNameWithoutExtension(file), file);
        }
    }

    private sealed record RolloutItemSnippet(
        int LineNumber,
        DateTimeOffset Timestamp,
        string Text);

    private sealed class HitBuilder
    {
        private readonly List<ContextSearchEvidence> _evidence = [];

        public string ThreadId { get; init; } = string.Empty;

        public string? DisplayName { get; init; }

        public string Status { get; init; } = string.Empty;

        public DateTimeOffset? LastActiveAt { get; init; }

        public string? RolloutPath { get; set; }

        public string? FallbackSessionKey { get; init; }

        public double Score { get; set; }

        public void AddEvidence(ContextSearchEvidence evidence)
        {
            if (_evidence.Count >= 6)
                return;

            if (_evidence.Any(existing =>
                    string.Equals(existing.Source, evidence.Source, StringComparison.Ordinal) &&
                    string.Equals(existing.SourceId, evidence.SourceId, StringComparison.Ordinal)))
            {
                return;
            }

            _evidence.Add(evidence);
        }

        public ContextSearchHit ToResult(string workspacePath)
        {
            return new ContextSearchHit
            {
                ThreadId = ThreadId,
                DisplayName = DisplayName ?? FallbackSessionKey,
                Status = Status,
                LastActiveAt = LastActiveAt,
                Score = Math.Round(Score, 2),
                RolloutPath = RolloutPath,
                ExportCommand = RolloutPath == null
                    ? null
                    : $"dotcraft context export --thread {Quote(ThreadId)} --workspace {Quote(workspacePath)}",
                Evidence = _evidence
                    .OrderByDescending(e => e.Timestamp ?? DateTimeOffset.MinValue)
                    .ToList()
            };
        }

        private static string Quote(string value)
        {
            if (value.Length > 0 && value.All(ch => !char.IsWhiteSpace(ch) && ch != '"'))
                return value;

            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }
    }
}
