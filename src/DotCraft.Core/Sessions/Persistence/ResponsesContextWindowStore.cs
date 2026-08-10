using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed record ResponsesContextWindowRecord(
    string ThreadId,
    string FirstWindowId,
    string? PreviousWindowId,
    string CurrentWindowId,
    long Generation,
    DateTimeOffset UpdatedAt)
{
    public static ResponsesContextWindowRecord Transient(string threadId)
    {
        var now = DateTimeOffset.UtcNow;
        var windowId = CreateWindowId();
        return new ResponsesContextWindowRecord(threadId, windowId, null, windowId, 0, now);
    }

    public static string CreateWindowId() => Guid.CreateVersion7().ToString();
}

internal sealed class ResponsesContextWindowStore(WorkspaceStateDatabase stateRuntime)
{
    public ResponsesContextWindowRecord GetOrCreate(string threadId)
    {
        var normalizedThreadId = NormalizeThreadId(threadId);
        using var connection = stateRuntime.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = Load(connection, transaction, normalizedThreadId);
        if (existing != null)
        {
            transaction.Commit();
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var windowId = ResponsesContextWindowRecord.CreateWindowId();
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO thread_context_windows(
                    thread_id,
                    first_window_id,
                    previous_window_id,
                    current_window_id,
                    generation,
                    updated_at)
                VALUES ($thread_id, $first_window_id, NULL, $current_window_id, 0, $updated_at)
                ON CONFLICT(thread_id) DO NOTHING
                """;
            insert.Parameters.AddWithValue("$thread_id", normalizedThreadId);
            insert.Parameters.AddWithValue("$first_window_id", windowId);
            insert.Parameters.AddWithValue("$current_window_id", windowId);
            insert.Parameters.AddWithValue("$updated_at", now.ToString("O"));
            insert.ExecuteNonQuery();
        }

        var record = Load(connection, transaction, normalizedThreadId)
            ?? new ResponsesContextWindowRecord(normalizedThreadId, windowId, null, windowId, 0, now);
        transaction.Commit();
        return record;
    }

    public ResponsesContextWindowRecord Advance(string threadId)
    {
        var normalizedThreadId = NormalizeThreadId(threadId);
        using var connection = stateRuntime.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = Load(connection, transaction, normalizedThreadId)
            ?? InsertInitial(connection, transaction, normalizedThreadId);

        var now = DateTimeOffset.UtcNow;
        var nextWindowId = ResponsesContextWindowRecord.CreateWindowId();
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE thread_context_windows
                SET previous_window_id = current_window_id,
                    current_window_id = $current_window_id,
                    generation = generation + 1,
                    updated_at = $updated_at
                WHERE thread_id = $thread_id
                """;
            update.Parameters.AddWithValue("$thread_id", normalizedThreadId);
            update.Parameters.AddWithValue("$current_window_id", nextWindowId);
            update.Parameters.AddWithValue("$updated_at", now.ToString("O"));
            update.ExecuteNonQuery();
        }

        var updated = Load(connection, transaction, normalizedThreadId)
            ?? existing with
            {
                PreviousWindowId = existing.CurrentWindowId,
                CurrentWindowId = nextWindowId,
                Generation = existing.Generation + 1,
                UpdatedAt = now
            };
        transaction.Commit();
        return updated;
    }

    public ResponsesContextWindowRecord Reconcile(string threadId, string committedWindowId)
    {
        var normalizedThreadId = NormalizeThreadId(threadId);
        if (string.IsNullOrWhiteSpace(committedWindowId))
            throw new ArgumentException("Committed window id must be non-empty.", nameof(committedWindowId));
        var normalizedWindowId = committedWindowId.Trim();

        using var connection = stateRuntime.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = Load(connection, transaction, normalizedThreadId)
            ?? InsertInitial(connection, transaction, normalizedThreadId);
        if (string.Equals(existing.CurrentWindowId, normalizedWindowId, StringComparison.Ordinal))
        {
            transaction.Commit();
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE thread_context_windows
                SET previous_window_id = current_window_id,
                    current_window_id = $current_window_id,
                    generation = generation + 1,
                    updated_at = $updated_at
                WHERE thread_id = $thread_id
                """;
            update.Parameters.AddWithValue("$thread_id", normalizedThreadId);
            update.Parameters.AddWithValue("$current_window_id", normalizedWindowId);
            update.Parameters.AddWithValue("$updated_at", now.ToString("O"));
            update.ExecuteNonQuery();
        }

        var reconciled = Load(connection, transaction, normalizedThreadId)
            ?? existing with
            {
                PreviousWindowId = existing.CurrentWindowId,
                CurrentWindowId = normalizedWindowId,
                Generation = existing.Generation + 1,
                UpdatedAt = now
            };
        transaction.Commit();
        return reconciled;
    }

    private static ResponsesContextWindowRecord InsertInitial(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId)
    {
        var now = DateTimeOffset.UtcNow;
        var windowId = ResponsesContextWindowRecord.CreateWindowId();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO thread_context_windows(
                thread_id,
                first_window_id,
                previous_window_id,
                current_window_id,
                generation,
                updated_at)
            VALUES ($thread_id, $first_window_id, NULL, $current_window_id, 0, $updated_at)
            """;
        insert.Parameters.AddWithValue("$thread_id", threadId);
        insert.Parameters.AddWithValue("$first_window_id", windowId);
        insert.Parameters.AddWithValue("$current_window_id", windowId);
        insert.Parameters.AddWithValue("$updated_at", now.ToString("O"));
        insert.ExecuteNonQuery();
        return new ResponsesContextWindowRecord(threadId, windowId, null, windowId, 0, now);
    }

    private static ResponsesContextWindowRecord? Load(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId)
    {
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT
                first_window_id,
                previous_window_id,
                current_window_id,
                generation,
                updated_at
            FROM thread_context_windows
            WHERE thread_id = $thread_id
            LIMIT 1
            """;
        select.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = select.ExecuteReader();
        if (!reader.Read())
            return null;

        var updatedAtRaw = reader.GetString(4);
        var updatedAt = DateTimeOffset.TryParse(updatedAtRaw, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
        return new ResponsesContextWindowRecord(
            threadId,
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            updatedAt);
    }

    private static string NormalizeThreadId(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new ArgumentException("Thread id must be non-empty.", nameof(threadId));
        return threadId.Trim();
    }
}
