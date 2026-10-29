using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed record ThreadReadStateUpdateOutcome(bool Applied, ThreadReadState State);

/// <summary>Stores one read receipt per thread; a missing row reads as read with the initial revision.</summary>
internal sealed class ThreadReadStateStore(WorkspaceStateDatabase database)
{
    public const string InitialRevision = "0";

    private const string ThreadStartMarker = "";
    private const int BatchSize = 500;

    public ThreadReadState Load(string threadId)
    {
        using var connection = database.OpenConnection();
        return Load(connection, null, threadId);
    }

    public IReadOnlyDictionary<string, ThreadReadState> LoadMany(IReadOnlyCollection<string> threadIds)
    {
        var result = new Dictionary<string, ThreadReadState>(StringComparer.Ordinal);
        if (threadIds.Count == 0)
            return result;

        using var connection = database.OpenConnection();
        foreach (var batch in threadIds.Distinct(StringComparer.Ordinal).Chunk(BatchSize))
        {
            using var command = connection.CreateCommand();
            var names = new string[batch.Length];
            for (var i = 0; i < batch.Length; i++)
            {
                names[i] = "$id" + i;
                command.Parameters.AddWithValue(names[i], batch[i]);
            }

            command.CommandText = $"""
                SELECT thread_id, first_unread_turn_id, revision
                FROM thread_read_receipts
                WHERE thread_id IN ({string.Join(", ", names)})
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = ReadState(reader, 1);
        }

        foreach (var threadId in threadIds)
            result.TryAdd(threadId, Initial);
        return result;
    }

    /// <summary>Records a Turn's new result once; returns null when that Turn was already applied.</summary>
    public ThreadReadState? ApplyNewResult(string threadId, string turnId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_read_receipts (thread_id, first_unread_turn_id, revision, last_result_turn_id, updated_at)
            VALUES ($thread_id, $turn_id, $revision, $turn_id, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                first_unread_turn_id = COALESCE(first_unread_turn_id, excluded.first_unread_turn_id),
                revision = excluded.revision,
                last_result_turn_id = excluded.last_result_turn_id,
                updated_at = excluded.updated_at
            WHERE last_result_turn_id IS NULL OR last_result_turn_id <> excluded.last_result_turn_id
            RETURNING first_unread_turn_id, revision
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        command.Parameters.AddWithValue("$revision", NewRevision());
        command.Parameters.AddWithValue("$updated_at", Now());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadState(reader, 0) : null;
    }

    public ThreadReadStateUpdateOutcome Update(
        string threadId,
        string expectedRevision,
        ThreadReadStateOperation operation)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var current = Load(connection, transaction, threadId);
        if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
            return new ThreadReadStateUpdateOutcome(false, current);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO thread_read_receipts (thread_id, first_unread_turn_id, revision, last_result_turn_id, updated_at)
            VALUES ($thread_id, $first_unread, $revision, NULL, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                first_unread_turn_id = excluded.first_unread_turn_id,
                revision = excluded.revision,
                updated_at = excluded.updated_at
            """;
        var next = new ThreadReadState(
            operation == ThreadReadStateOperation.Unread ? ThreadFirstUnread.ThreadStart : null,
            NewRevision());
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$first_unread", ToStorage(next.FirstUnread));
        command.Parameters.AddWithValue("$revision", next.Revision);
        command.Parameters.AddWithValue("$updated_at", Now());
        command.ExecuteNonQuery();
        transaction.Commit();
        return new ThreadReadStateUpdateOutcome(true, next);
    }

    /// <summary>Forgets applied results and marks the thread read when its first unread Turn was removed.</summary>
    public ThreadReadState? ResetAfterRollback(string threadId, string? removedFirstUnreadTurnId)
    {
        var revision = NewRevision();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE thread_read_receipts
            SET revision = CASE WHEN first_unread_turn_id = $removed THEN $revision ELSE revision END,
                first_unread_turn_id = CASE WHEN first_unread_turn_id = $removed THEN NULL ELSE first_unread_turn_id END,
                last_result_turn_id = NULL,
                updated_at = $updated_at
            WHERE thread_id = $thread_id
            RETURNING first_unread_turn_id, revision
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$removed", (object?)removedFirstUnreadTurnId ?? DBNull.Value);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$updated_at", Now());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        var state = ReadState(reader, 0);
        return state.Revision == revision ? state : null;
    }

    public void Delete(string threadId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thread_read_receipts WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }

    private static ThreadReadState Initial { get; } = new(null, InitialRevision);

    private static ThreadReadState Load(SqliteConnection connection, SqliteTransaction? transaction, string threadId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT first_unread_turn_id, revision
            FROM thread_read_receipts
            WHERE thread_id = $thread_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadState(reader, 0) : Initial;
    }

    private static ThreadReadState ReadState(SqliteDataReader reader, int offset)
    {
        var revision = reader.GetString(offset + 1);
        if (reader.IsDBNull(offset))
            return new ThreadReadState(null, revision);
        var turnId = reader.GetString(offset);
        return new ThreadReadState(
            turnId == ThreadStartMarker ? ThreadFirstUnread.ThreadStart : new ThreadFirstUnread(turnId),
            revision);
    }

    private static object ToStorage(ThreadFirstUnread? firstUnread) =>
        firstUnread is null ? DBNull.Value : firstUnread.TurnId ?? ThreadStartMarker;

    private static string NewRevision() => Guid.NewGuid().ToString("N");

    private static string Now() => DateTimeOffset.UtcNow.UtcDateTime.ToString("O");
}
