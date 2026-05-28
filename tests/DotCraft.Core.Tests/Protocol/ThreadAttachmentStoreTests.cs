using DotCraft.Protocol;

namespace DotCraft.Tests.Protocol;

public sealed class ThreadAttachmentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ThreadAttachmentStoreTests_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ThreadStore _store;

    public ThreadAttachmentStoreTests()
    {
        _store = new ThreadStore(_root);
    }

    [Fact]
    public async Task DeleteThread_RemovesUnreferencedManagedAttachment()
    {
        var imagePath = CreateAttachment("one.png");
        var thread = CreateThread("thread_with_attachment", imagePath);
        await _store.SaveThreadAsync(thread);

        _store.DeleteThread(thread.Id);

        Assert.False(File.Exists(imagePath));
    }

    [Fact]
    public async Task DeleteThread_KeepsAttachmentReferencedByAnotherThread()
    {
        var imagePath = CreateAttachment("shared.png");
        var first = CreateThread("thread_first_attachment", imagePath);
        var second = CreateThread("thread_second_attachment", imagePath);
        await _store.SaveThreadAsync(first);
        await _store.SaveThreadAsync(second);

        _store.DeleteThread(first.Id);
        Assert.True(File.Exists(imagePath));

        _store.DeleteThread(second.Id);
        Assert.False(File.Exists(imagePath));
    }

    [Fact]
    public async Task ArchiveThread_KeepsAttachmentReferenceAndFile()
    {
        var imagePath = CreateAttachment("archived.png");
        var thread = CreateThread("thread_archived_attachment", imagePath);
        await _store.SaveThreadAsync(thread);

        thread.Status = ThreadStatus.Archived;
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread);

        Assert.True(File.Exists(imagePath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort on Windows.
        }
    }

    private string CreateAttachment(string fileName)
    {
        var dir = Path.Combine(_root, "attachments", "images");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private static SessionThread CreateThread(string id, string imagePath)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload
            {
                Text = "Image",
                NativeInputParts =
                [
                    new SessionWireInputPart
                    {
                        Type = "localImage",
                        Path = imagePath,
                        MimeType = "image/png",
                        FileName = Path.GetFileName(imagePath)
                    }
                ],
                MaterializedInputParts =
                [
                    new SessionWireInputPart
                    {
                        Type = "localImage",
                        Path = imagePath,
                        MimeType = "image/png",
                        FileName = Path.GetFileName(imagePath)
                    }
                ]
            }
        };

        return new SessionThread
        {
            Id = id,
            WorkspacePath = Path.Combine(Path.GetTempPath(), "workspace"),
            UserId = "local",
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            CreatedAt = now,
            LastActiveAt = now,
            HistoryMode = HistoryMode.Server,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = id,
                    Status = TurnStatus.Completed,
                    StartedAt = now,
                    CompletedAt = now,
                    Input = user,
                    Items = [user]
                }
            ]
        };
    }
}
