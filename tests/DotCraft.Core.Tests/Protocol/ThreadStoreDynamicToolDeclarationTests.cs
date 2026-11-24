using System.Text.Json;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadStoreDynamicToolDeclarationTests
{
    [Fact]
    public async Task CommitDynamicToolDeclarations_WhenCancelledAfterEnqueue_StillPublishesThePersistedUpdate()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var writer = new CancelAfterAppendWriter();
            using var database = new WorkspaceStateDatabase(directory.FullName);
            await using var store = new ThreadStore(directory.FullName, database, null, writer);
            var thread = new SessionThread
            {
                Id = SessionIdGenerator.NewThreadId(),
                WorkspacePath = directory.FullName,
                CreatedAt = DateTimeOffset.UtcNow,
                LastActiveAt = DateTimeOffset.UtcNow
            };
            await store.SaveThreadAsync(thread);
            writer.CancelAfterAppend = cancellation;

            await store.CommitDynamicToolDeclarationsAsync(thread,
                [new(null, null, "SaveNote", "Save a note", JsonSerializer.SerializeToElement(new { type = "object" }), false, null)],
                cancellation.Token);

            Assert.True(cancellation.IsCancellationRequested);
            var loaded = Assert.IsType<SessionThread>(await store.LoadThreadAsync(thread.Id));
            Assert.Equal("SaveNote", Assert.Single(thread.DynamicToolDeclarations).Name);
            Assert.Equal("SaveNote", Assert.Single(loaded.DynamicToolDeclarations).Name);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class CancelAfterAppendWriter : IRolloutWriter
    {
        private readonly OrderedRolloutWriter _inner = new();

        public CancellationTokenSource? CancelAfterAppend { get; set; }

        public async Task AddBatchAsync(string threadId, string path, IReadOnlyList<ThreadRolloutRecord> records,
            CancellationToken ct = default)
        {
            await _inner.AddBatchAsync(threadId, path, records, ct);
            CancelAfterAppend?.Cancel();
        }

        public Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default) =>
            _inner.FlushAsync(threadId, ct);

        public Task CloseAsync(string threadId, CancellationToken ct = default, bool discardPending = false) =>
            _inner.CloseAsync(threadId, ct, discardPending);

        public Task ShutdownAllAsync(CancellationToken ct = default) => _inner.ShutdownAllAsync(ct);
    }
}
