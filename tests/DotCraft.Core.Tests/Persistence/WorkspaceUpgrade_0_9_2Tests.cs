using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotCraft.Tests.Persistence;

public sealed class WorkspaceUpgrade_0_9_2Tests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-upgrade-0-9-2-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Upgrade_drops_the_workspace_path_and_superseded_turn_records()
    {
        var craft = Path.Combine(_workspace, ".craft");
        var thread = CreateThread();
        await using (var store = new ThreadStore(craft))
            await store.SaveThreadAsync(thread);

        var rolloutPath = Path.Combine(craft, "threads", "active", $"{thread.Id}.jsonl");
        WriteOldRollout(rolloutPath, thread);
        MakeOldDatabase(craft, thread.Id);
        var expectedTurns = JsonSerializer.Serialize((await new SessionRolloutReader().ReadAsync(rolloutPath))!.Turns);

        await using (var store = new ThreadStore(craft))
        {
            var summary = Assert.Single(await store.LoadIndexAsync());
            Assert.Equal(Path.GetFullPath(_workspace), summary.WorkspacePath);
            var loaded = await store.LoadThreadAsync(thread.Id);
            Assert.Equal(expectedTurns, JsonSerializer.Serialize(loaded!.Turns));
        }

        var rollout = File.ReadAllText(rolloutPath);
        Assert.DoesNotContain("workspacePath", rollout);
        Assert.DoesNotContain(RolloutKinds.ItemAppended, rollout);
        Assert.DoesNotContain(RolloutKinds.TurnStarted, rollout);
        Assert.Contains(RolloutKinds.TurnStateReplaced, rollout);

        using var connection = OpenDatabase(craft);
        Assert.Equal(Path.Combine("threads", "active", $"{thread.Id}.jsonl"), Scalar(connection, "SELECT rollout_path FROM threads"));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM pragma_table_info('threads') WHERE name = 'workspace_path'"));
        Assert.Equal("0.9.2", Scalar(connection, "SELECT value FROM workspace_meta WHERE key = 'data_version'"));
    }

    private SessionThread CreateThread()
    {
        var now = DateTimeOffset.UtcNow;
        var input = new SessionItem
        {
            Id = "item_turn_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = "request" }
        };
        return new SessionThread
        {
            Id = "thread_upgrade",
            WorkspacePath = _workspace,
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            HistoryMode = HistoryMode.Server,
            CreatedAt = now,
            LastActiveAt = now,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = "thread_upgrade",
                    Status = TurnStatus.Completed,
                    StartedAt = now,
                    CompletedAt = now,
                    Input = input,
                    Items = [input]
                }
            ]
        };
    }

    private static void WriteOldRollout(string rolloutPath, SessionThread thread)
    {
        var lines = new List<string>();
        foreach (var line in File.ReadAllLines(rolloutPath))
        {
            var record = JsonNode.Parse(line)!.AsObject();
            var kind = record["kind"]!.GetValue<string>();
            if (kind == RolloutKinds.ThreadOpened)
                record["threadOpened"]!["workspacePath"] = @"C:\old\workspace";
            lines.Add(record.ToJsonString());
            if (kind == RolloutKinds.ItemAppended)
                lines.Add(record.ToJsonString());
        }

        lines.Add(JsonSerializer.Serialize(new ThreadRolloutRecord
        {
            Kind = RolloutKinds.TurnStateReplaced,
            Timestamp = thread.LastActiveAt,
            TurnStateReplaced = new TurnStateReplacedPayload
            {
                ThreadId = thread.Id,
                Turn = thread.Turns[0],
                ThreadStatus = thread.Status,
                LastActiveAt = thread.LastActiveAt
            }
        }, SessionJsonOptions.Default));
        File.WriteAllLines(rolloutPath, lines);
    }

    private static void MakeOldDatabase(string craft, string threadId)
    {
        using (var connection = OpenDatabase(craft))
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                ALTER TABLE threads ADD COLUMN workspace_path TEXT NOT NULL DEFAULT '';
                UPDATE threads SET workspace_path = 'C:\old\workspace', rollout_path = 'C:\old\workspace\.craft\threads\active\{threadId}.jsonl';
                CREATE INDEX idx_threads_workspace_identity ON threads(workspace_path, user_id, channel_context, origin_channel);
                DELETE FROM workspace_meta;
                """;
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    private static SqliteConnection OpenDatabase(string craft)
    {
        var connection = new SqliteConnection($"Data Source={Path.Combine(craft, "state.db")};Pooling=False");
        connection.Open();
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }
}
