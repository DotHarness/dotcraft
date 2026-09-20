using System.Text.Json;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class MemoryConsolidationConflictTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MemoryConflict_" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consolidation_FileEditSkipsStaleCandidate(bool fork)
    {
        var store = new MemoryStore(_root);
        store.WriteLongTerm("original");
        store.AppendHistory("existing");
        var client = new CallbackClient(async () =>
        {
            await new FileTools(_root).EditFile(store.LongTermFilePath, "original", "new user memory");
            return Candidate("stale candidate", "must-not-append");
        });

        var result = await RunAsync(store, client, fork);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("memory_version_conflict", result.Message);
        Assert.Equal(1, client.Calls);
        Assert.Equal("new user memory", store.ReadLongTerm());
        Assert.Equal("existing\n\n", store.ReadHistory());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Consolidation_ResetInvalidatesEvenAnEmptyStore(bool fork, bool populated)
    {
        var store = new MemoryStore(_root);
        if (populated)
            store.WriteLongTerm("old");
        var client = new CallbackClient(() =>
        {
            new MemoryStore(_root).ClearAll();
            return Task.FromResult(Candidate("must-not-return", "must-not-append"));
        });

        var result = await RunAsync(store, client, fork);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("memory_reset", result.Message);
        Assert.Equal(1, client.Calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.MemoryDirectoryPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consolidation_ConcurrentRunSkipsStaleCandidate(bool fork)
    {
        var store = new MemoryStore(_root);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstClient = new CallbackClient(async () =>
        {
            started.SetResult();
            await release.Task;
            return Candidate("first", "first-event");
        });
        var first = RunAsync(store, firstClient, fork);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var secondClient = new CallbackClient(() => Task.FromResult(Candidate("second", "second-event")));
            var second = await RunAsync(new MemoryStore(_root), secondClient, fork);
            Assert.Equal(MemoryConsolidationOutcome.Succeeded, second.Outcome);
        }
        finally
        {
            release.SetResult();
        }
        var result = await first;

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("memory_version_conflict", result.Message);
        Assert.Equal(1, firstClient.Calls);
        Assert.Equal("second", store.ReadLongTerm());
        Assert.Equal("second-event\n\n", store.ReadHistory());
    }

    [Fact]
    public async Task Fork_ResetBeforeInvalidResponseDoesNotStartFallback()
    {
        var store = new MemoryStore(_root);
        var client = new CallbackClient(() =>
        {
            store.ClearAll();
            return Task.FromResult("invalid");
        });

        var result = await RunAsync(store, client, fork: true);

        Assert.Equal("memory_reset", result.Message);
        Assert.Equal(1, client.Calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.MemoryDirectoryPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consolidation_CancellationBeforeCommitDoesNotWrite(bool fork)
    {
        var store = new MemoryStore(_root);
        using var cancellation = new CancellationTokenSource();
        var client = new CallbackClient(() =>
        {
            cancellation.Cancel();
            return Task.FromResult(Candidate("must-not-save", "must-not-append"));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(store, client, fork, cancellation.Token));
        Assert.Equal(string.Empty, store.ReadLongTerm());
        Assert.Equal(string.Empty, store.ReadHistory());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consolidation_InvalidCandidateDoesNotChangeExistingFiles(bool fork)
    {
        var store = new MemoryStore(_root);
        store.WriteLongTerm("saved");
        store.AppendHistory("existing");
        var client = new CallbackClient(() => Task.FromResult("{\"memory_update\":42,\"history_entry\":\"bad\"}"));

        var result = await RunAsync(store, client, fork);

        Assert.Equal(MemoryConsolidationOutcome.Skipped, result.Outcome);
        Assert.Equal("saved", store.ReadLongTerm());
        Assert.Equal("existing\n\n", store.ReadHistory());
    }

    private Task<MemoryConsolidationResult> RunAsync(
        MemoryStore store, CallbackClient client, bool fork, CancellationToken cancellationToken = default)
    {
        var fallback = new MemoryConsolidator(client, store);
        ChatMessage[] messages = [new(ChatRole.User, "Remember the test record.")];
        if (!fork)
            return fallback.ConsolidateAsync(messages, cancellationToken);
        var consolidator = new MemoryForkConsolidator(
            new MaintenanceForkRunner(client), fallback, store, "test", "test", workspaceRoot: _root);
        return consolidator.ConsolidateAsync(messages,
            PromptRequestSnapshot.Capture(messages, new ChatOptions { ModelId = "test" }), cancellationToken);
    }

    private static string Candidate(string memory, string history) =>
        JsonSerializer.Serialize(new { memory_update = memory, history_entry = history });

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class CallbackClient(Func<Task<string>> callback) : IChatClient
    {
        public int Calls { get; private set; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new(new ChatMessage(ChatRole.Assistant, await callback()));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, await callback());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
