using DotCraft.State;

namespace DotCraft.Tracing;

internal enum TraceSessionBindingKind
{
    ThreadMain,
    ThreadChild,
    Unbound
}

internal sealed record TraceSessionBinding(
    string SessionKey,
    string? RootThreadId,
    string? ParentSessionKey,
    TraceSessionBindingKind BindingKind,
    DateTimeOffset CreatedAt);

internal sealed class TraceSessionBindingStore(StateRuntime stateRuntime)
{
    public TraceSessionBinding GetOrCreateBinding(string sessionKey, DateTimeOffset? createdAt = null)
    {
        var existing = GetBinding(sessionKey);
        if (existing != null)
            return existing;

        var inferred = InferBinding(sessionKey, createdAt ?? DateTimeOffset.UtcNow);
        UpsertBinding(inferred);
        return inferred;
    }

    public void BindThreadMain(string threadId, DateTimeOffset? createdAt = null)
    {
        var binding = new TraceSessionBinding(
            threadId,
            threadId,
            null,
            TraceSessionBindingKind.ThreadMain,
            createdAt ?? DateTimeOffset.UtcNow);
        UpsertBinding(binding);
    }

    public void BindThreadChild(
        string sessionKey,
        string rootThreadId,
        string parentSessionKey,
        DateTimeOffset? createdAt = null)
    {
        var binding = new TraceSessionBinding(
            sessionKey,
            rootThreadId,
            parentSessionKey,
            TraceSessionBindingKind.ThreadChild,
            createdAt ?? DateTimeOffset.UtcNow);
        UpsertBinding(binding);
    }

    public TraceSessionBinding? GetBinding(string sessionKey)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_key, root_thread_id, parent_session_key, binding_kind, created_at
            FROM trace_session_bindings
            WHERE session_key = $session_key
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$session_key", sessionKey);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return ReadBinding(reader);
    }

    public Dictionary<string, TraceSessionBinding> GetBindings(IEnumerable<string> sessionKeys)
    {
        var keys = sessionKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new Dictionary<string, TraceSessionBinding>(StringComparer.Ordinal);
        if (keys.Length == 0)
            return result;

        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var placeholders = new List<string>(keys.Length);
        for (var i = 0; i < keys.Length; i++)
        {
            var name = $"$k{i}";
            placeholders.Add(name);
            command.Parameters.AddWithValue(name, keys[i]);
        }

        command.CommandText = $"""
            SELECT session_key, root_thread_id, parent_session_key, binding_kind, created_at
            FROM trace_session_bindings
            WHERE session_key IN ({string.Join(", ", placeholders)})
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var binding = ReadBinding(reader);
            result[binding.SessionKey] = binding;
        }

        return result;
    }

    public List<TraceSessionBinding> GetBindingsForRootThread(string rootThreadId)
    {
        var list = new List<TraceSessionBinding>();
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT session_key, root_thread_id, parent_session_key, binding_kind, created_at
            FROM trace_session_bindings
            WHERE root_thread_id = $root_thread_id
            ORDER BY created_at, session_key
            """;
        command.Parameters.AddWithValue("$root_thread_id", rootThreadId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(ReadBinding(reader));
        return list;
    }

    public void DeleteBinding(string sessionKey)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM trace_session_bindings WHERE session_key = $session_key";
        command.Parameters.AddWithValue("$session_key", sessionKey);
        command.ExecuteNonQuery();
    }

    public void DeleteBindings(IEnumerable<string> sessionKeys)
    {
        var keys = sessionKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
            return;

        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var placeholders = new List<string>(keys.Length);
        for (var i = 0; i < keys.Length; i++)
        {
            var name = $"$k{i}";
            placeholders.Add(name);
            command.Parameters.AddWithValue(name, keys[i]);
        }

        command.CommandText = $"DELETE FROM trace_session_bindings WHERE session_key IN ({string.Join(", ", placeholders)})";
        command.ExecuteNonQuery();
    }

    public void DeleteBindingsForRootThread(string rootThreadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM trace_session_bindings WHERE root_thread_id = $root_thread_id";
        command.Parameters.AddWithValue("$root_thread_id", rootThreadId);
        command.ExecuteNonQuery();
    }

    public void DeleteAllBindings()
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM trace_session_bindings";
        command.ExecuteNonQuery();
    }

    private void UpsertBinding(TraceSessionBinding binding)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trace_session_bindings (
                session_key,
                root_thread_id,
                parent_session_key,
                binding_kind,
                created_at
            ) VALUES (
                $session_key,
                $root_thread_id,
                $parent_session_key,
                $binding_kind,
                $created_at
            )
            ON CONFLICT(session_key) DO UPDATE SET
                root_thread_id = excluded.root_thread_id,
                parent_session_key = excluded.parent_session_key,
                binding_kind = excluded.binding_kind
            """;
        command.Parameters.AddWithValue("$session_key", binding.SessionKey);
        command.Parameters.AddWithValue("$root_thread_id", (object?)binding.RootThreadId ?? DBNull.Value);
        command.Parameters.AddWithValue("$parent_session_key", (object?)binding.ParentSessionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$binding_kind", binding.BindingKind.ToStorageValue());
        command.Parameters.AddWithValue("$created_at", binding.CreatedAt.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    private TraceSessionBinding InferBinding(string sessionKey, DateTimeOffset createdAt)
    {
        if (ThreadExists(sessionKey))
        {
            return new TraceSessionBinding(
                sessionKey,
                sessionKey,
                null,
                TraceSessionBindingKind.ThreadMain,
                createdAt);
        }

        var parentSessionKey = TryGetParentSessionKey(sessionKey);
        if (parentSessionKey != null)
        {
            var parentBinding = GetBinding(parentSessionKey);
            if (parentBinding != null && !string.IsNullOrWhiteSpace(parentBinding.RootThreadId))
            {
                return new TraceSessionBinding(
                    sessionKey,
                    parentBinding.RootThreadId,
                    parentSessionKey,
                    TraceSessionBindingKind.ThreadChild,
                    createdAt);
            }

            var rootThreadId = ExtractRootThreadId(sessionKey);
            if (!string.IsNullOrWhiteSpace(rootThreadId) && ThreadExists(rootThreadId))
            {
                return new TraceSessionBinding(
                    sessionKey,
                    rootThreadId,
                    parentSessionKey,
                    TraceSessionBindingKind.ThreadChild,
                    createdAt);
            }
        }

        return new TraceSessionBinding(
            sessionKey,
            null,
            parentSessionKey,
            TraceSessionBindingKind.Unbound,
            createdAt);
    }

    private bool ThreadExists(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM threads WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() != null;
    }

    private static TraceSessionBinding ReadBinding(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        return new TraceSessionBinding(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            TraceSessionBindingKindExtensions.FromStorageValue(reader.GetString(3)),
            DateTimeOffset.Parse(reader.GetString(4)));
    }

    private static string? TryGetParentSessionKey(string sessionKey)
    {
        var idx = sessionKey.LastIndexOf(":sub:", StringComparison.Ordinal);
        if (idx <= 0)
            return null;
        return sessionKey[..idx];
    }

    private static string ExtractRootThreadId(string sessionKey)
    {
        var idx = sessionKey.IndexOf(":sub:", StringComparison.Ordinal);
        return idx > 0 ? sessionKey[..idx] : sessionKey;
    }
}

internal static class TraceSessionBindingKindExtensions
{
    public static string ToStorageValue(this TraceSessionBindingKind kind) => kind switch
    {
        TraceSessionBindingKind.ThreadMain => "threadMain",
        TraceSessionBindingKind.ThreadChild => "threadChild",
        _ => "unbound"
    };

    public static TraceSessionBindingKind FromStorageValue(string value) => value switch
    {
        "threadMain" => TraceSessionBindingKind.ThreadMain,
        "threadChild" => TraceSessionBindingKind.ThreadChild,
        _ => TraceSessionBindingKind.Unbound
    };
}
