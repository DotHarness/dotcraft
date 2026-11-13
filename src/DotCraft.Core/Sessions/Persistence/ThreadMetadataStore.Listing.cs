using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class ThreadMetadataStore
{
    private const string SummaryColumns = """
        thread_id, user_id, origin_channel, channel_context, display_name, status,
        created_at, updated_at, turn_count, metadata_json, forked_from_id, ephemeral,
        worktree_json, source_json, git_info_json
        """;

    public ThreadListPage ListThreads(
        ThreadListQuery query,
        IReadOnlyList<ThreadSummary> liveSummaries,
        IReadOnlySet<string> excludedThreadIds)
    {
        using var connection = stateRuntime.OpenConnection();
        connection.CreateFunction<string?, string?, bool>("ordinal_equals",
            (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            isDeterministic: true);
        connection.CreateFunction<string?, string, bool>("ordinal_contains",
            (value, term) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true,
            isDeterministic: true);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var source = BuildSummarySource(command, liveSummaries);
        var predicates = BuildListPredicates(command, query, excludedThreadIds);
        command.CommandText = $"{source} SELECT COUNT(*) FROM summaries t WHERE {predicates}";
        var total = checked((int)(long)command.ExecuteScalar()!);

        if (query.Cursor is { } cursor)
        {
            command.Parameters.AddWithValue("$anchorTime", cursor.LastActiveAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$anchorId", cursor.ThreadId);
            predicates += " AND (t.updated_at < $anchorTime OR (t.updated_at = $anchorTime AND t.thread_id < $anchorId))";
        }
        command.Parameters.AddWithValue("$limit", query.Limit is { } limit ? (long)limit + 1 : -1);
        command.CommandText = $"""
            {source}
            SELECT {string.Join(", ", SummaryColumns.Split(',').Select(column => "t." + column.Trim()))}
            FROM summaries t
            WHERE {predicates}
            ORDER BY t.updated_at DESC, t.thread_id DESC
            LIMIT $limit
            """;
        var liveById = liveSummaries.ToDictionary(summary => summary.Id, StringComparer.Ordinal);
        var items = new List<ThreadSummary>();
        var hasMore = false;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (query.Limit is { } pageSize && items.Count == pageSize)
                {
                    hasMore = true;
                    break;
                }
                items.Add(liveById.TryGetValue(reader.GetString(0), out var live) ? live : ReadSummary(reader));
            }
        }
        transaction.Commit();
        return new ThreadListPage(items, total, hasMore);
    }

    private static string BuildSummarySource(SqliteCommand command, IReadOnlyList<ThreadSummary> liveSummaries)
    {
        if (liveSummaries.Count == 0)
            return $"WITH summaries AS (SELECT {SummaryColumns} FROM threads)";

        var rows = liveSummaries.Select(summary => new
        {
            thread_id = summary.Id,
            user_id = summary.UserId,
            origin_channel = summary.OriginChannel,
            channel_context = summary.ChannelContext,
            display_name = summary.DisplayName,
            status = summary.Status.ToString(),
            created_at = summary.CreatedAt.UtcDateTime.ToString("O"),
            updated_at = summary.LastActiveAt.UtcDateTime.ToString("O"),
            turn_count = summary.TurnCount,
            metadata_json = JsonSerializer.Serialize(summary.Metadata),
            forked_from_id = summary.ForkedFromId,
            ephemeral = summary.Ephemeral ? 1 : 0,
            worktree_json = summary.Worktree is null ? null : JsonSerializer.Serialize(summary.Worktree),
            source_json = JsonSerializer.Serialize(
                PersistedThreadSourceCodec.Encode(summary.Source), SessionPersistenceJsonOptions.Default),
            git_info_json = summary.GitInfo is null ? null : JsonSerializer.Serialize(summary.GitInfo)
        });
        command.Parameters.AddWithValue("$live", JsonSerializer.Serialize(rows));
        var liveColumns = string.Join(", ", SummaryColumns.Split(',')
            .Select(column => $"json_extract(value, '$.{column.Trim()}') AS {column.Trim()}"));
        return $"""
            WITH live AS (SELECT {liveColumns} FROM json_each($live)),
            summaries AS (
                SELECT {SummaryColumns} FROM threads
                WHERE thread_id NOT IN (SELECT thread_id FROM live)
                UNION ALL
                SELECT {SummaryColumns} FROM live
            )
            """;
    }

    private static string BuildListPredicates(
        SqliteCommand command,
        ThreadListQuery query,
        IReadOnlySet<string> excludedThreadIds)
    {
        const string source = "CASE WHEN json_valid(t.source_json) THEN t.source_json END";
        const string child = $"(ordinal_equals(json_extract({source}, '$.kind'), 'subagent') OR ordinal_equals(t.origin_channel, 'subagent'))";
        var predicates = new List<string> { "t.ephemeral = 0" };
        if (excludedThreadIds.Count > 0)
        {
            command.Parameters.AddWithValue("$excluded", JsonSerializer.Serialize(excludedThreadIds));
            predicates.Add("t.thread_id NOT IN (SELECT value FROM json_each($excluded))");
        }
        if (!query.IncludeArchived)
        {
            predicates.Add("t.status != 'Archived'");
            if (query.IncludeSubAgents)
                predicates.Add($"""
                    NOT ({child} AND EXISTS (
                        SELECT 1 FROM summaries parent
                        WHERE parent.thread_id = COALESCE(
                            NULLIF(trim(json_extract({source}, '$.subAgent.parentThreadId')), ''),
                            NULLIF(trim(t.channel_context), ''))
                          AND parent.status = 'Archived'))
                    """);
        }
        if (!query.IncludeSubAgents)
            predicates.Add($"NOT {child}");
        if (!query.IncludeInternal)
        {
            command.Parameters.AddWithValue("$internalKey", "$.\"" + ThreadVisibility.InternalMetadataKey + "\"");
            command.Parameters.AddWithValue("$dreams", DotCraft.Dreams.DreamsConstants.ChannelName);
            command.Parameters.AddWithValue("$commitMessages", CommitMessageSuggestConstants.ChannelName);
            predicates.Add("""
                trim(COALESCE(json_extract(
                    CASE WHEN json_valid(t.metadata_json) THEN t.metadata_json END, $internalKey), '')) = ''
                AND NOT ordinal_equals(t.origin_channel, $dreams)
                AND NOT ordinal_equals(t.origin_channel, $commitMessages)
                """);
        }
        if (query.Scope == ThreadDiscoveryScope.Identity)
        {
            command.Parameters.AddWithValue("$user", (object?)query.Identity.UserId ?? DBNull.Value);
            command.Parameters.AddWithValue("$context", (object?)query.Identity.ChannelContext ?? DBNull.Value);
            var identity = "($user IS NULL OR t.user_id = $user) AND t.channel_context IS $context";
            if (query.IncludeSubAgents)
                identity = $"({identity}) OR ({child} AND ($user IS NULL OR t.user_id = $user))";
            if (query.CrossChannelOrigins is { Count: > 0 } origins)
            {
                command.Parameters.AddWithValue("$origins", JsonSerializer.Serialize(origins));
                identity = $"({identity}) OR EXISTS (SELECT 1 FROM json_each($origins) WHERE ordinal_equals(value, t.origin_channel))";
            }
            predicates.Add($"({identity})");
        }
        if (!string.IsNullOrEmpty(query.ChannelName))
        {
            command.Parameters.AddWithValue("$channel", query.ChannelName);
            predicates.Add("ordinal_equals(t.origin_channel, $channel)");
        }
        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            command.Parameters.AddWithValue("$query", query.Query.Trim());
            predicates.Add("""
                (ordinal_contains(t.thread_id, $query) OR ordinal_contains(t.display_name, $query)
                 OR ordinal_contains(t.origin_channel, $query) OR ordinal_contains(t.channel_context, $query)
                 OR ordinal_contains(t.status, $query))
                """);
        }
        return string.Join(" AND ", predicates.Select(predicate => $"({predicate})"));
    }
}
