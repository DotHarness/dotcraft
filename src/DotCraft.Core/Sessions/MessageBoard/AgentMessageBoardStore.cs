using System.Globalization;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class AgentMessageBoardStore(WorkspaceStateDatabase database)
{
    private SqliteConnection Open()
    {
        var connection = database.OpenConnection();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS agent_boards (
                    root_id TEXT PRIMARY KEY, deleted INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE IF NOT EXISTS agent_board_channels (
                    root_id TEXT NOT NULL, name TEXT NOT NULL, description TEXT,
                    author TEXT NOT NULL, created_at TEXT NOT NULL,
                    PRIMARY KEY(root_id,name));
                CREATE TABLE IF NOT EXISTS agent_board_posts (
                    seq INTEGER PRIMARY KEY AUTOINCREMENT, root_id TEXT NOT NULL,
                    id TEXT NOT NULL, channel TEXT NOT NULL, topic TEXT NOT NULL,
                    author TEXT NOT NULL, created_at TEXT NOT NULL, body TEXT NOT NULL,
                    body_search TEXT NOT NULL, request_id TEXT NOT NULL, request_json TEXT NOT NULL,
                    UNIQUE(root_id,id), UNIQUE(root_id,request_id),
                    FOREIGN KEY(root_id,channel) REFERENCES agent_board_channels(root_id,name) ON DELETE CASCADE);
                CREATE INDEX IF NOT EXISTS idx_agent_board_posts_topic ON agent_board_posts(root_id,topic,seq);
                CREATE INDEX IF NOT EXISTS idx_agent_board_posts_channel ON agent_board_posts(root_id,channel,seq);
                CREATE TABLE IF NOT EXISTS agent_board_subscriptions (
                    root_id TEXT NOT NULL, target TEXT NOT NULL, agent TEXT NOT NULL,
                    enabled INTEGER NOT NULL, PRIMARY KEY(root_id,target,agent));
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static void EnsureBoard(SqliteConnection connection, SqliteTransaction transaction, string root)
    {
        using var create = Command(connection, transaction,
            "INSERT OR IGNORE INTO agent_boards(root_id) VALUES($root)", ("$root", root));
        create.ExecuteNonQuery();
        CheckDeleted(connection, transaction, root);
    }

    private static void CheckDeleted(SqliteConnection connection, SqliteTransaction? transaction, string root)
    {
        using var check = Command(connection, transaction,
            "SELECT deleted FROM agent_boards WHERE root_id=$root", ("$root", root));
        if (check.ExecuteScalar() is long value && value != 0)
            throw new BoardException("board_deleted", "The message board's root has been permanently deleted.");
    }

    public void Delete(string root)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = Command(connection, transaction, """
            INSERT INTO agent_boards(root_id,deleted) VALUES($root,1)
                ON CONFLICT(root_id) DO UPDATE SET deleted=1;
            DELETE FROM agent_board_subscriptions WHERE root_id=$root;
            DELETE FROM agent_board_channels WHERE root_id=$root;
            """, ("$root", root));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static BoardPost ReadPost(SqliteDataReader reader) => new(
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)), reader.GetString(5));

    private const string PostColumns = "id,channel,author,topic,created_at,body";

    private static BoardPost LoadPost(SqliteConnection connection, SqliteTransaction? transaction, string root, string? id)
    {
        using var command = Command(connection, transaction,
            $"SELECT {PostColumns} FROM agent_board_posts WHERE root_id=$root AND id=$id", ("$root", root), ("$id", id));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new BoardException("board_not_found", "Post not found in this board.");
        return ReadPost(reader);
    }

    private static string SubscriptionTarget(SqliteConnection connection, SqliteTransaction transaction, string root, BoardRequest request)
    {
        BoardText.Require((request.ChannelName != null) != (request.ThreadId != null), "Supply exactly one of channelName or threadId.");
        if (request.ThreadId != null)
        {
            var post = LoadPost(connection, transaction, root, request.ThreadId);
            BoardText.Require(post.Metadata.ThreadId == request.ThreadId, "threadId must identify a top-level post.");
            return "thread:" + request.ThreadId;
        }
        RequireChannel(connection, transaction, root, request.ChannelName!);
        return "channel:" + request.ChannelName;
    }

    private static void RequireChannel(SqliteConnection connection, SqliteTransaction? transaction, string root, string name)
    {
        using var command = Command(connection, transaction,
            "SELECT 1 FROM agent_board_channels WHERE root_id=$root AND name=$name", ("$root", root), ("$name", name));
        if (command.ExecuteScalar() == null) throw new BoardException("board_not_found", "Channel not found in this board.");
    }

    private static void SetSubscription(SqliteConnection connection, SqliteTransaction transaction,
        string root, string target, string agent, bool enabled, bool explicitChange)
    {
        using var command = Command(connection, transaction,
            "INSERT INTO agent_board_subscriptions(root_id,target,agent,enabled) VALUES($root,$target,$agent,$enabled) " +
            (explicitChange ? "ON CONFLICT(root_id,target,agent) DO UPDATE SET enabled=excluded.enabled" : "ON CONFLICT DO NOTHING"),
            ("$root", root), ("$target", target), ("$agent", agent), ("$enabled", enabled ? 1 : 0));
        command.ExecuteNonQuery();
    }

    public object ChangeSubscription(BoardActor actor, BoardRequest request, string targetAgent)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        EnsureBoard(connection, transaction, actor.RootId);
        var target = SubscriptionTarget(connection, transaction, actor.RootId, request);
        SetSubscription(connection, transaction, actor.RootId, target, targetAgent, request.Subscribe, true);
        using var latest = Command(connection, transaction,
            "SELECT id FROM agent_board_posts WHERE root_id=$root AND " +
            (request.ThreadId != null ? "topic=$target" : "channel=$target") + " ORDER BY seq DESC LIMIT 1",
            ("$root", actor.RootId), ("$target", request.ThreadId ?? request.ChannelName));
        var last = latest.ExecuteScalar() as string;
        var result = new { request.ChannelName, request.ThreadId, TargetAgent = targetAgent, Enabled = request.Subscribe, LastMessageId = last };
        BoardText.RequireFits(result);
        transaction.Commit();
        return result;
    }
}
