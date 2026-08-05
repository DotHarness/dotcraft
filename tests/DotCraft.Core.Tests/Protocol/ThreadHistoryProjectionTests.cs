using System.Text;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadHistoryProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ThreadHistoryProjectionTests_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ThreadStore _store;

    public ThreadHistoryProjectionTests()
    {
        Directory.CreateDirectory(_root);
        _store = new ThreadStore(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void WorkspaceSchema_CreatesHistoryProjectionWithoutLegacySessionBlob()
    {
        using var connection = new WorkspaceStateDatabase(_root).OpenConnection();
        var tables = ReadNames(connection, "table");
        var indexes = ReadNames(connection, "index");

        Assert.Contains("thread_history_projection_state", tables);
        Assert.Contains("thread_turns", tables);
        Assert.Contains("thread_items", tables);
        Assert.DoesNotContain("thread_sessions", tables);
        Assert.Contains("idx_thread_turns_rollout_ordinal", indexes);
        Assert.Contains("idx_thread_items_rollout_ordinal", indexes);
        Assert.Contains("idx_thread_items_turn_rollout_ordinal", indexes);
    }

    [Fact]
    public async Task HistoryPages_AreBoundedOrderedAndProviderNeutral()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 3);
        await _store.SaveThreadAsync(thread);

        var snapshot = await _store.ReadThreadSnapshotAsync(thread.Id);
        Assert.Empty(snapshot.Thread.Turns);

        var newestTurn = await _store.ListThreadTurnsAsync(
            thread.Id, null, 1, ThreadHistorySortDirection.Descending);
        Assert.Equal("turn_002", Assert.Single(newestTurn.Data).Id);
        Assert.NotNull(newestTurn.NextCursor);
        var olderTurn = await _store.ListThreadTurnsAsync(
            thread.Id, newestTurn.NextCursor, 1, ThreadHistorySortDirection.Descending);
        Assert.Equal("turn_001", Assert.Single(olderTurn.Data).Id);
        Assert.Null(olderTurn.NextCursor);

        var newestItems = await _store.ListThreadItemsAsync(
            thread.Id, null, null, 2, ThreadHistorySortDirection.Descending);
        Assert.Equal(["item_002_003", "item_002_002"], newestItems.Data.Select(entry => entry.Item.Id));
        Assert.NotNull(newestItems.NextCursor);

        var turnItems = await _store.ListThreadItemsAsync(
            thread.Id, "turn_001", null, 10, ThreadHistorySortDirection.Ascending);
        Assert.Equal(3, turnItems.Data.Count);
        Assert.All(turnItems.Data, entry => Assert.Equal("turn_001", entry.TurnId));
    }

    [Fact]
    public async Task MissingOrCorruptProjection_RebuildsFromCanonicalRollout()
    {
        var thread = CreateThread(turnCount: 2, itemsPerTurn: 2);
        await _store.SaveThreadAsync(thread);

        using (var connection = new WorkspaceStateDatabase(_root).OpenConnection())
        {
            using var corrupt = connection.CreateCommand();
            corrupt.CommandText = "UPDATE thread_history_projection_state SET thread_snapshot_json = 'not-json' WHERE thread_id = $thread_id";
            corrupt.Parameters.AddWithValue("$thread_id", thread.Id);
            corrupt.ExecuteNonQuery();
        }

        var rebuilt = await _store.ListThreadTurnsAsync(
            thread.Id, null, 10, ThreadHistorySortDirection.Ascending);
        Assert.Equal(["turn_001", "turn_002"], rebuilt.Data.Select(turn => turn.Id));

        using (var connection = new WorkspaceStateDatabase(_root).OpenConnection())
        {
            using var remove = connection.CreateCommand();
            remove.CommandText = "DELETE FROM thread_history_projection_state WHERE thread_id = $thread_id; DELETE FROM thread_turns WHERE thread_id = $thread_id;";
            remove.Parameters.AddWithValue("$thread_id", thread.Id);
            remove.ExecuteNonQuery();
        }

        var items = await _store.ListThreadItemsAsync(
            thread.Id, null, null, 20, ThreadHistorySortDirection.Ascending);
        Assert.Equal(4, items.Data.Count);
    }

    [Fact]
    public async Task ProviderHistoryPayload_IsSkippedWithoutEnteringProjection()
    {
        var thread = CreateThread(turnCount: 1, itemsPerTurn: 1);
        await _store.SaveThreadAsync(thread);
        await _store.FlushAndCloseAsync();
        var rolloutPath = new ThreadRolloutStore(_root).ResolveExistingPath(thread.Id)!;
        const string protectedMarker = "provider-secret-marker";
        var record = "{\"kind\":\"provider_history_items_appended\",\"timestamp\":\"2026-01-01T00:00:10Z\","
            + "\"providerHistoryItemsAppended\":{\"items\":\"" + protectedMarker + "\"}}";
        await File.AppendAllTextAsync(rolloutPath, record + "\n", new UTF8Encoding(false));

        var page = await _store.ListThreadItemsAsync(
            thread.Id, null, null, 10, ThreadHistorySortDirection.Ascending);
        Assert.Single(page.Data);

        using var connection = new WorkspaceStateDatabase(_root).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT projected_rollout_offset, thread_snapshot_json, persisted_runtime_json
            FROM thread_history_projection_state WHERE thread_id = $thread_id
            """;
        command.Parameters.AddWithValue("$thread_id", thread.Id);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(new FileInfo(rolloutPath).Length, reader.GetInt64(0));
        Assert.DoesNotContain(protectedMarker, reader.GetString(1), StringComparison.Ordinal);
        Assert.DoesNotContain(protectedMarker, reader.GetString(2), StringComparison.Ordinal);
    }

    private static SessionThread CreateThread(int turnCount, int itemsPerTurn)
    {
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var thread = new SessionThread
        {
            Id = "thread_history_fixture",
            WorkspacePath = "/workspace/sample",
            OriginChannel = "test",
            Source = ThreadSource.User(),
            Status = ThreadStatus.Active,
            CreatedAt = createdAt,
            LastActiveAt = createdAt,
            HistoryMode = HistoryMode.Server
        };
        for (var turnIndex = 1; turnIndex <= turnCount; turnIndex++)
        {
            var turnId = $"turn_{turnIndex:000}";
            var turn = new SessionTurn
            {
                Id = turnId,
                ThreadId = thread.Id,
                Status = TurnStatus.Completed,
                StartedAt = createdAt.AddSeconds(turnIndex),
                CompletedAt = createdAt.AddSeconds(turnIndex).AddMilliseconds(500)
            };
            for (var itemIndex = 1; itemIndex <= itemsPerTurn; itemIndex++)
            {
                var item = new SessionItem
                {
                    Id = $"item_{turnIndex:000}_{itemIndex:000}",
                    TurnId = turnId,
                    Type = itemIndex == 1 ? ItemType.UserMessage : ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = turn.StartedAt.AddMilliseconds(itemIndex),
                    CompletedAt = turn.StartedAt.AddMilliseconds(itemIndex + 1),
                    Payload = itemIndex == 1
                        ? new UserMessagePayload { Text = $"User {turnIndex}" }
                        : new AgentMessagePayload { Text = $"Agent {turnIndex}.{itemIndex}" }
                };
                turn.Items.Add(item);
                turn.Input ??= itemIndex == 1 ? item : null;
            }
            thread.Turns.Add(turn);
            thread.LastActiveAt = turn.CompletedAt!.Value;
        }
        return thread;
    }

    private static HashSet<string> ReadNames(SqliteConnection connection, string type)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type";
        command.Parameters.AddWithValue("$type", type);
        using var reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
            names.Add(reader.GetString(0));
        return names;
    }
}
