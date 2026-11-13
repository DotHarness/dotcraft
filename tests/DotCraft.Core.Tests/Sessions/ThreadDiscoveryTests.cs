using System.Text;
using System.Text.Json;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ThreadDiscoveryTests_" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceStateDatabase _database;

    public ThreadDiscoveryTests() => _database = new WorkspaceStateDatabase(_root);

    [Fact]
    public async Task OlderIndexes_ListingDoesNotRepairHistoryAndReadingRepairsOnlyTheSelectedThread()
    {
        await using var store = new ThreadStore(_root, _database);
        var firstAttachment = CreateAttachment("first.png");
        var secondAttachment = CreateAttachment("second.png");
        await store.SaveThreadAsync(CreateThread("thread_first", firstAttachment));
        await store.SaveThreadAsync(CreateThread("thread_second", secondAttachment));
        Execute("""
            UPDATE threads SET attachment_index_version = 2;
            DELETE FROM thread_turns;
            DELETE FROM thread_attachments;
            """);
        var projectedBytes = store.ProjectedRolloutBytes;

        var summaries = await store.LoadIndexAsync();

        Assert.Equal(2, summaries.Count);
        Assert.All(summaries, summary => Assert.Equal(1, summary.TurnCount));
        Assert.Equal(projectedBytes, store.ProjectedRolloutBytes);
        Assert.Equal(0, ReadLong("SELECT COUNT(*) FROM thread_turns"));
        Assert.Equal(0, ReadLong("SELECT COUNT(*) FROM thread_items"));
        Assert.Equal(0, ReadLong("SELECT COUNT(*) FROM thread_attachments"));
        Assert.Equal(2, ReadLong("SELECT COUNT(*) FROM threads WHERE attachment_index_version = 2"));
        Assert.True(File.Exists(firstAttachment));
        Assert.True(File.Exists(secondAttachment));

        await store.ReadThreadSnapshotAsync("thread_first");

        Assert.True(store.ProjectedRolloutBytes > projectedBytes);
        Assert.Equal(1, ReadLong("SELECT COUNT(*) FROM thread_turns WHERE thread_id = 'thread_first'"));
        Assert.Equal(1, ReadLong("SELECT COUNT(*) FROM thread_items WHERE thread_id = 'thread_first'"));
        Assert.Equal(1, ReadLong("SELECT COUNT(*) FROM thread_attachments WHERE thread_id = 'thread_first'"));
        Assert.Equal(0, ReadLong("SELECT COUNT(*) FROM thread_attachments WHERE thread_id = 'thread_second'"));
        Assert.Equal(0, ReadLong("SELECT COUNT(*) FROM thread_turns WHERE thread_id = 'thread_second'"));
        Assert.Equal(2, ReadLong("SELECT attachment_index_version FROM threads WHERE thread_id = 'thread_second'"));
    }

    [Fact]
    public async Task SummaryRepair_AppliesNameStatusAndRollbackWithoutAdvancingTheHistoryCheckpoint()
    {
        await using var store = new ThreadStore(_root, _database);
        const string id = "thread_summary_repair";
        var thread = CreateThread(id);
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_002",
            ThreadId = id,
            Status = TurnStatus.Completed,
            StartedAt = thread.CreatedAt.AddMinutes(1)
        });
        await store.SaveThreadAsync(thread);
        await store.FlushAndCloseAsync();
        var checkpoint = ReadLong("SELECT projected_rollout_offset FROM thread_history_projection_state");
        var projectedBytes = store.ProjectedRolloutBytes;
        var path = new ThreadRolloutStore(_root).ResolveExistingPath(id)!;
        var changedAt = thread.CreatedAt.AddMinutes(5);
        ThreadRolloutRecord[] suffix =
        [
            new()
            {
                Kind = RolloutKinds.ThreadNameUpdated,
                ThreadNameUpdated = new ThreadNameUpdatedPayload { ThreadId = id, DisplayName = "Recovered title" }
            },
            new()
            {
                Kind = RolloutKinds.ThreadRolledBack,
                ThreadRolledBack = new ThreadRolledBackPayload { ThreadId = id, NumTurns = 1, LastActiveAt = changedAt }
            },
            new()
            {
                Kind = RolloutKinds.ThreadStatusChanged,
                ThreadStatusChanged = new RolloutThreadStatusChangedPayload
                {
                    ThreadId = id, Status = ThreadStatus.Archived, LastActiveAt = changedAt
                }
            }
        ];
        await File.AppendAllTextAsync(path,
            string.Concat(suffix.Select(record => JsonSerializer.Serialize(record, SessionJsonOptions.Default) + "\n")),
            new UTF8Encoding(false));

        var summary = Assert.Single(await store.LoadIndexAsync());

        Assert.Equal("Recovered title", summary.DisplayName);
        Assert.Equal(ThreadStatus.Archived, summary.Status);
        Assert.Equal(changedAt, summary.LastActiveAt);
        Assert.Equal(1, summary.TurnCount);
        Assert.Equal(new FileInfo(path).Length, ReadLong("SELECT projected_rollout_offset FROM threads"));
        Assert.Equal(checkpoint, ReadLong("SELECT projected_rollout_offset FROM thread_history_projection_state"));
        Assert.Equal(2, ReadLong("SELECT COUNT(*) FROM thread_turns"));
        Assert.Equal(projectedBytes, store.ProjectedRolloutBytes);

        var turns = await store.ListThreadTurnsAsync(id, null, 10, ThreadHistorySortDirection.Ascending);

        Assert.Equal("turn_001", Assert.Single(turns.Data).Id);
        Assert.Equal(new FileInfo(path).Length, ReadLong("SELECT projected_rollout_offset FROM thread_history_projection_state"));
    }

    [Fact]
    public async Task RepeatedListing_DoesNotSweepAttachmentsWhileHistoryIsBehindTheRepairedSummary()
    {
        await using var store = new ThreadStore(_root, _database);
        const string id = "thread_attachment_checkpoint";
        await store.SaveThreadAsync(CreateThread(id));
        await store.FlushAndCloseAsync();
        var attachment = CreateAttachment("unprojected.png");
        var record = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ItemAppended,
            ItemAppended = new ItemAppendedPayload
            {
                TurnId = "turn_001",
                Item = new SessionItem
                {
                    Id = "item_external", TurnId = "turn_001", Type = ItemType.UserMessage,
                    Payload = new UserMessagePayload
                    {
                        Text = "External attachment",
                        NativeInputParts = [new SessionInputPart { Type = "localImage", Path = attachment }]
                    }
                }
            }
        };
        var path = new ThreadRolloutStore(_root).ResolveExistingPath(id)!;
        await File.AppendAllTextAsync(path, JsonSerializer.Serialize(record, SessionJsonOptions.Default) + "\n", new UTF8Encoding(false));
        var projectedBytes = store.ProjectedRolloutBytes;

        await store.LoadIndexAsync();
        await store.LoadIndexAsync();

        Assert.True(File.Exists(attachment));
        Assert.Equal(0, ReadLong("SELECT COUNT(*) FROM thread_attachments"));
        Assert.Equal(projectedBytes, store.ProjectedRolloutBytes);

        await store.ReadThreadSnapshotAsync(id);

        Assert.Equal(1, ReadLong("SELECT COUNT(*) FROM thread_attachments"));
    }

    [Fact]
    public async Task MatchingSummary_DoesNotOpenTheRolloutEvenWithAnOlderAttachmentIndex()
    {
        await using var store = new ThreadStore(_root, _database);
        const string id = "thread_locked_history";
        await store.SaveThreadAsync(CreateThread(id));
        await store.FlushAndCloseAsync();
        Execute("UPDATE threads SET attachment_index_version = 2");
        var path = new ThreadRolloutStore(_root).ResolveExistingPath(id)!;
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(id, Assert.Single(await store.LoadIndexAsync()).Id);
    }

    public void Dispose()
    {
        _database.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private void Execute(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long ReadLong(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private string CreateAttachment(string name)
    {
        var directory = Path.Combine(_root, "attachments", "images");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        return path;
    }

    private static SessionThread CreateThread(string id, string? attachment = null)
    {
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var user = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = createdAt,
            Payload = new UserMessagePayload
            {
                Text = "Request",
                NativeInputParts = attachment is null ? [] : [new SessionInputPart { Type = "localImage", Path = attachment }]
            }
        };
        return new SessionThread
        {
            Id = id,
            UserId = "local",
            OriginChannel = "test",
            Source = ThreadSource.User(),
            CreatedAt = createdAt,
            LastActiveAt = createdAt,
            Status = ThreadStatus.Active,
            HistoryMode = HistoryMode.Server,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001", ThreadId = id, Status = TurnStatus.Completed,
                    StartedAt = createdAt, CompletedAt = createdAt, Input = user, Items = [user]
                }
            ]
        };
    }
}
