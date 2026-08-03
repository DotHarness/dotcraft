using DotCraft.Persistence;
using DotCraft.Protocol;
using Microsoft.Data.Sqlite;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentMailboxSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "SubAgentMailboxSchemaTests_" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public void Initialize_ExistingMailboxTable_AddsTypedProvenanceWithMessageDefault()
    {
        Directory.CreateDirectory(_root);
        var databasePath = Path.Combine(_root, "state.db");
        using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE subagent_mailbox_entries (
                    id TEXT PRIMARY KEY,
                    root_thread_id TEXT NOT NULL,
                    sender_agent_path TEXT NOT NULL,
                    target_agent_path TEXT NOT NULL,
                    message TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    delivered_at TEXT
                );
                INSERT INTO subagent_mailbox_entries (
                    id, root_thread_id, sender_agent_path, target_agent_path,
                    message, status, created_at, delivered_at
                ) VALUES (
                    'existing-message', 'root-thread', '/root/worker', '/root',
                    'existing payload',
                    'pending', '2026-08-03T00:00:00.0000000Z', NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var state = new WorkspaceStateDatabase(_root);
        var store = new ThreadMetadataStore(state);
        var entry = Assert.Single(store.ListPendingSubAgentMailbox("root-thread", AgentPath.Root));

        Assert.Equal("MESSAGE", entry.MessageType);
        Assert.Null(entry.ParentTurnId);

        using var migratedConnection = state.OpenConnection();
        using var columnsCommand = migratedConnection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info(subagent_mailbox_entries);";
        using var reader = columnsCommand.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
            columns.Add(reader.GetString(1));

        Assert.Contains("message_type", columns);
        Assert.Contains("parent_turn_id", columns);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
