using System.Text.Json;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadAttachmentCleanupTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AttachmentCleanup_" + Guid.NewGuid().ToString("N"));
    private ThreadStore _store;

    public ThreadAttachmentCleanupTests() => _store = new ThreadStore(_root);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        _store.StateDatabase.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task DeleteAfterDatabaseRecovery_KeepsSharedAttachmentsWithoutProjectingOtherThreads(
        bool listFirst, bool archiveOther)
    {
        var attachment = CreateAttachment();
        await _store.SaveThreadAsync(CreateThread("first", attachment));
        var other = CreateThread("other", attachment);
        other.Status = archiveOther ? ThreadStatus.Archived : ThreadStatus.Active;
        await _store.SaveThreadAsync(other);
        await RebuildDatabaseAsync();
        if (listFirst)
            await _store.LoadIndexAsync();

        _store.DeleteThread("first");

        Assert.True(File.Exists(attachment));
        Assert.Equal(0, AttachmentReferences("other"));
        await _store.ReadThreadSnapshotAsync("other");
        Assert.Equal(1, AttachmentReferences("other"));

        _store.DeleteThread("other");
        Assert.False(File.Exists(attachment));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MutationAfterDatabaseRecovery_KeepsSharedAttachmentsWhenOtherReferencesAreUnprojected(bool rollback)
    {
        var attachment = CreateAttachment();
        var first = CreateThread("first", attachment);
        await _store.SaveThreadAsync(first);
        await _store.SaveThreadAsync(CreateThread("other", attachment));
        await RebuildDatabaseAsync();
        await _store.LoadIndexAsync();

        if (rollback)
        {
            first.Turns.Clear();
            await _store.RollbackThreadAsync(first, 1);
        }
        else
        {
            first.Turns[0].Input!.Payload = new UserMessagePayload { Text = "Updated input" };
            await _store.SaveThreadAsync(first);
        }

        Assert.True(File.Exists(attachment));
        Assert.Equal(0, AttachmentReferences("first"));
        Assert.Equal(0, AttachmentReferences("other"));
        await _store.ReadThreadSnapshotAsync("other");
        Assert.Equal(1, AttachmentReferences("other"));
    }

    [Fact]
    public async Task Delete_KeepsAnAttachmentIntroducedByAnUnprojectedExternalAppend()
    {
        var attachment = CreateAttachment();
        await _store.SaveThreadAsync(CreateThread("first", attachment));
        await _store.SaveThreadAsync(CreateThread("other"));
        await _store.FlushAndCloseAsync();
        var append = new ThreadRolloutRecord
        {
            Kind = RolloutKinds.ItemAppended,
            ItemAppended = new ItemAppendedPayload
            {
                TurnId = "turn_001",
                Item = new SessionItem
                {
                    Id = "external_input",
                    TurnId = "turn_001",
                    Type = ItemType.UserMessage,
                    Payload = new UserMessagePayload
                    {
                        Text = "External input",
                        NativeInputParts = [new SessionInputPart { Type = "localImage", Path = attachment }]
                    }
                }
            }
        };
        await File.AppendAllTextAsync(Path.Combine(_root, "threads", "active", "other.jsonl"),
            JsonSerializer.Serialize(append, SessionJsonOptions.Default) + "\n");

        _store.DeleteThread("first");

        Assert.True(File.Exists(attachment));
        Assert.Equal(0, AttachmentReferences("other"));
        await _store.ReadThreadSnapshotAsync("other");
        Assert.Equal(1, AttachmentReferences("other"));
    }

    private async Task RebuildDatabaseAsync()
    {
        await _store.DisposeAsync();
        _store.StateDatabase.Dispose();
        foreach (var path in Directory.EnumerateFiles(_root, "state.db*"))
            File.Delete(path);
        _store = new ThreadStore(_root);
    }

    private long AttachmentReferences(string threadId)
    {
        using var connection = _store.StateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM thread_attachments WHERE thread_id = $id";
        command.Parameters.AddWithValue("$id", threadId);
        return (long)command.ExecuteScalar()!;
    }

    private string CreateAttachment()
    {
        var folder = Path.Combine(_root, "attachments", "images");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "shared.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));
        return path;
    }

    private static SessionThread CreateThread(string id, string? attachment = null)
    {
        var timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var input = new SessionItem
        {
            Id = "input",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = timestamp,
            Payload = new UserMessagePayload
            {
                Text = "Input",
                NativeInputParts = attachment is null
                    ? []
                    : [new SessionInputPart { Type = "localImage", Path = attachment }]
            }
        };
        return new SessionThread
        {
            Id = id,
            UserId = "local",
            OriginChannel = "test",
            CreatedAt = timestamp,
            LastActiveAt = timestamp,
            HistoryMode = HistoryMode.Server,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = id,
                    Status = TurnStatus.Completed,
                    StartedAt = timestamp,
                    CompletedAt = timestamp,
                    Input = input,
                    Items = [input]
                }
            ]
        };
    }
}
